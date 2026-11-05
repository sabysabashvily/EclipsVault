import type { BrowserContext } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { randomBytes } from 'node:crypto';
import { test, expect } from './fixtures';
import { ServiceAccounts } from './service-accounts';
import { ServiceAccountCreation } from './service-account-creation';
import { ServiceAccount } from './service-account';
import { AccountCreation } from './account-creation';
import { AccountSignIn } from './account-sign-in';
import { AuthenticatorEnrollment } from './authenticator-enrollment';
import { freshTotp, currentTotpStep } from './totp';
import { AccountAdministrationActivity } from './account-administration-activity';
import { savedAdministratorCookies } from './administrator-cookies';

test.describe.configure({ mode: 'serial' });
let administratorCookies: Awaited<ReturnType<BrowserContext['cookies']>> = [];
let staleCookies: typeof administratorCookies = [];
let secret = '';
let detailsUrl = '';
const name = 'e2e-service-account';

// The real Profile dependency outlasts the freshness window; the original signed cookie remains stale.
test('creates a service account and issues a scoped key through real proof, then revokes Public API access', async ({
  page,
  browserHealth,
}) => {
  const directory = process.env['E2E_TEST_RESULTS'] || 'test-results';
  const cookies = await savedAdministratorCookies();
  secret = await readFile(`${directory}/users-administrator-authenticator.txt`, 'utf8');
  staleCookies = cookies;
  await page.context().addCookies(staleCookies);
  const list = new ServiceAccounts(page);
  await list.open();
  await expect(list.heading).toBeVisible();
  await list.create.click();
  const creation = new ServiceAccountCreation(page);
  await creation.fill(name);
  await creation.create.click();
  const details = new ServiceAccount(page, name);
  await expect(details.heading).toBeVisible();
  detailsUrl = page.url();
  await expect(details.status).toContainText(
    `Service account '${name}' created. Issue an API key to let it authenticate.`,
  );
  await details.scoped(await details.sourceIp());
  browserHealth.expectConsoleError({ text: /403/, url: /\/api\/ui\/service-accounts\/.*\/keys$/ });
  await details.issue.click();
  await expect(details.stepUp).toBeVisible();
  await details.cancelProof();
  await expect(details.issue).toBeFocused();
  await expect(details.token).toHaveCount(0);
  await details.issue.click();
  await details.prove(await freshTotp(secret, currentTotpStep()));
  await expect(details.status).toContainText(
    'API key issued. Copy it now — it will not be shown again.',
  );
  await expect(details.token).toBeVisible();
  const apiKey = await details.token.innerText();
  expect(apiKey.startsWith('evk_')).toBe(true);
  expect(page.url().includes(apiKey)).toBe(false);
  const leaked = await page.evaluate(
    (value) =>
      [document.cookie, JSON.stringify(localStorage), JSON.stringify(sessionStorage)].some(
        (store) => store.includes(value),
      ),
    apiKey,
  );
  expect(leaked).toBe(false);
  expect(
    (await page.request.get('/api/v1/secrets', { headers: { 'X-Api-Key': apiKey } })).status(),
  ).toBe(200);
  await page.context().grantPermissions(['clipboard-read', 'clipboard-write']);
  await details.copy.click();
  expect(await details.copiedVisibleKey()).toBe(true);
  await expect(details.scope).toContainText('metadata-only');
  await expect(details.scope).toContainText('at most Standard');
  await expect(details.scope).toContainText('GLOBAL');
  await expect(details.scope).toContainText('1 network');
  await details.revoke.click();
  await expect(details.confirmation).toContainText(
    'Any caller using it will stop working immediately.',
  );
  await details.cancel();
  await expect(details.revoke).toBeVisible();
  await details.revoke.click();
  await details.confirm();
  await expect(details.status).toContainText('API key revoked.');
  expect(
    (await page.request.get('/api/v1/secrets', { headers: { 'X-Api-Key': apiKey } })).status(),
  ).toBe(401);
  await details.back.click();
  await list.manage(name);
  await expect(details.token).toHaveCount(0);
  await page.reload();
  await expect(details.token).toHaveCount(0);
  await expect(details.heading).toBeVisible();
  administratorCookies = await page.context().cookies();
});

test('disables without confirmation and requires fresh proof to enable, restoring focus after cancel', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(administratorCookies);
  await page.goto(detailsUrl);
  const details = new ServiceAccount(page, name);
  await details.disable.click();
  await expect(details.status).toContainText(
    'Service account disabled — all of its keys are now rejected.',
  );
  await expect(details.confirmation).not.toBeVisible();
  await page.context().addCookies(staleCookies);
  await page.reload();
  browserHealth.expectConsoleError({
    text: /403/,
    url: /\/api\/ui\/service-accounts\/.*\/enable$/,
  });
  await details.enable.click();
  await expect(details.stepUp).toBeVisible();
  await details.cancelProof();
  await expect(details.enable).toBeFocused();
  await details.enable.click();
  await details.prove(await freshTotp(secret, currentTotpStep()));
  await expect(details.status).toContainText('Service account enabled.');
  await expect(details.disable).toBeVisible();
  administratorCookies = await page.context().cookies();
});

