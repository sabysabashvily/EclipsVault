import type { BrowserContext } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { randomBytes } from 'node:crypto';
import { test, expect } from './fixtures';
import { AccountCreation } from './account-creation';
import { AccountSignIn } from './account-sign-in';
import { AuthenticatorEnrollment } from './authenticator-enrollment';
import { Users } from './users';
import { lockAccount } from './users-state';
import { freshTotp } from './totp';
import { rejectAuditWrites } from './profile-state';
import { UserRole } from './user-role';

test.describe.configure({ mode: 'serial' });
let administratorCookies: Awaited<ReturnType<BrowserContext['cookies']>> = [];
let ownerCookies: typeof administratorCookies = [];
let staleAdministratorCookies: typeof administratorCookies = [];
let administratorSecret = '';
const password = `E2e-${randomBytes(24).toString('hex')}`;
const username = 'e2e-users-target';

// The dependent Profile suite takes over ten minutes. This cookie's proof therefore ages naturally;
// the server's real 403 below, not a route mock or altered clock, decides whether to show the dialog.
test('a stale administrator creates a user through real authenticator step-up and the new user enrolls', async ({
  page,
  browserHealth,
}) => {
  const directory = process.env['E2E_TEST_RESULTS'] || 'test-results';
  const state: { cookies: typeof administratorCookies } = JSON.parse(
    await readFile(`${directory}/sessions-administrator.json`, 'utf8'),
  );
  administratorSecret = await readFile(
    `${directory}/users-administrator-authenticator.txt`,
    'utf8',
  );
  await page.context().addCookies(state.cookies);
  const creation = new AccountCreation(page);
  await creation.open();
  browserHealth.expectConsoleError({ text: /403/, url: /\/api\/ui\/users$/ });
  await creation.createMailboxOwner(username, password);
  const createProof = new Users(page);
  await expect(createProof.stepUp).toBeVisible();
  await createProof.cancelStepUp();
  await expect(creation.submitButton).toBeFocused();
  await expect(creation.password).toHaveValue('');
  await creation.createMailboxOwner(username, password);
  await expect(createProof.stepUp).toBeVisible();
  await createProof.enterCode(await freshTotp(administratorSecret));
  await createProof.verifyCode();
  const users = new Users(page);
  await expect(users.heading).toBeVisible();
  await expect(users.status).toContainText(`User '${username}' created with email`);
  const owner = await (await browserHealth.newContext()).newPage();
  await new AccountSignIn(owner).open();
  await new AccountSignIn(owner).submitPassword(username, password);
  const enrollment = new AuthenticatorEnrollment(owner);
  await enrollment.confirm(await freshTotp(await enrollment.secret.innerText()));
  await expect(owner).toHaveURL(/\/Secrets$/);
  ownerCookies = await owner.context().cookies();
  administratorCookies = await page.context().cookies();
  staleAdministratorCookies = state.cookies;
});

test('cancelling stale Edit user proof restores Save role focus and changes no role', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(staleAdministratorCookies);
  const users = new Users(page);
  await users.open();
  await users.edit(username);
  const role = new UserRole(page);
  await role.project.fill('CANCELLED');
  browserHealth.expectConsoleError({ text: /403/, url: /\/api\/ui\/users\/.*\/role$/ });
  await role.save();
  await expect(users.stepUp).toBeVisible();
  await users.cancelStepUp();
  await expect(role.saveButton).toBeFocused();
  await role.back.click();
  await expect(users.row(username)).toContainText('GLOBAL');
  await expect(users.row(username)).not.toContainText('CANCELLED');
});

test('a stale administrator disables then enables through real authenticator step-up', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(administratorCookies);
  const users = new Users(page);
  await users.open();
  await users.disable(username);
  await users.confirm();
  await expect(users.row(username)).toContainText('Disabled');
  // Reuse the original stale cookie so enable still proves its own freshness rule.
  await page.context().addCookies(staleAdministratorCookies);
  await users.open();
  const blocked = page.waitForResponse(
    (response) => response.url().endsWith('/enable') && response.status() === 403,
  );
  browserHealth.expectConsoleError({ text: /403/, url: /\/api\/ui\/users\/.*\/enable$/ });
  await users.enable(username);
  await blocked;
  await expect(users.stepUp).toBeVisible();
  await users.cancelStepUp();
  await expect(users.row(username)).toContainText('Disabled');
  await users.enable(username);
  await users.enterCode(await freshTotp(administratorSecret, Math.floor(Date.now() / 30_000)));
  const verified = page.waitForResponse((response) => response.url().endsWith('/api/ui/step-up'));
  await users.verifyCode();
  expect((await verified).status()).toBe(204);
  await expect(users.status).toContainText('Account enabled.');
  await expect(users.row(username)).toContainText('Active');
  const owner = await (await browserHealth.newContext()).newPage();
  await owner.context().addCookies(ownerCookies);
  await owner.goto('/Access');
  await expect(owner).toHaveURL(/\/Account\/Login/);
  administratorCookies = await page.context().cookies();
});

