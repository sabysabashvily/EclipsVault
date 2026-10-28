import type { BrowserContext } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { test, expect } from './fixtures';
import { AccountCreation } from './account-creation';
import { AccountSignIn } from './account-sign-in';
import { AuthenticatorEnrollment } from './authenticator-enrollment';
import { MailboxProfile } from './mailbox';
import { ProfilePage } from './profile';
import { expireProfileOperation } from './profile-state';
import { freshTotp } from './totp';

test.describe.configure({ mode: 'serial' });
let ownerCookies: Awaited<ReturnType<BrowserContext['cookies']>> = [];
let password = `E2e-${randomBytes(24).toString('hex')}`;
let secret = '';
let lastStep = -1;

// This account belongs only to this flow; sensitive changes never retire another suite's session.
test('Profile opens through production routes after real enrollment', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  const administrator = JSON.parse(
    await readFile(
      `${process.env['E2E_TEST_RESULTS'] || 'test-results'}/sessions-administrator.json`,
      'utf8',
    ),
  ) as { cookies: typeof ownerCookies };
  await page.context().addCookies(administrator.cookies);
  const creation = new AccountCreation(page);
  await creation.open();
  await creation.createMailboxOwner('e2e-profile-owner', password);
  await expect(page).toHaveURL(/\/Admin\/Users$/);
  const profile = new ProfilePage(page);
  await profile.freshBudget();
  const owner = await (await browserHealth.newContext()).newPage();
  const signIn = new AccountSignIn(owner);
  await signIn.open();
  await signIn.submitPassword('e2e-profile-owner', password);
  await expect(owner).toHaveURL(/\/Account\/EnrollTotp$/);
  const enrollment = new AuthenticatorEnrollment(owner);
  secret = await enrollment.secret.innerText();
  await enrollment.confirm(await freshTotp(secret));
  lastStep = Math.floor(Date.now() / 30_000);
  await expect(owner).toHaveURL(/\/Secrets$/);
  await new ProfilePage(owner).open('/pRoFiLe/InDeX');
  await expect(owner).toHaveTitle('Profile — EclipsVault');
  await expect(owner.getByRole('heading', { name: 'Passkeys', exact: true })).toBeVisible();
  ownerCookies = await owner.context().cookies();
});