const shells = ['/Secrets', '/Access'] as const;
for (const shell of shells) {
  test(`both Service accounts navigation entries work from ${shell}`, async ({ page }) => {
    await page.context().addCookies(administratorCookies);
    const list = new ServiceAccounts(page);
    await list.fromSidebar(shell);
    await expect(list.heading).toBeVisible();
    await list.fromPalette(shell);
    await expect(list.heading).toBeVisible();
    await list.manage(name);
    await expect(new ServiceAccount(page, name).heading).toBeVisible();
  });
}
const themes = ['dark', 'light'] as const;
for (const theme of themes) {
  test(`visual parity: Service accounts and both forms in ${theme} appearance`, async ({
    page,
  }, info) => {
    await page.context().addCookies(administratorCookies);
    const list = new ServiceAccounts(page);
    await list.open();
    await page.context().addCookies([{ name: 'EclipsVault.Theme', value: theme, url: page.url() }]);
    await page.reload();
    await expect(list.row(name)).toBeVisible();
    await page.screenshot({
      path: info.outputPath(`service-accounts-${theme}.png`),
      fullPage: true,
    });
    await list.create.click();
    const creation = new ServiceAccountCreation(page);
    await expect(creation.name).toBeEnabled();
    await page.screenshot({
      path: info.outputPath(`new-service-account-${theme}.png`),
      fullPage: true,
    });
    await creation.back.click();
    await list.manage(name);
    const details = new ServiceAccount(page, name);
    await expect(details.expiry).toBeEnabled();
    const expiry = await details.expiry.boundingBox();
    const ceiling = await details.ceiling.boundingBox();
    expect(expiry?.y).toBe(ceiling?.y);
    await expect(details.metadataOnly).toHaveCSS('width', '13px');
    await expect(details.keyScopes).toHaveCSS('display', 'inline-flex');
    await expect(details.dangerZone).toHaveCSS('border-top-width', '1px');
    await page.screenshot({
      path: info.outputPath(`service-account-${theme}.png`),
      fullPage: true,
    });
  });

  test(`Service accounts forms and confirmation support the keyboard in ${theme} appearance`, async ({
    page,
  }) => {
    await page.context().addCookies(administratorCookies);
    const list = new ServiceAccounts(page);
    await list.open();
    await page.context().addCookies([{ name: 'EclipsVault.Theme', value: theme, url: page.url() }]);
    await page.reload();
    await list.create.click();
    const creation = new ServiceAccountCreation(page);
    await creation.name.focus();
    await page.keyboard.press('Tab');
    await expect(creation.clearance).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(creation.project).toBeFocused();
    await creation.back.click();
    await list.manage(name);
    const details = new ServiceAccount(page, name);
    await details.expiry.focus();
    await page.keyboard.press('Tab');
    await expect(details.ceiling).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(details.projectScope).toBeFocused();
    await details.remove.focus();
    await page.keyboard.press('Enter');
    await expect(details.confirmation).toContainText(
      `Delete service account '${name}' and all its API keys? This cannot be undone.`,
    );
    await page.keyboard.press('Escape');
    await expect(details.remove).toBeFocused();
    await expect(details.heading).toBeVisible();
  });
}

test('a non-administrator reaches Denied from every service-account bookmark', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(administratorCookies);
  const password = `E2e-${randomBytes(24).toString('hex')}`;
  const creation = new AccountCreation(page);
  await creation.open();
  await creation.createMailboxOwner('e2e-sa-reader', password);
  await expect(page).toHaveURL(/\/Admin\/Users$/);
  const reader = await (await browserHealth.newContext()).newPage();
  const signIn = new AccountSignIn(reader);
  await signIn.open();
  await signIn.submitPassword('e2e-sa-reader', password);
  const enrollment = new AuthenticatorEnrollment(reader);
  await enrollment.confirm(await freshTotp(await enrollment.secret.innerText()));
  await reader.goto('/ServiceAccounts');
  await expect(reader).toHaveURL(/\/Account\/Denied/);
  await reader.goto('/ServiceAccounts/Index');
  await expect(reader).toHaveURL(/\/Account\/Denied/);
  await reader.goto('/ServiceAccounts/Create');
  await expect(reader).toHaveURL(/\/Account\/Denied/);
  await reader.goto(detailsUrl);
  await expect(reader).toHaveURL(/\/Account\/Denied/);
});

test('deletes after the exact confirmation and announces the outcome on the list', async ({
  page,
}) => {
  await page.context().addCookies(administratorCookies);
  await page.goto(detailsUrl);
  const details = new ServiceAccount(page, name);
  await details.remove.click();
  await expect(details.confirmation).toContainText(
    `Delete service account '${name}' and all its API keys? This cannot be undone.`,
  );
  await details.confirm();
  const list = new ServiceAccounts(page);
  await expect(list.heading).toBeVisible();
  await expect(list.status).toBeVisible();
  await expect(list.row(name)).toHaveCount(0);
});

test('My activity shows the service account and key actions after leaving Service accounts', async ({
  page,
}) => {
  await page.context().addCookies(administratorCookies);
  const activity = new AccountAdministrationActivity(page);
  await activity.open();
  await expect(activity.heading).toBeVisible();
  await expect(activity.entry('Deleted a service account')).toContainText(name);
  await expect(activity.entry('Enabled a service account')).toContainText(name);
  await expect(activity.entry('Disabled a service account')).toContainText(name);
  await expect(activity.entry('Revoked an API key')).toContainText(name);
  await expect(activity.entry('Issued an API key')).toContainText(name);
  await expect(activity.entry('Created a service account')).toContainText(name);
});
