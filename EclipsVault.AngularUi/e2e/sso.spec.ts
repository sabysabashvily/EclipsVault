import { randomBytes } from 'node:crypto';
import { signInNewAccount } from './account-activation';
import { AccountCreation, emailOf } from './account-creation';
import { AccountSignIn } from './account-sign-in';
import { savedAdministratorCookies } from './administrator-cookies';
import { refreshAdministratorProof } from './administrator-proof';
import { test, expect } from './fixtures';
import { clearSignInBudget } from './profile-state';
import { SecretsList } from './secrets-list';
import { KeycloakSignIn } from './sso';
import { freshTotp } from './totp';

const mfaUsername = 'e2e-sso-mfa';
const passwordUsername = 'e2e-sso-password';
const refused = 'Single sign-on did not grant access to this vault. Contact an administrator.';

function idpPassword(): string {
  const password = process.env['E2E_SSO_PASSWORD'];
  if (!password)
    throw new Error('Run through npm run e2e so the identity provider password is set');
  return password;
}

function idpAuthenticatorSecret(): string {
  const secret = process.env['E2E_SSO_OTP_SECRET'];
  if (!secret)
    throw new Error('Run through npm run e2e so the identity provider authenticator secret is set');
  return secret;
}

test.describe.configure({ mode: 'serial' });
const vaultPassword = `E2e-${randomBytes(24).toString('hex')}`;
let vaultAuthenticatorSecret = '';
let vaultAuthenticatorStep = -1;

test('an administrator invites the vault accounts both identity provider users sign in as', async ({
  page,
}) => {
  test.setTimeout(180_000);
  await page.context().addCookies(await savedAdministratorCookies());
  await refreshAdministratorProof(page);

  const creation = new AccountCreation(page);
  for (const [lastName, username] of [
    ['Multifactor', mfaUsername],
    ['Passwordonly', passwordUsername],
  ] as const) {
    await creation.open();
    await creation.create({
      firstName: 'Sso',
      lastName,
      username,
      clearance: 'Standard',
      project: 'GLOBAL',
    });
    await expect(creation.status).toContainText(`Invitation sent to ${emailOf(username)}.`);
  }
});

// Single sign-on refuses an account awaiting activation, so both owners activate first.
test('both identity provider users activate their vault accounts', async ({ browserHealth }) => {
  test.setTimeout(180_000);
  await signInNewAccount(
    await (await browserHealth.newContext()).newPage(),
    mfaUsername,
    `E2e-${randomBytes(24).toString('hex')}`,
  );
  const enrolled = await signInNewAccount(
    await (await browserHealth.newContext()).newPage(),
    passwordUsername,
    vaultPassword,
  );
  vaultAuthenticatorSecret = enrolled.secret;
  vaultAuthenticatorStep = enrolled.step;
});

test('a user whose identity provider completed MFA lands on Secrets', async ({ browserHealth }) => {
  test.setTimeout(120_000);
  const page = await (await browserHealth.newContext()).newPage();
  clearSignInBudget();
  const vault = new AccountSignIn(page);
  await vault.open();
  await vault.continueWithSingleSignOn('Keycloak');
  const keycloak = new KeycloakSignIn(page);
  await keycloak.submitPassword('sso-mfa', idpPassword());
  await keycloak.submitOneTimeCode(await freshTotp(idpAuthenticatorSecret()));
  await expect(page).toHaveURL(/\/Secrets$/);
  await expect(new SecretsList(page).heading).toBeVisible();
});

test('a user whose identity provider did not complete MFA finishes with an authenticator code', async ({
  browserHealth,
}) => {
  test.setTimeout(120_000);
  const page = await (await browserHealth.newContext()).newPage();
  clearSignInBudget();
  const vault = new AccountSignIn(page);
  await vault.open();
  await vault.continueWithSingleSignOn('Keycloak');
  await new KeycloakSignIn(page).submitPassword('sso-password', idpPassword());
  await expect(page).toHaveURL(/\/Account\/Totp$/);
  await vault.submitAuthenticatorCode(
    await freshTotp(vaultAuthenticatorSecret, vaultAuthenticatorStep),
  );
  await expect(page).toHaveURL(/\/Secrets$/);
  await expect(new SecretsList(page).heading).toBeVisible();
});

test('a restarted second factor loads Login with its notice and permits single sign-on', async ({
  browserHealth,
}) => {
  test.setTimeout(120_000);
  const page = await (await browserHealth.newContext()).newPage();
  const signIn = new AccountSignIn(page);
  clearSignInBudget();
  await signIn.open();
  await signIn.submitPassword(passwordUsername, vaultPassword);
  await expect(page).toHaveURL(/\/Account\/Totp$/);
  // A directly loaded second-factor document permits only same-origin form targets.
  const secondFactor = await page.reload();
  expect(secondFactor?.headers()['content-security-policy']).toContain("form-action 'self';");
  await page.route(
    '**/api/ui/auth/totp',
    (route) =>
      route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          status: 409,
          code: 'sign_in_restarted',
          title: 'Your account changed while you were signing in. Sign in again.',
        }),
      }),
    { times: 1 },
  );
  browserHealth.expectConsoleError({ text: /status of 409/, url: /\/api\/ui\/auth\/totp$/ });
  const loginDocument = page.waitForResponse(
    (response) =>
      response.request().isNavigationRequest() &&
      new URL(response.url()).pathname === '/Account/Login',
  );

  await signIn.submitAuthenticatorCode('123456');

  expect((await loginDocument).status()).toBe(200);
  await expect(page).toHaveURL(/\/Account\/Login\?signIn=restarted$/);
  await expect(signIn.restartedNotice).toBeVisible();
  await signIn.continueWithSingleSignOn('Keycloak');
  await new KeycloakSignIn(page).submitPassword('sso-password', idpPassword());
  await expect(page).toHaveURL(/\/Account\/Totp$/);
});

test('a user with no vault account is told single sign-on did not grant access', async ({
  browserHealth,
}) => {
  const page = await (await browserHealth.newContext()).newPage();
  clearSignInBudget();
  const vault = new AccountSignIn(page);
  await vault.open();
  await vault.continueWithSingleSignOn('Keycloak');
  await new KeycloakSignIn(page).submitPassword('sso-stranger', idpPassword());
  await expect(page).toHaveURL(/\/Account\/Login\?sso=refused$/);
  await expect(page.getByText(refused, { exact: true })).toBeVisible();
  expect(
    (await page.context().cookies()).some((cookie) => cookie.name === 'EclipsVault.Session'),
  ).toBe(false);
});
