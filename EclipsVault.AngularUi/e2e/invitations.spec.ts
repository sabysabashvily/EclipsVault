import type { BrowserContext } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { test, expect } from './fixtures';
import { AccountCreation, emailOf } from './account-creation';
import { AccountSignIn } from './account-sign-in';
import { Activation } from './activation';
import { savedAdministratorCookies } from './administrator-cookies';
import { refreshAdministratorProof } from './administrator-proof';
import { clearSignInBudget } from './profile-state';
import { invitationCount, invitationToken } from './smtp-capture';
import { currentTotpStep, freshTotp } from './totp';
import { UserRole } from './user-role';
import { Users } from './users';
import { expireInvitation } from './users-state';

const invalidLink =
  'This invitation link is not valid. It may have expired or been used. Ask an administrator to send a new one.';

test.describe.configure({ mode: 'serial' });
test.beforeEach(() => clearSignInBudget());

let administratorCookies: Awaited<ReturnType<BrowserContext['cookies']>> = [];
const resendUsername = 'e2e-inv-resend';
const replacedAddress = 'e2e-inv-resend-new@example.test';
let currentToken = '';

test('create, capture email, activate in the Angular page and sign in with the new password and authenticator', async ({
  page,
  browserHealth,
}) => {
  const username = 'e2e-inv-activate';
  const password = `E2e-${randomBytes(24).toString('hex')}`;
  await page.context().addCookies(await savedAdministratorCookies());
  await refreshAdministratorProof(page);
  administratorCookies = await page.context().cookies();

  const creation = new AccountCreation(page);
  await creation.fromDashboard();
  await expect(creation.heading).toBeVisible();
  await creation.create({
    firstName: 'Invited',
    lastName: 'Person',
    username,
    clearance: 'Standard',
    project: 'GLOBAL',
  });
  await expect(creation.status).toHaveText(
    `Invitation sent to ${emailOf(username)}. It expires in 72 hours.`,
  );

  const users = new Users(page);
  await users.open();
  await expect(users.row(username)).toContainText(/^.*Invited · expires .+ UTC.*$/);

  const token = await invitationToken(emailOf(username));
  const owner = await (await browserHealth.newContext()).newPage();
  const activation = new Activation(owner);
  const document = await activation.open(token);
  expect(document?.headers()['content-security-policy']).toContain(
    "require-trusted-types-for 'script'",
  );
  await expect(owner).toHaveTitle('Activate your account — EclipsVault');
  await expect(activation.heading).toBeVisible();
  await expect(activation.lead).toBeVisible();
  await expect(activation.username).toHaveValue(username);
  await expect(activation.username).toHaveAttribute('readonly', '');
  await expect.poll(() => activation.qrCodeLoaded()).toBe(true);
  await expect.poll(() => new URL(owner.url()).hash).toBe('');

  const secret = (await activation.secret.innerText()).trim();
  const activationCode = await freshTotp(secret);
  const activationStep = currentTotpStep();
  await activation.activate(password, activationCode);

  await expect(owner).toHaveURL(/\/Account\/Login\?activated=1$/);
  const signIn = new AccountSignIn(owner);
  await expect(signIn.activatedNotice).toBeVisible();
  await signIn.submitPassword(username, password);
  await signIn.submitAuthenticatorCode(await freshTotp(secret, activationStep));
  await expect(owner).toHaveURL(/\/Secrets$/);

  await users.open();
  await expect(users.row(username)).toContainText('Active');
});

test('resending an invitation from Users kills the old link and issues a usable new one', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(administratorCookies);
  const creation = new AccountCreation(page);
  await creation.open();
  await creation.create({
    firstName: 'Resent',
    lastName: 'Invite',
    username: resendUsername,
    clearance: 'Standard',
    project: 'GLOBAL',
  });
  await expect(creation.status).toHaveText(
    `Invitation sent to ${emailOf(resendUsername)}. It expires in 72 hours.`,
  );
  const firstToken = await invitationToken(emailOf(resendUsername));

  const users = new Users(page);
  await users.open();
  await users.resend(resendUsername);
  await expect(users.confirmation).toContainText(
    `Send a new invitation to ${emailOf(resendUsername)}? The previous link stops working.`,
  );
  await expect(users.confirmation).toContainText('Saved to the audit log');
  await users.confirm('Resend invitation');
  await expect(users.status).toHaveText(
    `Invitation sent to ${emailOf(resendUsername)}. It expires in 72 hours.`,
  );

  currentToken = await invitationToken(emailOf(resendUsername));
  expect(currentToken).not.toBe(firstToken);

  const stale = await (await browserHealth.newContext()).newPage();
  const staleActivation = new Activation(stale);
  browserHealth.expectConsoleError({ text: /404/, url: /\/api\/ui\/auth\/activation$/ });
  await staleActivation.open(firstToken);
  await expect(staleActivation.alert).toHaveText(invalidLink);
  await expect(staleActivation.newPassword).toHaveCount(0);
  await expect.poll(() => new URL(stale.url()).hash).toBe('');

  const fresh = await (await browserHealth.newContext()).newPage();
  const freshActivation = new Activation(fresh);
  await freshActivation.open(currentToken);
  await expect(freshActivation.username).toHaveValue(resendUsername);
});