test('visual parity: Profile preserves cards and labels in both themes and on a narrow screen', async ({
  page,
}, testInfo) => {
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await expect(page.getByRole('heading', { level: 2 })).toHaveText([
    'Profile picture',
    'Details',
    'Access',
    'Security',
    'Passkeys',
  ]);
  await expect(page.getByLabel('Username', { exact: true })).toBeDisabled();
  await expect(profile.email).toHaveAttribute('readonly', '');
  await expect(
    page.getByText('Argon2id-hashed. Change it if you suspect it has been compromised.'),
  ).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath('profile-dark.png'), fullPage: true });
  await page.getByText('Mailbox Owner', { exact: true }).click();
  await page.getByRole('menuitem', { name: /Switch to light/ }).click();
  // eslint-disable-next-line playwright/no-raw-locators -- Theme is a document attribute shared by both shells.
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light'); // Theme is exposed on the document for both shells.
  await page.screenshot({ path: testInfo.outputPath('profile-light.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(profile.displayName).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath('profile-narrow.png'), fullPage: true });
});

test('display name and avatar update the live shell without changing the email', async ({
  page,
}) => {
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  const email = await profile.email.inputValue();
  await profile.displayName.fill('Profile Browser Owner');
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(
    page.getByRole('status').filter({ hasText: 'Your profile was updated.' }),
  ).toBeVisible();
  await expect(page.getByText('Profile Browser Owner', { exact: true })).toBeVisible();
  await expect(profile.email).toHaveValue(email);
  // The hidden file picker is intentionally reached by its native file-input type.
  // eslint-disable-next-line playwright/no-raw-locators -- The hidden native file picker has no visible label.
  await page.locator('input[type="file"]').setInputFiles({
    name: 'avatar.png',
    mimeType: 'image/png',
    buffer: Buffer.from(
      'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==',
      'base64',
    ),
  });
  await expect(page.getByText('Your profile picture was updated.', { exact: true })).toBeVisible();
  const avatar = await page.request.get(
    (await page.getByRole('img', { name: 'Your avatar', exact: true }).getAttribute('src')) || '',
  );
  expect(avatar.headers()['cache-control']).toContain('no-store');
  expect(avatar.headers()['content-type']).toContain('image/png');
  await page.getByRole('button', { name: 'Remove', exact: true }).click();
  await profile.confirm();
  await expect(page.getByText('Your profile picture was removed.', { exact: true })).toBeVisible();
  ownerCookies = await page.context().cookies();
});

test('real password verification renews the current browser and refuses its copied cookie', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  await page.context().addCookies(ownerCookies);
  const copied = await browserHealth.newContext();
  await copied.addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await profile.freshBudget();
  const replacement = `E2e-new-${randomBytes(24).toString('hex')}`;
  await profile.changePassword(password, replacement);
  await expect(profile.verification).toBeVisible();
  await expect(
    profile.verification.getByRole('button', { name: 'Cancel', exact: true }),
  ).toBeFocused();
  lastStep = await profile.verify(password, secret, lastStep);
  await expect(
    page.getByText('Your password was changed. Any other signed-in devices have been signed out.', {
      exact: true,
    }),
  ).toBeVisible();
  password = replacement;
  ownerCookies = await page.context().cookies();
  expect((await copied.request.get('/api/ui/me')).status()).toBe(401);
  await page.getByRole('link', { name: 'Back to profile', exact: true }).click();
  await expect(profile.heading).toBeVisible();
  await profile.displayName.fill('After Password Renewal');
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();
  await expect(page.getByText('Your profile was updated.', { exact: true })).toBeVisible();
  ownerCookies = await page.context().cookies();
});

test('a real SMTP mailbox code activates the address only after Profile confirmation', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  await page.context().addCookies(ownerCookies);
  const copied = await browserHealth.newContext();
  await copied.addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  const original = await profile.email.inputValue();
  await profile.newEmail.fill('e2e-profile-confirmed@example.test');
  await page.getByRole('button', { name: 'Change email', exact: true }).click();
  lastStep = await profile.verify(password, secret, lastStep);
  await expect(page.getByLabel('Mailbox confirmation code')).toBeVisible();
  await expect(profile.email).toHaveValue(original);
  const code = await new MailboxProfile(page).mailboxCode('e2e-profile-confirmed@example.test');
  await page.getByLabel('Mailbox confirmation code').fill(code);
  await page.getByRole('button', { name: 'Confirm email', exact: true }).click();
  await expect(profile.email).toHaveValue('e2e-profile-confirmed@example.test');
  expect((await copied.request.get('/api/ui/me')).status()).toBe(401);
  ownerCookies = await page.context().cookies();
});

test('ten real recovery codes print once and clear on Done, direct visits and refresh', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  await page.context().addCookies(ownerCookies);
  const copied = await browserHealth.newContext();
  await copied.addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await profile.freshBudget();
  await page.getByRole('button', { name: 'Generate codes', exact: true }).click();
  await profile.confirm();
  lastStep = await profile.verify(password, secret, lastStep);
  await expect(page).toHaveTitle('Recovery codes — EclipsVault');
  await expect(profile.codes).toHaveCount(10);
  const printed = page.waitForEvent('dialog');
  await page.evaluate(() => {
    window.print = () => window.alert('Printing recovery codes');
  });
  await page.getByRole('button', { name: 'Print', exact: true }).click();
  await (await printed).dismiss();
  await page.getByRole('button', { name: "I've saved them — done", exact: true }).click();
  await expect(profile.heading).toBeVisible();
  await expect(profile.codes).toHaveCount(0);
  expect((await copied.request.get('/api/ui/me')).status()).toBe(401);
  await profile.open('/PrOfIlE/ReCoVeRyCoDeS');
  await expect(
    page.getByText('Recovery codes are shown only once, right after you generate them.', {
      exact: true,
    }),
  ).toBeVisible();
  await page.reload();
  await expect(profile.codes).toHaveCount(0);
  ownerCookies = await page.context().cookies();
});

