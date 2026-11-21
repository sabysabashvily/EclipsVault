import type { BrowserContext, Page } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { AccountCreation } from './account-creation';
import { savedAdministratorCookies } from './administrator-cookies';
import { refreshAdministratorProof } from './administrator-proof';
import { test as browserTest, expect } from './fixtures';
import { createSecret } from './palette';
import {
  savedProofSetupTimeout,
  waitForSavedAdministratorProofToExpire,
} from './saved-administrator-proof';
import { SecretDetails } from './secret-details';
import { SecretsList } from './secrets-list';
import { currentTotpStep, freshTotp } from './totp';

/** The saved administrator session, once its proof is older than a Secret reveal accepts. */
const test = browserTest.extend<{ staleAdministrator: Page }>({
  staleAdministrator: [
    async ({ page }, use) => {
      await page.context().addCookies(await savedAdministratorCookies());
      await waitForSavedAdministratorProofToExpire(page.request);
      await use(page);
    },
    { timeout: savedProofSetupTimeout },
  ],
});

test.describe.configure({ mode: 'serial' });
const stepUp = 'E2e_Details_Step_Up';
const changed = 'E2e_Details_Changed';
const shared = 'E2e_Details_Shared';
const byKeyboard = 'E2e_Details_By_Keyboard';
const deleted = 'E2e_Details_Deleted';
const recipientUsername = 'e2e-details-recipient';
const detailsPaths = new Map<string, string>();
let recipientCookies: Awaited<ReturnType<BrowserContext['cookies']>> = [];
const rotated = 'Secret rotated. The previous value was archived to version history.';

async function openAsAdministrator(page: Page, secret: string): Promise<SecretDetails> {
  await page.context().addCookies(await savedAdministratorCookies());
  const details = new SecretDetails(page, secret);
  await details.open(detailsPaths.get(secret)!);
  return details;
}

function administratorAuthenticator(): Promise<string> {
  return readFile(
    `${process.env['E2E_TEST_RESULTS'] || 'test-results'}/users-administrator-authenticator.txt`,
    'utf8',
  );
}

// First, so that only the sign-in projects this one depends on have refreshed the saved proof.
test('a stale session reveals a Secret value after an authenticator code, asked once', async ({
  staleAdministrator: page,
  browserHealth,
}) => {
  // The browser logs the refused first reveal's 403 as a failed request.
  browserHealth.expectConsoleError({ text: /status of 403/, url: /\/reveal$/ });
  await createSecret(page, stepUp, 'Secret');
  const details = new SecretDetails(page, stepUp);

  await details.revealValue.click();
  await expect(details.stepUp).toBeVisible();
  await expect(details.authenticatorCode).toBeFocused();
  await details.authenticatorCode.fill(
    await freshTotp(await administratorAuthenticator(), currentTotpStep()),
  );
  await details.verifyAndContinue.click();

  await expect(details.stepUp).toBeHidden();
  await expect(details.revealed('current value')).toBeVisible();
  await expect(page.getByText(`e2e-${stepUp}`, { exact: true })).toBeVisible();
});

test('the administrator makes the secrets and the share recipient', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(180_000);
  await page.context().addCookies(await savedAdministratorCookies());
  for (const secret of [changed, shared, byKeyboard, deleted]) {
    await createSecret(page, secret, 'Internal');
    detailsPaths.set(secret, new URL(page.url()).pathname);
  }
  const password = `E2e-${randomBytes(24).toString('hex')}`;
  await refreshAdministratorProof(page);
  const creation = new AccountCreation(page);
  await creation.open();
  await creation.create({
    firstName: 'Details',
    lastName: 'Recipient',
    username: recipientUsername,
    password,
    clearance: 'Elevated',
    project: 'E2E-SD',
  });
  await expect(page).toHaveURL(/\/Admin\/Users$/);

  const recipient = await (await browserHealth.newContext()).newPage();
  await recipient.goto('/Account/Login');
  await recipient.getByLabel('Username').fill(recipientUsername);
  await recipient.getByLabel('Password', { exact: true }).fill(password);
  await recipient.getByRole('button', { name: 'Continue', exact: true }).click();
  await expect(recipient).toHaveURL(/\/Account\/EnrollTotp$/);
  // eslint-disable-next-line playwright/no-raw-locators -- Enrollment displays the throwaway authenticator seed as unlabelled text.
  const seed = await recipient.locator('.secret-code').innerText();
  await recipient.getByLabel('Confirmation code').fill(await freshTotp(seed));
  await recipient.getByRole('button', { name: 'Confirm enrollment', exact: true }).click();
  await expect(recipient).toHaveURL(/\/Secrets$/);
  recipientCookies = await recipient.context().cookies();
});