test('unlock clears the account lockout set in the disposable database', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  lockAccount(username);
  const users = new Users(page);
  await users.open();
  await expect(users.row(username)).toContainText('Locked');
  await users.unlock(username);
  await expect(users.status).toContainText('Lockout cleared. The user can sign in again.');
  await expect(users.row(username)).toContainText('Active');
  await expect(users.unlockButton(username)).toHaveCount(0);
});

test('Reset MFA requires enrollment on the next sign-in', async ({ page, browserHealth }) => {
  await page.context().addCookies(administratorCookies);
  const users = new Users(page);
  await users.open();
  await users.more(username, 'Reset MFA');
  await users.confirm();
  await expect(users.status).toContainText(
    'MFA was reset — the user will enroll a new authenticator at their next sign-in.',
  );
  const owner = await (await browserHealth.newContext()).newPage();
  const signIn = new AccountSignIn(owner);
  await signIn.open();
  await signIn.submitPassword(username, password);
  await expect(owner).toHaveURL(/\/Account\/EnrollTotp$/);
  const enrollment = new AuthenticatorEnrollment(owner);
  await enrollment.confirm(await freshTotp(await enrollment.secret.innerText()));
  await expect(owner).toHaveURL(/\/Secrets$/);
  ownerCookies = await owner.context().cookies();
});

test('a non-administrator receives Denied on Users', async ({ browserHealth }) => {
  const owner = await (await browserHealth.newContext()).newPage();
  await owner.context().addCookies(ownerCookies);
  await owner.goto('/Access');
  await expect(owner).toHaveURL(/\/Access$/);
  await owner.goto('/Admin/Users');
  await expect(owner).toHaveURL(/\/Account\/Denied/);
});

test('Force logout ends the other account session', async ({ page, browserHealth }) => {
  await page.context().addCookies(administratorCookies);
  const users = new Users(page);
  await users.open();
  await users.more(username, 'Force logout');
  await users.confirm();
  await expect(users.status).toContainText("All of the user's sessions were revoked.");
  const owner = await (await browserHealth.newContext()).newPage();
  await owner.context().addCookies(ownerCookies);
  await owner.goto('/Access');
  await expect(owner).toHaveURL(/\/Account\/Login/);
});

test('deleting the account removes its row', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  const users = new Users(page);
  await users.open();
  await users.more(username, 'Delete user');
  await users.confirm();
  await expect(users.status).toContainText('User deleted.');
  await expect(users.row(username)).toHaveCount(0);
});

test('both sidebars open Users in Angular', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  const users = new Users(page);
  await users.fromSidebar('/Secrets');
  await expect(users.heading).toBeVisible();
  await expect(page).toHaveTitle('Users — EclipsVault');
  await users.fromSidebar('/Access');
  await expect(users.heading).toBeVisible();
});

test('both palettes open Users in Angular', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  const users = new Users(page);
  await users.fromPalette('/Secrets');
  await expect(users.heading).toBeVisible();
  await users.fromPalette('/Access');
  await expect(users.heading).toBeVisible();
});

test('filtering Users leaves only the matching account', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  const users = new Users(page);
  await users.open();
  await users.filter.fill('vault-admin');
  await expect(users.rows).toHaveCount(2);
  await expect(users.row('vault-admin')).toBeVisible();
});

const themes: ('dark' | 'light')[] = ['dark', 'light'];
for (const theme of themes) {
  test(`the Users row menu supports the keyboard in ${theme} appearance`, async ({ page }) => {
    await page.context().addCookies(administratorCookies);
    const users = new Users(page);
    await users.open();
    await users.useTheme(theme);
    const more = users.moreTrigger('vault-admin');
    await more.focus();
    await page.keyboard.press('ArrowDown');
    await expect(users.forceLogout('vault-admin')).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(more).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(users.forceLogout('vault-admin')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(users.forceLogout('vault-admin')).toBeHidden();
    await expect(more).toBeFocused();
  });
}

test('the last enabled administrator cannot demote themselves through Angular Edit', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(administratorCookies);
  const users = new Users(page);
  await users.open();
  await users.edit('vault-admin');
  const role = new UserRole(page);
  await role.clearance.selectOption({ label: 'Secret' });
  browserHealth.expectConsoleError({ text: /409/, url: /\/api\/ui\/users\/.*\/role$/ });
  await role.save();
  await expect(role.lastAdministratorRefusal).toBeVisible();
});

