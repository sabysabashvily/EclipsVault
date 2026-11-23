import { randomBytes } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { signInNewAccount } from './account-activation';
import { AccountCreation, emailOf } from './account-creation';
import { AccountSignIn } from './account-sign-in';
import { savedAdministratorCookies } from './administrator-cookies';
import { refreshAdministratorProof } from './administrator-proof';
import { test as base, expect, type BrowserHealth } from './fixtures';
import { clearSignInBudget } from './profile-state';
import { SecretsList } from './secrets-list';
import { KeycloakSignIn } from './sso';
import { freshTotp, serverTotp } from './totp';

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

interface SsoAccounts {
  /** The password-only user's vault password, which the MFA user's account never needs. */
  password: string;
  secret: string;
  /** The step the password-only user's vault sign-in spent, which the vault never accepts again. */
  step: number;
}

// Single sign-on refuses an account awaiting activation, and both identity provider users map to
// one vault account each by address. Provisioned once per run, by whichever case needs it first.
// Playwright replaces the worker after a failure, so the accounts are kept on disk for its successor.
const savedAccountsFile = `${process.env['E2E_TEST_RESULTS'] || 'test-results'}/sso-accounts.json`;

const test = base.extend<{ ssoAccounts: SsoAccounts }>({
  ssoAccounts: async ({ browserHealth }, use) => {
    await use(savedSsoAccounts() ?? (await provisionSsoAccounts(browserHealth)));
  },
});

/** The accounts this run's stack already provisioned, or undefined before the first provisioning. */
function savedSsoAccounts(): SsoAccounts | undefined {
  let saved: { stack: string; accounts: SsoAccounts };
  try {
    saved = JSON.parse(readFileSync(savedAccountsFile, 'utf8')) as typeof saved;
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === 'ENOENT') return undefined;
    throw error;
  }
  return saved.stack === process.env['E2E_COMPOSE_PROJECT'] ? saved.accounts : undefined;
}

async function provisionSsoAccounts(browserHealth: BrowserHealth): Promise<SsoAccounts> {
  const administrator = await (await browserHealth.newContext()).newPage();
  await administrator.context().addCookies(await savedAdministratorCookies());
  await refreshAdministratorProof(administrator);
  const creation = new AccountCreation(administrator);
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

  await signInNewAccount(
    await (await browserHealth.newContext()).newPage(),
    mfaUsername,
    `E2e-${randomBytes(24).toString('hex')}`,
  );
  const password = `E2e-${randomBytes(24).toString('hex')}`;
  const enrolled = await signInNewAccount(
    await (await browserHealth.newContext()).newPage(),
    passwordUsername,
    password,
  );
  const accounts = { password, ...enrolled };
  // A later run reuses this results folder with a new stack, which never has these accounts.
  writeFileSync(
    savedAccountsFile,
    JSON.stringify({ stack: process.env['E2E_COMPOSE_PROJECT'], accounts }),
  );
  return accounts;
}

test('a user whose identity provider completed MFA lands on Secrets', async ({
  browserHealth,
  // eslint-disable-next-line @typescript-eslint/no-unused-vars -- Asking for it activates sso-mfa's vault account.
  ssoAccounts,
}) => {
  test.setTimeout(120_000);
  const page = await (await browserHealth.newContext()).newPage();
  clearSignInBudget();
  const vault = new AccountSignIn(page);
  await vault.open();
  const ssoButton = vault.singleSignOnButton('Keycloak');
  await expect(ssoButton).toBeVisible();
  for (const width of [300, 2000]) {
    await page.setViewportSize({ width, height: 900 });
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(width);
    expect(await ssoButton.evaluate((el) => el.scrollWidth <= el.clientWidth + 1)).toBe(true);
  }
  await page.setViewportSize({ width: 1280, height: 720 });
  await vault.continueWithSingleSignOn('Keycloak');
  const keycloak = new KeycloakSignIn(page);
  await keycloak.submitPassword('sso-mfa', idpPassword());
  await keycloak.submitOneTimeCode(await freshTotp(idpAuthenticatorSecret()));
  await expect(page).toHaveURL(/\/Secrets$/);
  await expect(new SecretsList(page).heading).toBeVisible();
});

test('a user whose identity provider did not complete MFA finishes with an authenticator code', async ({
  browserHealth,
  ssoAccounts,
}) => {
  test.setTimeout(120_000);
  const page = await (await browserHealth.newContext()).newPage();
  clearSignInBudget();
  const vault = new AccountSignIn(page);
  await vault.open();
  await vault.continueWithSingleSignOn('Keycloak');
  await new KeycloakSignIn(page).submitPassword('sso-password', idpPassword());
  await expect(page).toHaveURL(/\/Account\/Totp$/);
  const code = await serverTotp(page.request, ssoAccounts.secret, ssoAccounts.step);
  await vault.submitAuthenticatorCode(code.code);
  await expect(page).toHaveURL(/\/Secrets$/);
  await expect(new SecretsList(page).heading).toBeVisible();
});

test('a restarted second factor loads Login with its notice and permits single sign-on', async ({
  browserHealth,
  ssoAccounts,
}) => {
  test.setTimeout(120_000);
  const page = await (await browserHealth.newContext()).newPage();
  const signIn = new AccountSignIn(page);
  clearSignInBudget();
  await signIn.open();
  await signIn.submitPassword(passwordUsername, ssoAccounts.password);
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