test('a UV virtual passkey is added, verifies a frozen change and is removed using a surviving authenticator', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(300_000);
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await profile.freshBudget();
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('WebAuthn.enable');
  const { authenticatorId } = await cdp.send('WebAuthn.addVirtualAuthenticator', {
    options: {
      protocol: 'ctap2',
      transport: 'internal',
      hasResidentKey: true,
      hasUserVerification: true,
      isUserVerified: true,
      automaticPresenceSimulation: true,
    },
  });
  const beforeAdd = await browserHealth.newContext();
  await beforeAdd.addCookies(ownerCookies);
  await page.getByLabel('New passkey name').fill('Profile UV key');
  await page.getByRole('button', { name: 'Add a passkey', exact: true }).click();
  await expect(profile.verification).toContainText('Add the passkey “Profile UV key”.');
  lastStep = await profile.verify(password, secret, lastStep);
  await expect(
    page.getByText('Passkey added. You can now sign in with it.', { exact: true }),
  ).toBeVisible();
  await expect(page.getByText(/^Added \d{1,2} \w{3} \d{4} UTC$/)).toBeVisible();
  expect((await beforeAdd.request.get('/api/ui/me')).status()).toBe(401);
  const beforeProof = await browserHealth.newContext();
  await beforeProof.addCookies(await page.context().cookies());
  await page.getByRole('button', { name: 'Regenerate', exact: true }).click();
  await profile.confirm();
  await profile.verification.getByLabel('Verify with').selectOption('Passkey');
  await profile.verification.getByRole('button', { name: 'Verify account', exact: true }).click();
  await expect(page).toHaveTitle('Recovery codes — EclipsVault');
  await expect(profile.codes).toHaveCount(10);
  await page.getByRole('button', { name: "I've saved them — done", exact: true }).click();
  expect((await beforeProof.request.get('/api/ui/me')).status()).toBe(401);
  const beforeRemoval = await browserHealth.newContext();
  await beforeRemoval.addCookies(await page.context().cookies());
  await page.getByRole('button', { name: 'Remove', exact: true }).click();
  await profile.confirm();
  await expect(profile.verification.getByLabel('Verify with')).not.toContainText('Passkey');
  lastStep = await profile.verify(password, secret, lastStep);
  await expect(page.getByText('No passkeys registered yet.', { exact: true })).toBeVisible();
  expect((await beforeRemoval.request.get('/api/ui/me')).status()).toBe(401);
  await cdp.send('WebAuthn.removeVirtualAuthenticator', { authenticatorId });
  ownerCookies = await page.context().cookies();
});

