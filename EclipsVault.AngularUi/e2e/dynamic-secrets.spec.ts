import { readFile } from 'node:fs/promises';
import { savedAdministratorCookies } from './administrator-cookies';
import { AppShell } from './app-shell';
import { DynamicSecretsPage } from './dynamic-secrets';
import { dynamicRoles, loginExists, provisionDynamicTarget } from './dynamic-target';
import { test as browserTest, expect } from './fixtures';
import {
  savedProofSetupTimeout,
  waitForSavedAdministratorProofToExpire,
} from './saved-administrator-proof';
import { currentTotpStep, freshTotp } from './totp';

// The saved administrator session's proof must be stale for the Secret role to ask for a code.
const test = browserTest.extend<{ withExpiredProof: DynamicSecretsPage }>({
  withExpiredProof: [
    async ({ page }, use) => {
      await page.context().addCookies(await savedAdministratorCookies());
      await waitForSavedAdministratorProofToExpire(page.request);
      const dynamicSecrets = new DynamicSecretsPage(page);
      await dynamicSecrets.open();
      await use(dynamicSecrets);
    },
    { timeout: savedProofSetupTimeout },
  ],
});

test.describe.configure({ mode: 'serial' });
const refusedIssue = { text: /403/, url: /\/api\/ui\/dynamic-secrets\/issue$/ };
const notStarted =
  'Dynamic secrets are a licensed feature. Issuing your first credential starts a free 30-day evaluation — no licence key, no registration. After that, issuing new credentials needs a licence; existing leases stay listed and revocable either way.';
const evaluating =
  'Evaluating dynamic secrets — 30 days left. When the evaluation ends, issuing new credentials needs a licence. Existing leases stay listed and revocable.';
let secretLogin = '';
let internalLogin = '';

test('viewing the page twice starts no evaluation window', async ({ page }) => {
  provisionDynamicTarget();
  await page.context().addCookies(await savedAdministratorCookies());
  const dynamicSecrets = new DynamicSecretsPage(page);
  await dynamicSecrets.open();
  await expect(dynamicSecrets.note(notStarted)).toBeVisible();
  await dynamicSecrets.open();
  await expect(dynamicSecrets.note(notStarted)).toBeVisible();
  await expect(dynamicSecrets.generate(dynamicRoles.secret)).toBeVisible();
});

test('a cancelled issue dialog or cancelled proof for the Secret role issues nothing and starts no window', async ({
  withExpiredProof: dynamicSecrets,
  browserHealth,
}) => {
  browserHealth.expectConsoleError(refusedIssue);
  await dynamicSecrets.generate(dynamicRoles.secret).click();
  await expect(dynamicSecrets.confirmation).toContainText('Issue dynamic credential?');
  await dynamicSecrets.confirmationCancel.click();
  await expect(dynamicSecrets.confirmation).toBeHidden();
  await expect(dynamicSecrets.generate(dynamicRoles.secret)).toBeFocused();
  await expect(dynamicSecrets.credential).toHaveCount(0);

  await dynamicSecrets.generate(dynamicRoles.secret).click();
  await dynamicSecrets.confirmationIssue.click();
  await expect(dynamicSecrets.stepUp).toBeVisible();
  await dynamicSecrets.cancelProof();
  await expect(dynamicSecrets.stepUp).toBeHidden();
  await expect(dynamicSecrets.generate(dynamicRoles.secret)).toBeFocused();
  await expect(dynamicSecrets.credential).toHaveCount(0);
  await dynamicSecrets.open();
  await expect(dynamicSecrets.note(notStarted)).toBeVisible();
});

test('a real code issues the Secret role once, starts the evaluation and leaves no copy after departure', async ({
  withExpiredProof: dynamicSecrets,
  page,
  browserHealth,
}) => {
  const directory = process.env['E2E_TEST_RESULTS'] || 'test-results';
  const secret = await readFile(`${directory}/users-administrator-authenticator.txt`, 'utf8');
  browserHealth.expectConsoleError(refusedIssue);
  await dynamicSecrets.generate(dynamicRoles.secret).click();
  await dynamicSecrets.confirmationIssue.click();
  await dynamicSecrets.prove(await freshTotp(secret, currentTotpStep()));

  await expect(dynamicSecrets.credential).toBeFocused();
  await expect(dynamicSecrets.issuedPassword).toHaveText(/^[A-Za-z0-9]{40}$/);
  const password = (await dynamicSecrets.issuedPassword.textContent())!.trim();
  const username = (await dynamicSecrets.issuedUsername.textContent())!.trim();
  secretLogin = username;
  expect(loginExists(username)).toBe(true);
  await expect(dynamicSecrets.note(evaluating)).toBeVisible();
  await expect(dynamicSecrets.revoke(username)).toBeVisible();

  // Leaving within the app and coming back finds the lease, never the password.
  await new AppShell(page).secrets.click();
  await expect(page).toHaveURL(/\/Secrets$/);
  await dynamicSecrets.sidebarLink.click();
  await expect(dynamicSecrets.revoke(username)).toBeVisible();
  await expect(dynamicSecrets.credential).toHaveCount(0);
  await expect(page.getByText(password)).toHaveCount(0);
});

