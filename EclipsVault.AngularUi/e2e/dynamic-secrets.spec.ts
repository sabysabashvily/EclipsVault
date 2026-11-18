import { readFile } from 'node:fs/promises';
import { savedAdministratorCookies } from './administrator-cookies';
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

test('a cancelled proof for the Secret role issues nothing and starts no window', async ({
  withExpiredProof: dynamicSecrets,
  browserHealth,
}) => {
  browserHealth.expectConsoleError(refusedIssue);
  await dynamicSecrets.generate(dynamicRoles.secret).click();
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
  await page.getByRole('link', { name: 'Secrets', exact: true }).click();
  await expect(page).toHaveURL(/\/Secrets$/);
  await page.getByRole('link', { name: 'Dynamic secrets', exact: true }).click();
  await expect(dynamicSecrets.revoke(username)).toBeVisible();
  await expect(dynamicSecrets.credential).toHaveCount(0);
  await expect(page.getByText(password)).toHaveCount(0);
});

test('the Internal role issues by keyboard without a dialog, and reloading forgets it', async ({
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

test('Revoke by keyboard asks with the Legacy confirmation and removes the login', async ({
  page,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const dynamicSecrets = new DynamicSecretsPage(page);
  await dynamicSecrets.open();
  await dynamicSecrets.revoke(secretLogin).focus();
  await page.keyboard.press('Enter');
  await expect(dynamicSecrets.confirmation).toContainText(
    `Hand back '${secretLogin}'? It stops working immediately.`,
  );
  await expect(dynamicSecrets.confirmation.getByRole('button', { name: 'Cancel' })).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(dynamicSecrets.confirmation.getByRole('button', { name: 'Continue' })).toBeFocused();
  await page.keyboard.press('Enter');

  await expect(dynamicSecrets.status).toHaveText('Credential handed back — it no longer works.');
  await expect(dynamicSecrets.revoke(secretLogin)).toHaveCount(0);
  expect(loginExists(secretLogin)).toBe(false);
});

test('cancelling Revoke keeps the login, and Continue removes it', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const dynamicSecrets = new DynamicSecretsPage(page);
  await dynamicSecrets.open();
  await dynamicSecrets.revoke(internalLogin).click();
  await dynamicSecrets.confirmation.getByRole('button', { name: 'Cancel' }).click();
  await expect(dynamicSecrets.confirmation).toBeHidden();
  expect(loginExists(internalLogin)).toBe(true);

  await dynamicSecrets.revoke(internalLogin).click();
  await dynamicSecrets.confirmation.getByRole('button', { name: 'Continue' }).click();

  await expect(dynamicSecrets.status).toHaveText('Credential handed back — it no longer works.');
  expect(loginExists(internalLogin)).toBe(false);
});

test('the Legacy sidebar opens Dynamic secrets', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const dynamicSecrets = new DynamicSecretsPage(page);
  await dynamicSecrets.fromSidebar('/Account/Denied');
  await expect(dynamicSecrets.heading).toBeVisible();
  await expect(page).toHaveURL(/\/DynamicSecrets$/);
});

test('the Angular sidebar opens Dynamic secrets', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const dynamicSecrets = new DynamicSecretsPage(page);
  await dynamicSecrets.fromSidebar('/Access');
  await expect(dynamicSecrets.heading).toBeVisible();
  await expect(page).toHaveURL(/\/DynamicSecrets$/);
});

test('the palette in the Legacy shell opens Dynamic secrets', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const dynamicSecrets = new DynamicSecretsPage(page);
  await dynamicSecrets.fromPalette('/Account/Denied');
  await expect(dynamicSecrets.heading).toBeVisible();
});

test('the palette in the Angular shell opens Dynamic secrets', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const dynamicSecrets = new DynamicSecretsPage(page);
  await dynamicSecrets.fromPalette('/Access');
  await expect(dynamicSecrets.heading).toBeVisible();
});

for (const theme of ['dark', 'light'] as const) {
  test(`visual parity: Dynamic secrets in ${theme} appearance`, async ({ page }, info) => {
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