test('Dashboard New user opens the Angular form and Back to users stays in Angular', async ({
  page,
}) => {
  await page.context().addCookies(administratorCookies);
  const creation = new AccountCreation(page);
  await creation.fromDashboard();
  await expect(creation.heading).toBeVisible();
  await expect(page).toHaveTitle('New user — EclipsVault');
  await creation.back.click();
  await expect(new Users(page).heading).toBeVisible();
});

for (const theme of themes) {
  test(`Edit user supports keyboard navigation in ${theme} appearance`, async ({ page }) => {
    await page.context().addCookies(administratorCookies);
    const users = new Users(page);
    await users.open();
    await users.useTheme(theme);
    await users.edit('vault-admin');
    const role = new UserRole(page);
    await expect(role.heading).toBeVisible();
    await expect(page.getByText('This is your own account', { exact: true })).toBeVisible();
    await role.clearance.focus();
    await page.keyboard.press('Tab');
    await expect(role.project).toBeFocused();
    await page.keyboard.press('Tab');
    await page.keyboard.press('Enter');
    await expect(users.heading).toBeVisible();
  });
}

for (const theme of themes) {
  test(`New user supports keyboard navigation in ${theme} appearance`, async ({ page }) => {
    await page.context().addCookies(administratorCookies);
    const users = new Users(page);
    await users.open();
    await users.useTheme(theme);
    const creation = new AccountCreation(page);
    await users.newUser.click();
    await expect(creation.heading).toBeVisible();
    await creation.firstName.focus();
    await page.keyboard.press('Tab');
    await expect(creation.lastName).toBeFocused();
    await creation.back.click();
    await expect(users.heading).toBeVisible();
  });
}

test('an unchanged Unicode role keeps its notice and self-demotion ends both open browsers', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(administratorCookies);
  const creation = new AccountCreation(page);
  await creation.open();
  await creation.create({
    firstName: 'Own',
    lastName: 'Role',
    username: 'e2e-own-role',
    password,
    clearance: 'Top Secret',
    project: 'STRAßE',
  });
  await expect(new Users(page).heading).toBeVisible();
  const owner = await (await browserHealth.newContext()).newPage();
  const signIn = new AccountSignIn(owner);
  await signIn.open();
  await signIn.submitPassword('e2e-own-role', password);
  const enrollment = new AuthenticatorEnrollment(owner);
  await enrollment.confirm(await freshTotp(await enrollment.secret.innerText()));
  const otherContext = await browserHealth.newContext();
  await otherContext.addCookies(await owner.context().cookies());
  const otherDevice = await otherContext.newPage();
  await otherDevice.goto('/Access');
  await expect(otherDevice).toHaveURL(/\/Access$/);
  const ownUsers = new Users(owner);
  await ownUsers.open();
  await ownUsers.edit('e2e-own-role');
  const role = new UserRole(owner);
  await expect(role.clearance).toHaveValue('4');
  await expect(role.project).toHaveValue('STRAßE');
  await role.save();
  await expect(ownUsers.heading).toBeVisible();
  await expect(ownUsers.status).toContainText("Role updated for 'e2e-own-role'.");
  await otherDevice.reload();
  await expect(otherDevice).toHaveURL(/\/Access$/);
  await ownUsers.edit('e2e-own-role');
  await role.clearance.selectOption({ label: 'Secret' });
  await role.save();
  await expect(owner).toHaveURL(/\/Account\/Login/);
  await otherDevice.reload();
  await expect(otherDevice).toHaveURL(/\/Account\/Login/);
  expect(
    (await owner.context().cookies()).some((cookie) => cookie.name === 'EclipsVault.Session'),
  ).toBe(false);
  administratorCookies = await page.context().cookies();
});

test('own Force logout reaches sign-in and preserves an audit-failure warning', async ({
  page,
  context,
  browserHealth,
}) => {
  await context.addCookies(administratorCookies);
  const users = new Users(page);
  await users.open();
  await users.more('vault-admin', 'Force logout');
  browserHealth.expectConsoleError({ text: /503/, url: /\/api\/ui\/users\/.*\/sign-out$/ });
  rejectAuditWrites(true);
  try {
    await users.confirm();
    await expect(page).toHaveURL(/\/Account\/Login$/);
    await expect(new AccountSignIn(page).warning).toContainText(
      "All of the user's sessions were revoked, but the vault couldn't record it in the audit trail.",
    );
    expect((await context.cookies()).some((cookie) => cookie.name === 'EclipsVault.Session')).toBe(
      false,
    );
  } finally {
    rejectAuditWrites(false);
  }
});