test('the Internal role issues by keyboard through its consequence dialog without step-up, and reloading forgets it', async ({
  page,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const dynamicSecrets = new DynamicSecretsPage(page);
  await dynamicSecrets.open();
  await dynamicSecrets.minutes(dynamicRoles.internal).focus();
  await page.keyboard.type('30');
  await page.keyboard.press('Tab');
  await expect(dynamicSecrets.generate(dynamicRoles.internal)).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(dynamicSecrets.confirmation).toContainText('Issue dynamic credential?');
  await expect(dynamicSecrets.confirmation).toContainText(dynamicRoles.internal);
  await expect(dynamicSecrets.confirmation).toContainText('30 min');
  await expect(dynamicSecrets.confirmation).toContainText(
    'The password is shown once and cannot be recovered.',
  );
  await expect(dynamicSecrets.confirmation).toContainText('Issuance saved to the audit log');
  await expect(dynamicSecrets.confirmationCancel).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(dynamicSecrets.confirmationIssue).toBeFocused();
  await page.keyboard.press('Enter');

  await expect(dynamicSecrets.issuedPassword).toHaveText(/^[A-Za-z0-9]{40}$/);
  await expect(dynamicSecrets.stepUp).toBeHidden();
  const password = (await dynamicSecrets.issuedPassword.textContent())!.trim();
  const username = (await dynamicSecrets.issuedUsername.textContent())!.trim();
  internalLogin = username;
  expect(loginExists(username)).toBe(true);

  await page.reload();
  await expect(dynamicSecrets.revoke(username)).toBeVisible();
  await expect(dynamicSecrets.credential).toHaveCount(0);
  await expect(page.getByText(password)).toHaveCount(0);
});

test('Revoke by keyboard asks with the decision dialog and removes the login', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const dynamicSecrets = new DynamicSecretsPage(page);
  await dynamicSecrets.open();
  await dynamicSecrets.revoke(secretLogin).focus();
  await page.keyboard.press('Enter');
  await expect(dynamicSecrets.confirmation).toContainText('Revoke dynamic credential?');
  await expect(dynamicSecrets.confirmation).toContainText(
    `Hand back '${secretLogin}'? It stops working immediately.`,
  );
  await expect(dynamicSecrets.confirmation).toContainText('Saved to the audit log');
  await expect(dynamicSecrets.confirmationCancel).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(dynamicSecrets.confirmationRevoke).toBeFocused();
  await page.keyboard.press('Enter');

  await expect(dynamicSecrets.status).toHaveText('Credential handed back — it no longer works.');
  await expect(dynamicSecrets.revoke(secretLogin)).toHaveCount(0);
  expect(loginExists(secretLogin)).toBe(false);
});

test('cancelling Revoke keeps the login, and Revoke credential removes it', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const dynamicSecrets = new DynamicSecretsPage(page);
  await dynamicSecrets.open();
  await dynamicSecrets.revoke(internalLogin).click();
  await dynamicSecrets.confirmationCancel.click();
  await expect(dynamicSecrets.confirmation).toBeHidden();
  expect(loginExists(internalLogin)).toBe(true);

  await dynamicSecrets.revoke(internalLogin).click();
  await dynamicSecrets.confirmationRevoke.click();

  await expect(dynamicSecrets.status).toHaveText('Credential handed back — it no longer works.');
  expect(loginExists(internalLogin)).toBe(false);
});

test('the Angular sidebar opens Dynamic secrets', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const dynamicSecrets = new DynamicSecretsPage(page);
  await dynamicSecrets.fromSidebar();
  await expect(dynamicSecrets.heading).toBeVisible();
  await expect(page).toHaveURL(/\/DynamicSecrets$/);
});

test('the palette in the Angular shell opens Dynamic secrets', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const dynamicSecrets = new DynamicSecretsPage(page);
  await dynamicSecrets.fromPalette();
  await expect(dynamicSecrets.heading).toBeVisible();
});

for (const theme of ['dark', 'light'] as const) {
  test(`Dynamic secrets renders roles and leases in ${theme} appearance`, async ({
    page,
  }, info) => {
    await page.context().addCookies(await savedAdministratorCookies());
    const dynamicSecrets = new DynamicSecretsPage(page);
    await dynamicSecrets.open();
    await dynamicSecrets.useTheme(theme);
    // eslint-disable-next-line playwright/no-raw-locators -- The theme is an attribute of the document element.
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
    await expect(dynamicSecrets.generate(dynamicRoles.internal)).toBeVisible();
    await page.screenshot({
      path: info.outputPath(`dynamic-secrets-${theme}.png`),
      fullPage: true,
    });
  });
}

for (const width of [300, 2000] as const) {
  test(`Dynamic secrets has no sideways scroll at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.context().addCookies(await savedAdministratorCookies());
    const dynamicSecrets = new DynamicSecretsPage(page);
    await dynamicSecrets.open();
    await expect(dynamicSecrets.generate(dynamicRoles.internal)).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(width);
  });
}