test('authenticator reset ends current and copied sessions before Legacy reenrollment', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  await page.context().addCookies(ownerCookies);
  const copied = await browserHealth.newContext();
  await copied.addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await profile.freshBudget();
  await page.getByRole('button', { name: 'Reset authenticator', exact: true }).click();
  await expect(
    profile.confirmation.getByRole('button', { name: 'Cancel', exact: true }),
  ).toBeFocused();
  await profile.confirm();
  await expect(profile.confirmation).not.toBeVisible();
  lastStep = await profile.verify(password, secret, lastStep);
  await expect(page).toHaveURL(/\/Account\/Login$/);
  await expect(
    page.getByText('Your authenticator was reset. Sign in to set up a new one.', { exact: true }),
  ).toBeVisible();
  expect(
    (await page.context().cookies()).some((cookie) => cookie.name === 'EclipsVault.Session'),
  ).toBe(false);
  expect((await copied.request.get('/api/ui/me')).status()).toBe(401);
  const signIn = new AccountSignIn(page);
  await signIn.submitPassword('e2e-profile-owner', password);
  await expect(page).toHaveURL(/\/Account\/EnrollTotp$/);
  const enrollment = new AuthenticatorEnrollment(page);
  const replacementSecret = await enrollment.secret.innerText();
  expect(replacementSecret).not.toBe(secret);
  await enrollment.confirm(await freshTotp(replacementSecret));
  lastStep = Math.floor(Date.now() / 30_000);
  secret = replacementSecret;
  await expect(page).toHaveURL(/\/Secrets$/);
  await profile.open();
  await expect(page.getByText('None generated yet.', { exact: false })).toBeVisible();
  ownerCookies = await page.context().cookies();
});

test('Cancel and Escape clear frozen proof fields and return keyboard focus to their opener', async ({
  page,
}) => {
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  const reset = page.getByRole('button', { name: 'Reset authenticator', exact: true });
  await reset.click();
  await page.keyboard.press('Escape');
  await expect(reset).toBeFocused();
  await reset.click();
  await profile.confirm();
  await profile.verification
    .getByLabel('Current password', { exact: true })
    .fill('discarded-password');
  await profile.verification.getByLabel('Authenticator code', { exact: true }).fill('123456');
  await page.keyboard.press('Escape');
  await expect(reset).toBeFocused();
  await reset.click();
  await profile.confirm();
  await expect(profile.verification.getByLabel('Current password', { exact: true })).toHaveValue(
    '',
  );
  await expect(profile.verification.getByLabel('Authenticator code', { exact: true })).toHaveValue(
    '',
  );
  await profile.verification.getByRole('button', { name: 'Cancel', exact: true }).click();
});

test('a wrong account proof stays on Profile without sending the reset mutation', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  const writes: string[] = [];
  page.on('request', (request) => {
    if (request.url().endsWith('/authenticator/reset')) writes.push(request.method());
  });
  browserHealth.expectConsoleError({
    text: /status of 400/,
    url: /\/api\/ui\/profile\/operations\/[\da-f-]+\/verify$/,
  });
  await page.getByRole('button', { name: 'Reset authenticator', exact: true }).click();
  await profile.confirm();
  await profile.verification
    .getByLabel('Current password', { exact: true })
    .fill('Wrong-Password-For-Profile!');
  await profile.verification.getByLabel('Authenticator code').fill('000000');
  await profile.verification.getByRole('button', { name: 'Verify account', exact: true }).click();
  await expect(profile.verification.getByRole('alert')).toHaveText(
    "That didn't verify your account. Check what you entered and try again.",
  );
  await expect(profile.verification).toBeVisible();
  expect(writes).toEqual([]);
  await profile.verification.getByRole('button', { name: 'Cancel', exact: true }).click();
});

test('an expired prepared change is refused by the real server without a credential mutation', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await profile.freshBudget();
  browserHealth.expectConsoleError({
    text: /status of 409/,
    url: /\/api\/ui\/profile\/operations\/[\da-f-]+\/verify$/,
  });
  const prepared = page.waitForResponse(
    (response) =>
      response.url().endsWith('/api/ui/profile/operations') &&
      response.request().method() === 'POST',
  );
  await page.getByRole('button', { name: 'Reset authenticator', exact: true }).click();
  await profile.confirm();
  const operation = (await (await prepared).json()) as { operationId: string };
  expireProfileOperation(operation.operationId);
  await profile.verify(password, secret, lastStep);
  await expect(profile.verification.getByRole('alert')).toHaveText(
    'This change was not verified in time. Start it again.',
  );
  await expect(page).toHaveURL(/\/Profile$/);
  await profile.verification.getByRole('button', { name: 'Cancel', exact: true }).click();
});