test('replacing the invitation address kills the old link without sending mail, and Resend delivers to the new address', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(administratorCookies);
  const users = new Users(page);
  await users.open();
  await users.replaceInvitationAddress(resendUsername);

  const role = new UserRole(page);
  await expect(role.invitation).toBeVisible();
  await expect(role.invitationAddress).toHaveValue(emailOf(resendUsername));
  await role.invitationAddress.fill(replacedAddress);
  await role.replaceAddressButton.click();
  await expect(users.confirmation).toContainText(
    `Use ${replacedAddress} for ${resendUsername}? The link already sent stops working. Resend to send a new one there.`,
  );
  await expect(users.confirmation).toContainText('Saved to the audit log');
  await users.confirm('Replace address');
  await expect(role.status).toHaveText(
    `The invitation address is now ${replacedAddress}. Resend the invitation to send a link there.`,
  );
  await expect(role.invitation).toContainText('Invitation expired');
  expect(await invitationCount(replacedAddress)).toBe(0);

  const stale = await (await browserHealth.newContext()).newPage();
  const staleActivation = new Activation(stale);
  browserHealth.expectConsoleError({ text: /404/, url: /\/api\/ui\/auth\/activation$/ });
  await staleActivation.open(currentToken);
  await expect(staleActivation.alert).toHaveText(invalidLink);
  await expect(staleActivation.newPassword).toHaveCount(0);

  await role.resendButton.click();
  await expect(users.confirmation).toContainText(
    `Send a new invitation to ${replacedAddress}? The previous link stops working.`,
  );
  await users.confirm('Resend invitation');
  await expect(role.status).toHaveText(
    `Invitation sent to ${replacedAddress}. It expires in 72 hours.`,
  );

  currentToken = await invitationToken(replacedAddress);
  const fresh = await (await browserHealth.newContext()).newPage();
  const freshActivation = new Activation(fresh);
  await freshActivation.open(currentToken);
  await expect(freshActivation.username).toHaveValue(resendUsername);
});

test('an expired invitation shows Invitation expired with a Resend button on Users and is refused on Activation', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(administratorCookies);
  expireInvitation(resendUsername);

  const users = new Users(page);
  await users.open();
  await expect(users.row(resendUsername)).toContainText('Invitation expired');
  await expect(users.resendButton(resendUsername)).toBeVisible();

  const expired = await (await browserHealth.newContext()).newPage();
  const activation = new Activation(expired);
  browserHealth.expectConsoleError({ text: /404/, url: /\/api\/ui\/auth\/activation$/ });
  await activation.open(currentToken);
  await expect(activation.alert).toHaveText(invalidLink);
  await expect(activation.newPassword).toHaveCount(0);

  // Resend through the expired row's verb button so the theme and keyboard checks have a usable link.
  await users.resendButton(resendUsername).click();
  await users.confirm('Resend invitation');
  await expect(users.status).toHaveText(
    `Invitation sent to ${replacedAddress}. It expires in 72 hours.`,
  );
  currentToken = await invitationToken(replacedAddress);
});

for (const theme of ['dark', 'light'] as const) {
  test(`Activation and the user invitation panel support keyboard navigation and 300px layout in ${theme} appearance`, async ({
    page,
    browserHealth,
  }, info) => {
    const invited = await (await browserHealth.newContext()).newPage();
    await invited.setViewportSize({ width: 300, height: 900 });
    const activation = new Activation(invited);
    await activation.setTheme(theme);
    await activation.open(currentToken);
    // eslint-disable-next-line playwright/no-raw-locators -- The theme is an attribute of the document element.
    await expect(invited.locator('html')).toHaveAttribute('data-theme', theme);
    await expect(activation.username).toHaveValue(resendUsername);
    await expect.poll(() => activation.qrCodeLoaded()).toBe(true);
    await expect.poll(() => invited.evaluate(() => document.documentElement.scrollWidth)).toBe(300);
    await invited.screenshot({ path: info.outputPath(`activate-${theme}.png`), fullPage: true });

    await expect(activation.newPassword).toBeFocused();
    await invited.keyboard.press('Tab');
    await expect(activation.showPassword).toBeFocused();
    await invited.keyboard.press('Tab');
    await expect(activation.confirmPassword).toBeFocused();
    await invited.keyboard.press('Tab');
    await expect(activation.confirmationCode).toBeFocused();
    await invited.keyboard.press('Tab');
    await expect(activation.submitButton).toBeFocused();

    await page.setViewportSize({ width: 300, height: 900 });
    await page.context().addCookies(administratorCookies);
    const users = new Users(page);
    await users.open();
    await users.useTheme(theme);
    await users.edit(resendUsername);
    const role = new UserRole(page);
    await expect(role.invitation).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(300);
    await page.screenshot({
      path: info.outputPath(`edit-user-invitation-${theme}.png`),
      fullPage: true,
    });
    await role.resendButton.focus();
    await page.keyboard.press('Enter');
    await expect(users.confirmation).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(users.confirmation).toBeHidden();
    await expect(role.resendButton).toBeFocused();
  });
}