test('rotating archives the old value, which its version reveals', async ({ page }) => {
  const details = await openAsAdministrator(page, changed);
  await details.rotateValue.click();
  await details.newValue.fill('e2e-rotated-value');
  await details.changeNote.fill('e2e rotation');
  await details.rotateSecret.click();
  await expect(details.confirmation).toContainText(
    `Rotate '${changed}'? The current value is archived to history and replaced.`,
  );
  await expect(details.confirmation).toContainText('Saved to the audit log');
  await details.confirm('Rotate secret');

  await expect(details.announcement(rotated)).toBeVisible();
  await expect(details.version(1)).toContainText('e2e rotation');
  await details.version(1).getByRole('button', { name: 'Reveal', exact: true }).click();
  await expect(details.revealed('version 1')).toBeVisible();
  await expect(page.getByText(`e2e-${changed}`, { exact: true })).toBeVisible();
});

test('restoring a version makes it the live value again', async ({ page }) => {
  const details = await openAsAdministrator(page, changed);
  await details.version(1).getByRole('button', { name: 'Restore', exact: true }).click();
  await expect(details.confirmation).toContainText(
    'Restore version 1 as the live value? The current value will be archived first.',
  );
  await details.confirm('Restore version');

  await expect(
    details.announcement(
      'Secret reverted to the selected version. The value it replaced was archived.',
    ),
  ).toBeVisible();
  await details.revealValue.click();
  await expect(details.revealed('current value')).toBeVisible();
  await expect(page.getByText(`e2e-${changed}`, { exact: true })).toBeVisible();
});

test('sharing lists the recipient, who can reveal but change nothing', async ({
  page,
  browserHealth,
}) => {
  const details = await openAsAdministrator(page, shared);
  await details.shareWith.fill(recipientUsername);
  await details.shareAccess.click();
  await expect(
    details.announcement(`Access to '${shared}' was shared with ${recipientUsername}.`),
  ).toBeVisible();
  await expect(details.share(recipientUsername)).toContainText('No expiry');

  const recipient = await (await browserHealth.newContext()).newPage();
  await recipient.context().addCookies(recipientCookies);
  const recipientView = new SecretDetails(recipient, shared);
  await recipientView.open(detailsPaths.get(shared)!);
  await expect(recipientView.revealValue).toBeVisible();
  await expect(recipientView.rotateValue).toHaveCount(0);
  await expect(recipientView.delete).toHaveCount(0);
  await expect(recipientView.sharing).toHaveCount(0);
  await recipientView.revealValue.click();
  await expect(recipient.getByText(`e2e-${shared}`, { exact: true })).toBeVisible();
});

test('revoking the share removes it from the list', async ({ page }) => {
  const details = await openAsAdministrator(page, shared);
  await details
    .share(recipientUsername)
    .getByRole('button', { name: 'Revoke', exact: true })
    .click();
  await expect(details.confirmation).toContainText(
    `Revoke ${recipientUsername}'s access to '${shared}'?`,
  );
  await details.confirm('Revoke access');

  await expect(details.announcement('Access revoked.')).toBeVisible();
  await expect(details.announcement('This secret is not shared with anyone.')).toBeVisible();
});

test('the keyboard alone rotates through the confirmation', async ({ page }) => {
  const details = await openAsAdministrator(page, byKeyboard);
  await details.rotateValue.focus();
  await page.keyboard.press('Enter');
  await page.keyboard.press('Tab');
  await expect(details.newValue).toBeFocused();
  await page.keyboard.type('e2e-keyboard-value');
  await page.keyboard.press('Tab');
  await expect(details.changeNote).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(details.renewFor).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(details.rotateSecret).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(details.confirmation.getByRole('button', { name: 'Cancel' })).toBeFocused();
  await page.keyboard.press('Tab');
  await page.keyboard.press('Enter');

  await expect(details.announcement(rotated)).toBeVisible();
  await expect(details.version(1)).toBeVisible();
});

test('deleting returns to the list, which says so', async ({ page }) => {
  const details = await openAsAdministrator(page, deleted);
  await details.delete.click();
  await expect(details.confirmation).toContainText(
    `Delete '${deleted}' and its entire version history permanently? This cannot be undone and will be audited.`,
  );
  await details.confirm('Delete secret');

  await expect(page).toHaveURL(/\/Secrets$/);
  const list = new SecretsList(page);
  await expect(list.heading).toBeVisible();
  await expect(
    page.getByText('Secret deleted. The action was recorded in the audit trail.', { exact: true }),
  ).toBeVisible();
  await expect(list.secret(deleted)).toHaveCount(0);
});

for (const theme of ['dark', 'light']) {
  test(`the details page reveals a value in the ${theme} theme`, async ({ page }, info) => {
    await page
      .context()
      .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: process.env['E2E_BASE_URL']! }]);
    const details = await openAsAdministrator(page, changed);
    expect(await page.evaluate(() => document.documentElement.dataset['theme'])).toBe(theme);
    await details.revealValue.click();
    await expect(details.revealed('current value')).toBeVisible();
    await expect(details.copy).toBeVisible();

    const path = info.outputPath(`${theme}-secret-details.png`);
    await page.screenshot({ path, fullPage: true });
    await info.attach(`${theme}-secret-details`, { path, contentType: 'image/png' });
  });
}

for (const width of [300, 2000] as const) {
  test(`Secret details has no sideways scroll at ${width}px with a revealed value`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 900 });
    const details = await openAsAdministrator(page, changed);
    await details.revealValue.click();
    await expect(details.revealed('current value')).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(width);
  });
}
