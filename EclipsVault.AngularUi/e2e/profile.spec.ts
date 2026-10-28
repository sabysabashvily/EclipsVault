import type { BrowserContext } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { test, expect } from './fixtures';
import { AccountCreation } from './account-creation';
import { AccountSignIn } from './account-sign-in';
import { AuthenticatorEnrollment } from './authenticator-enrollment';
import { openPalette } from './palette';
import { avatarImage, ChangePasswordPage, ProfilePage } from './profile';
import {
  expireProfileOperation,
  expireMailboxCode,
  expireAccountGrant,
  expirePasskeyCeremony,
  setSmtpAvailable,
  pauseSessionStore,
  rejectAuditWrites,
  profileCredentialGeneration,
  pauseSmtpTransport,
} from './profile-state';
import { mailboxCode } from './smtp-capture';
import { currentTotpStep, freshTotp } from './totp';

test.describe.configure({ mode: 'serial' });
let ownerCookies: Awaited<ReturnType<BrowserContext['cookies']>> = [];
let password = `E2e-${randomBytes(24).toString('hex')}`;
let secret = '';
let lastStep = -1;
let recoveryProof = '';

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
  await profile.waitForNextThrottleWindow();
  const owner = await (await browserHealth.newContext()).newPage();
  const signIn = new AccountSignIn(owner);
  await signIn.open();
  await signIn.submitPassword('e2e-profile-owner', password);
  await expect(owner).toHaveURL(/\/Account\/EnrollTotp$/);
  const enrollment = new AuthenticatorEnrollment(owner);
  secret = await enrollment.secret.innerText();
  await enrollment.confirm(await freshTotp(secret));
  lastStep = currentTotpStep();
  await expect(owner).toHaveURL(/\/Secrets$/);
  await new ProfilePage(owner).open('/pRoFiLe/InDeX');
  await expect(owner).toHaveTitle('Profile — EclipsVault');
  await expect(owner.getByRole('heading', { name: 'Passkeys', exact: true })).toBeVisible();
  ownerCookies = await owner.context().cookies();
});

test('both account menus, both palettes and Security checkup open Profile on the same session', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(new URL(request.url()).pathname);
  });
  const session = async () =>
    (await page.context().cookies()).find((cookie) => cookie.name === 'EclipsVault.Session')?.value;
  const original = await session();

  await test.step('the Angular account menu and palette stay in the app', async () => {
    await page.goto('/Access');
    documents.length = 0;
    await page.getByText('Mailbox Owner', { exact: true }).click();
    await page.getByRole('menuitem', { name: 'Profile', exact: true }).click();
    await expect(profile.heading).toBeVisible();
    await expect(page).toHaveTitle('Profile — EclipsVault');
    await page.goto('/Access');
    documents.length = 0;
    await openPalette(page);
    await page.getByRole('combobox').fill('Profile');
    await page.getByRole('option', { name: /Profile/ }).click();
    await expect(profile.heading).toBeVisible();
    await page.goto('/Security');
    documents.length = 0;
    await page.getByRole('link', { name: 'Add a passkey', exact: true }).click();
    await expect(profile.heading).toBeVisible();
    expect(documents).toEqual([]);
  });

  await test.step('the Legacy account menu and palette load Profile into Angular', async () => {
    await page.goto('/Secrets');
    documents.length = 0;
    await page.getByText('Mailbox Owner', { exact: true }).click();
    await page.getByRole('menuitem', { name: 'Profile', exact: true }).click();
    await expect(profile.heading).toBeVisible();
    await page.goto('/Secrets');
    documents.length = 0;
    await openPalette(page);
    await page.getByRole('combobox').fill('Profile');
    await page.getByRole('option', { name: /Profile/ }).click();
    await expect(profile.heading).toBeVisible();
    expect(documents).toEqual(['/Profile', '/Profile']);
  });

  await test.step('deep links keep their titles and a signed-out visit returns after sign-in', async () => {
    await profile.open('/pRoFiLe/ChAnGePaSsWoRd');
    await expect(page).toHaveTitle('Change password — EclipsVault');
    await expect(page.getByRole('heading', { name: 'Change password', exact: true })).toBeVisible();
    const anonymous = await (await browserHealth.newContext()).newPage();
    await anonymous.goto('/pRoFiLe/ChAnGePaSsWoRd');
    await expect(anonymous).toHaveURL(/\/Account\/Login\?ReturnUrl=%2Fprofile%2Fchangepassword$/i);
  });
  expect(await session()).toBe(original);
});

test('the password form refuses mismatched, unchanged and breached choices before preparing a change', async ({
  page,
}) => {
  await page.context().addCookies(ownerCookies);
  const prepared: string[] = [];
  page.on('request', (request) => {
    if (new URL(request.url()).pathname === '/api/ui/profile/operations')
      prepared.push(request.method());
  });
  await page.goto('/Profile/ChangePassword');
  const form = new ChangePasswordPage(page);
  const refusal = page.getByRole('alert');
  await form.currentPassword.fill(password);

  await form.newPassword.fill(`E2e-mismatch-${randomBytes(8).toString('hex')}`);
  await expect(form.strength).toBeVisible();
  await form.confirmPassword.fill('E2e-something-else-entirely');
  await form.submit.click();
  await expect(refusal).toHaveText('The passwords do not match.');

  await form.newPassword.fill(password);
  await form.confirmPassword.fill(password);
  await form.submit.click();
  await expect(refusal).toHaveText('Choose a different password from your current one.');

  await form.newPassword.fill('000000000000');
  await form.confirmPassword.fill('000000000000');
  await expect(
    page.getByText(
      '⚠ This password has appeared in known data breaches — choose a different one.',
      { exact: true },
    ),
  ).toBeVisible();
  await form.submit.click();
  await expect(refusal).toHaveText(
    'This password has appeared in known data breaches — choose a different one.',
  );

  await expect(form.newPassword).toHaveAttribute('type', 'password');
  await form.showPassword.click();
  await expect(form.newPassword).toHaveAttribute('type', 'text');
  await expect(form.newPassword).toBeFocused();
  await form.hidePassword.click();
  await expect(form.newPassword).toHaveAttribute('type', 'password');
  expect(prepared).toEqual([]);
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
  await expect(page.getByRole('term')).toHaveText(['Clearance', 'Project']);
  await expect(
    page.getByText(
      'Clearance and project are set by an administrator. Ask one if these need to change.',
      { exact: true },
    ),
  ).toBeVisible();
  // eslint-disable-next-line playwright/no-raw-locators -- The rows' titles are unlabelled <strong> elements.
  await expect(page.locator('.settings-row > div > strong')).toHaveText([
    'Password',
    'Authenticator (2FA)',
    'Recovery codes',
    'Sessions',
  ]);
  await expect(
    page.getByText('Argon2id-hashed. Change it if you suspect it has been compromised.'),
  ).toBeVisible();
  const details = await page.getByRole('heading', { name: 'Details', exact: true }).boundingBox();
  const access = await page.getByRole('heading', { name: 'Access', exact: true }).boundingBox();
  expect(access!.x).toBeGreaterThan(details!.x + details!.width);

  const signOut = page.getByRole('button', { name: 'Sign out everywhere', exact: true });
  const colour = (button: typeof signOut) =>
    button.evaluate((element) => getComputedStyle(element).color);
  expect(await colour(signOut)).not.toBe(await colour(profile.resetAuthenticator));
  await page.screenshot({ path: testInfo.outputPath('profile-dark.png'), fullPage: true });
  await page.getByText('Mailbox Owner', { exact: true }).click();
  await page.getByRole('menuitem', { name: /Switch to light/ }).click();
  // eslint-disable-next-line playwright/no-raw-locators -- Theme is a document attribute shared by both shells.
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
  expect(await colour(signOut)).not.toBe(await colour(profile.resetAuthenticator));
  await page.screenshot({ path: testInfo.outputPath('profile-light.png'), fullPage: true });

  await page.setViewportSize({ width: 390, height: 844 });
  await expect(profile.displayName).toBeVisible();
  const narrowDetails = await page
    .getByRole('heading', { name: 'Details', exact: true })
    .boundingBox();
  const narrowAccess = await page
    .getByRole('heading', { name: 'Access', exact: true })
    .boundingBox();
  expect(narrowAccess!.y).toBeGreaterThan(narrowDetails!.y);
  expect(Math.abs(narrowAccess!.x - narrowDetails!.x)).toBeLessThan(2);
  await page.screenshot({ path: testInfo.outputPath('profile-narrow.png'), fullPage: true });
});

test('visual parity: Change password keeps its Legacy fields, hint, actions and meter', async ({
  page,
}, testInfo) => {
  await page.context().addCookies(ownerCookies);
  await page.goto('/Profile/ChangePassword');
  const form = new ChangePasswordPage(page);
  await expect(form.heading).toBeVisible();
  await expect(
    page.getByText(
      'Confirm your current password, then choose a new one (at least 12 characters).',
      { exact: true },
    ),
  ).toBeVisible();
  const current = await form.currentPassword.boundingBox();
  const candidate = await form.newPassword.boundingBox();
  const confirmation = await form.confirmPassword.boundingBox();
  expect(candidate!.y).toBeGreaterThan(current!.y);
  expect(confirmation!.y).toBeGreaterThan(candidate!.y);
  await expect(
    page.getByText(
      'At least 12 characters. Checked against a corpus of passwords exposed in public breaches (NIST 800-63B).',
      { exact: true },
    ),
  ).toBeVisible();
  await expect(page.getByRole('link', { name: 'Back to profile', exact: true })).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath('change-password.png'), fullPage: true });
  await form.newPassword.fill(`E2e-meter-${randomBytes(8).toString('hex')}`);
  await expect(form.strength).toBeVisible();
  await page.screenshot({
    path: testInfo.outputPath('change-password-meter-dark.png'),
    fullPage: true,
  });
  await page.getByText('Mailbox Owner', { exact: true }).click();
  await page.getByRole('menuitem', { name: /Switch to light/ }).click();
  await expect(form.strength).toBeVisible();
  await page.screenshot({
    path: testInfo.outputPath('change-password-meter-light.png'),
    fullPage: true,
  });
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

test('a file that is not an image is refused and leaves the avatar unchanged', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(ownerCookies);
  await new ProfilePage(page).open();
  const avatar = page.getByRole('img', { name: 'Your avatar', exact: true });
  const before = await avatar.getAttribute('src');
  browserHealth.expectConsoleError({ text: /status of 400/, url: /\/api\/ui\/profile\/avatar$/ });
  // eslint-disable-next-line playwright/no-raw-locators -- The hidden native file picker has no visible label.
  await page.locator('input[type="file"]').setInputFiles({
    name: 'not-an-image.png',
    mimeType: 'image/png',
    buffer: Buffer.from('This is plain text, not a PNG.'),
  });
  await expect(page.getByRole('alert')).toHaveText(
    'That file could not be read as an image. Upload a JPEG or PNG.',
  );
  await expect(avatar).toHaveAttribute('src', before || '');
});

test('a JPEG avatar is cropped into a metadata-free 256 pixel PNG', async ({ page }) => {
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  const jpeg = await avatarImage(page, 600, 200, 'image/jpeg');
  const comment = Buffer.from('Disposable avatar metadata');
  const segment = Buffer.alloc(comment.length + 4);
  segment.writeUInt16BE(0xfffe, 0);
  segment.writeUInt16BE(comment.length + 2, 2);
  comment.copy(segment, 4);
  const image = Buffer.concat([jpeg.subarray(0, 2), segment, jpeg.subarray(2)]);
  await profile.avatarFile.setInputFiles({
    name: 'avatar.jpg',
    mimeType: 'image/jpeg',
    buffer: image,
  });
  await expect(page.getByText('Your profile picture was updated.', { exact: true })).toBeVisible();
  const stored = await page.request.get(
    (await page.getByRole('img', { name: 'Your avatar', exact: true }).getAttribute('src'))!,
  );
  const png = await stored.body();
  expect(stored.headers()['content-type']).toContain('image/png');
  expect(png.readUInt32BE(16)).toBe(256);
  expect(png.readUInt32BE(20)).toBe(256);
  expect(png.includes(comment)).toBe(false);
  await page.getByRole('button', { name: 'Remove', exact: true }).click();
  await profile.confirm();
  await expect(page.getByText('Your profile picture was removed.', { exact: true })).toBeVisible();
  ownerCookies = await page.context().cookies();
});

test('an oversized avatar file is refused without sending an upload', async ({ page }) => {
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  const uploads: string[] = [];
  page.on('request', (request) => {
    if (request.method() === 'POST' && request.url().endsWith('/profile/avatar'))
      uploads.push(request.method());
  });
  const fourMiB = 4 * 1024 * 1024;
  await profile.avatarFile.setInputFiles({
    name: 'oversized.png',
    mimeType: 'image/png',
    buffer: Buffer.alloc(fourMiB + 1),
  });
  await expect(page.getByRole('alert')).toHaveText('Images must be 4 MB or smaller.');
  expect(uploads).toEqual([]);
});

test('an avatar over the pixel limit is refused by the real server', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  const image = await avatarImage(page, 6001, 1, 'image/png');
  browserHealth.expectConsoleError({ text: /status of 400/, url: /\/api\/ui\/profile\/avatar$/ });
  await profile.avatarFile.setInputFiles({
    name: 'wide.png',
    mimeType: 'image/png',
    buffer: image,
  });
  await expect(page.getByRole('alert')).toHaveText('Image dimensions must not exceed 6000×6000.');
});

test('an incomplete avatar decode is refused by the real server', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  const image = await avatarImage(page, 300, 300, 'image/jpeg');
  browserHealth.expectConsoleError({ text: /status of 400/, url: /\/api\/ui\/profile\/avatar$/ });
  await profile.avatarFile.setInputFiles({
    name: 'incomplete.jpg',
    mimeType: 'image/jpeg',
    buffer: image.subarray(0, Math.floor(image.length / 2)),
  });
  await expect(page.getByRole('alert')).toHaveText(
    'That file could not be read as an image. Upload a JPEG or PNG.',
  );
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
  await profile.waitForNextThrottleWindow();
  const replacement = `E2e-new-${randomBytes(24).toString('hex')}`;
  await new ChangePasswordPage(page).changeFromProfile(password, replacement);
  await expect(profile.verification).toBeVisible();
  await expect(profile.cancelVerification).toBeFocused();
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
  await expect(profile.mailboxCode).toBeVisible();
  await expect(profile.email).toHaveValue(original);
  const code = await mailboxCode('e2e-profile-confirmed@example.test');
  await profile.mailboxCode.fill(code);
  await page.getByRole('button', { name: 'Confirm email', exact: true }).click();
  await expect(profile.email).toHaveValue('e2e-profile-confirmed@example.test');
  expect((await copied.request.get('/api/ui/me')).status()).toBe(401);
  ownerCookies = await page.context().cookies();
});

test('a wrong mailbox code is refused, and cancelling keeps the active email', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await profile.waitForNextThrottleWindow();
  const verifications: string[] = [];
  page.on('request', (request) => {
    if (/\/api\/ui\/profile\/operations\/[\da-f-]+\/verify$/.test(request.url()))
      verifications.push(request.method());
  });
  const active = await profile.email.inputValue();
  await profile.newEmail.fill('e2e-profile-cancelled@example.test');
  await page.getByRole('button', { name: 'Change email', exact: true }).click();
  lastStep = await profile.fillVerification(password, secret, lastStep);
  await profile.verifyAccount.dblclick();
  await expect(profile.mailboxCode).toBeVisible();
  // Delivery is real; the delivered code is 32 hex characters, so six zeros can never match it.
  await mailboxCode('e2e-profile-cancelled@example.test');
  expect(verifications).toEqual(['POST']);

  browserHealth.expectConsoleError({
    text: /status of 400/,
    url: /\/api\/ui\/profile\/email\/confirm$/,
  });
  await profile.mailboxCode.fill('000000');
  await page.getByRole('button', { name: 'Confirm email', exact: true }).click();
  await expect(page.getByRole('alert')).toHaveText('That mailbox code is not valid.');
  await expect(profile.email).toHaveValue(active);

  await page.getByRole('button', { name: 'Cancel email change', exact: true }).click();
  await expect(
    page.getByText('Email change cancelled. Your current email is still active.', { exact: true }),
  ).toBeVisible();
  await expect(profile.mailboxCode).toHaveCount(0);
  await expect(profile.email).toHaveValue(active);
});

test('an expired mailbox code leaves the active address unchanged', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await profile.waitForNextThrottleWindow();
  const active = await profile.email.inputValue();
  const prepared = page.waitForResponse(
    (response) =>
      response.url().endsWith('/api/ui/profile/operations') &&
      response.request().method() === 'POST',
  );
  await profile.newEmail.fill('e2e-profile-expired@example.test');
  await page.getByRole('button', { name: 'Change email', exact: true }).click();
  lastStep = await profile.verify(password, secret, lastStep);
  await expect(profile.mailboxCode).toBeVisible();
  const operation = (await (await prepared).json()) as { operationId: string };
  const code = await mailboxCode('e2e-profile-expired@example.test');
  expireMailboxCode(operation.operationId);
  browserHealth.expectConsoleError({
    text: /status of 409/,
    url: /\/api\/ui\/profile\/email\/confirm$/,
  });
  await profile.mailboxCode.fill(code);
  await page.getByRole('button', { name: 'Confirm email', exact: true }).click();
  await expect(page.getByRole('alert')).toHaveText(
    'The mailbox code expired. Start the change again.',
  );
  await expect(profile.email).toHaveValue(active);
  await expect(profile.newEmail).toBeVisible();
});

test('an expired account grant requires fresh proof of the same pending mailbox', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  const active = await profile.email.inputValue();
  const prepared = page.waitForResponse(
    (response) =>
      response.url().endsWith('/api/ui/profile/operations') &&
      response.request().method() === 'POST',
  );
  await profile.newEmail.fill('e2e-profile-grant@example.test');
  await page.getByRole('button', { name: 'Change email', exact: true }).click();
  lastStep = await profile.verify(password, secret, lastStep);
  await expect(profile.mailboxCode).toBeVisible();
  const operation = (await (await prepared).json()) as { operationId: string };
  const code = await mailboxCode('e2e-profile-grant@example.test');
  expireAccountGrant(operation.operationId);
  browserHealth.expectConsoleError({
    text: /status of 409/,
    url: /\/api\/ui\/profile\/email\/confirm$/,
  });
  await profile.mailboxCode.fill(code);
  await page.getByRole('button', { name: 'Confirm email', exact: true }).click();
  await expect(page.getByRole('alert')).toHaveText(
    'Your verification expired. Verify your account again.',
  );
  await expect(profile.email).toHaveValue(active);
  await page.getByRole('button', { name: 'Verify account again', exact: true }).click();
  await expect(profile.verification).toContainText('e2e-profile-grant@example.test');
  lastStep = await profile.verify(password, secret, lastStep);
  await expect(
    page.getByText('Account verified. Enter the mailbox code and confirm your email.', {
      exact: true,
    }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Cancel email change', exact: true }).click();
  await expect(profile.newEmail).toBeVisible();
});

test('a real SMTP outage leaves email inactive and reports delivery failure', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await profile.waitForNextThrottleWindow();
  const active = await profile.email.inputValue();
  await profile.newEmail.fill('e2e-profile-undelivered@example.test');
  await page.getByRole('button', { name: 'Change email', exact: true }).click();
  browserHealth.expectConsoleError({
    text: /status of 409/,
    url: /\/api\/ui\/profile\/email\/request$/,
  });
  setSmtpAvailable(false);
  try {
    lastStep = await profile.verify(password, secret, lastStep);
    await expect(page.getByRole('alert')).toHaveText(
      'Email changes require configured, enabled SMTP. Delivery may be unavailable; verify again before deliberately resending.',
      { timeout: 90_000 },
    );
    await expect(profile.email).toHaveValue(active);
  } finally {
    setSmtpAvailable(true);
  }
  await page.getByRole('button', { name: 'Cancel email change', exact: true }).click();
  await expect(profile.newEmail).toBeVisible();
});

test('visual parity: ten recovery codes print once and clear on Done, direct visits and refresh', async ({
  page,
  browserHealth,
}, testInfo) => {
  test.setTimeout(240_000);
  await page.context().addCookies(ownerCookies);
  const copied = await browserHealth.newContext();
  await copied.addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await profile.waitForNextThrottleWindow();
  await page.getByRole('button', { name: 'Generate codes', exact: true }).click();
  await profile.confirm();
  lastStep = await profile.verify(password, secret, lastStep);
  await expect(page).toHaveTitle('Recovery codes — EclipsVault');
  await expect(profile.codes).toHaveCount(10);
  recoveryProof = await profile.codes.first().innerText();
  await expect(
    page.getByRole('heading', { name: 'Your recovery codes', exact: true }),
  ).toBeFocused();
  await page.screenshot({
    path: testInfo.outputPath('recovery-dark.png'),
    mask: [profile.codes],
    fullPage: true,
  });
  await page.getByText('After Password Renewal', { exact: true }).click();
  await page.getByRole('menuitem', { name: /Switch to light/ }).click();
  await expect(profile.codes).toHaveCount(10);
  await page.screenshot({
    path: testInfo.outputPath('recovery-light.png'),
    mask: [profile.codes],
    fullPage: true,
  });
  expect(await page.evaluate(() => Object.keys(localStorage))).toEqual([]);
  expect(await page.evaluate(() => Object.keys(sessionStorage))).toEqual([]);
  expect(page.url()).not.toContain(recoveryProof);
  const printed = page.waitForEvent('dialog');
  await page.evaluate(() => {
    window.print = () => window.alert('Printing recovery codes');
  });
  await page.getByRole('button', { name: 'Print', exact: true }).click();
  await (await printed).dismiss();
  await page.getByRole('button', { name: "I've saved them — done", exact: true }).click();
  await expect(profile.heading).toBeVisible();
  await expect(profile.codes).toHaveCount(0);
  await page.goBack();
  await expect(profile.codes).toHaveCount(0);
  await page.goForward();
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
  await profile.waitForNextThrottleWindow();
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
  await profile.verification.getByLabel('Verify with').selectOption('PasswordAndRecoveryCode');
  await profile.verificationPassword.fill(password);
  await profile.verification.getByLabel('Recovery code', { exact: true }).fill(recoveryProof);
  await profile.verifyAccount.click();
  recoveryProof = '';
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
  await profile.verifyAccount.click();
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

test('the server refuses a passkey ceremony after its stored two-minute deadline', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await profile.waitForNextThrottleWindow();
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
  await page.route('**/api/ui/profile/passkeys/registration/begin', async (route) => {
    const response = await route.fetch();
    const operation = route.request().postDataJSON() as { operationId: string };
    expirePasskeyCeremony(operation.operationId);
    await route.fulfill({ response });
  });
  browserHealth.expectConsoleError({
    text: /status of 409/,
    url: /\/api\/ui\/profile\/passkeys\/registration\/complete$/,
  });
  try {
    await page.getByLabel('New passkey name').fill('Expired UV key');
    await page.getByRole('button', { name: 'Add a passkey', exact: true }).click();
    lastStep = await profile.verify(password, secret, lastStep);
    await expect(page.getByRole('alert')).toHaveText('The passkey prompt expired. Try again.');
    await expect(page.getByText('No passkeys registered yet.', { exact: true })).toBeVisible();
  } finally {
    await cdp.send('WebAuthn.removeVirtualAuthenticator', { authenticatorId });
    await cdp.detach();
  }
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
  await profile.waitForNextThrottleWindow();
  await profile.resetAuthenticator.click();
  await expect(profile.cancelConfirmation).toBeFocused();
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
  lastStep = currentTotpStep();
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
  const reset = profile.resetAuthenticator;
  await reset.click();
  await page.keyboard.press('Escape');
  await expect(reset).toBeFocused();
  await reset.click();
  await profile.confirm();
  await profile.verificationPassword.fill('discarded-password');
  await profile.verificationCode.fill('123456');
  await page.keyboard.press('Escape');
  await expect(reset).toBeFocused();
  await reset.click();
  await profile.confirm();
  await expect(profile.verificationPassword).toHaveValue('');
  await expect(profile.verificationCode).toHaveValue('');
  for (const key of ['Tab', 'Shift+Tab']) {
    for (let press = 0; press < 6; press++) {
      await page.keyboard.press(key);
      expect(
        await profile.verification.evaluate((dialog) => dialog.contains(document.activeElement)),
      ).toBe(true);
    }
  }
  await profile.cancelVerification.click();
  await expect(reset).toBeFocused();
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
  await profile.resetAuthenticator.click();
  await profile.confirm();
  await profile.verificationPassword.fill('Wrong-Password-For-Profile!');
  await profile.verificationCode.fill('000000');
  await profile.verifyAccount.click();
  await expect(profile.verification.getByRole('alert')).toHaveText(
    "That didn't verify your account. Check what you entered and try again.",
  );
  await expect(profile.verification).toBeVisible();
  expect(writes).toEqual([]);
  await profile.cancelVerification.click();
});

test('an expired prepared change is refused by the real server without a credential mutation', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await profile.waitForNextThrottleWindow();
  browserHealth.expectConsoleError({
    text: /status of 409/,
    url: /\/api\/ui\/profile\/operations\/[\da-f-]+\/verify$/,
  });
  const prepared = page.waitForResponse(
    (response) =>
      response.url().endsWith('/api/ui/profile/operations') &&
      response.request().method() === 'POST',
  );
  await profile.resetAuthenticator.click();
  await profile.confirm();
  const operation = (await (await prepared).json()) as { operationId: string };
  expireProfileOperation(operation.operationId);
  await profile.verify(password, secret, lastStep);
  await expect(profile.verification.getByRole('alert')).toHaveText(
    'This change was not verified in time. Start it again.',
  );
  await expect(page).toHaveURL(/\/Profile$/);
  await profile.cancelVerification.click();
});

test('reset retains a passkey and refuses deleting the last method until Legacy reenrollment', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(420_000);
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await profile.waitForNextThrottleWindow();
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
  try {
    await page.getByLabel('New passkey name').fill('Surviving UV key');
    await page.getByRole('button', { name: 'Add a passkey', exact: true }).click();
    lastStep = await profile.verify(password, secret, lastStep);
    await expect(
      page.getByText('Passkey added. You can now sign in with it.', { exact: true }),
    ).toBeVisible();
    const completions: string[] = [];
    page.on('request', (request) => {
      if (/\/passkey\/complete$/.test(request.url())) completions.push(request.method());
    });
    await page.getByRole('button', { name: 'Generate codes', exact: true }).click();
    await profile.confirm();
    await profile.verification.getByLabel('Verify with').selectOption('Passkey');
    await cdp.send('WebAuthn.setAutomaticPresenceSimulation', { authenticatorId, enabled: false });
    await profile.verifyAccount.click();
    await expect(
      profile.verification.getByRole('button', { name: 'Verifying…', exact: true }),
    ).toBeDisabled();
    await profile.cancelVerification.click();
    await cdp.send('WebAuthn.setAutomaticPresenceSimulation', { authenticatorId, enabled: true });
    await expect(profile.verification).not.toBeVisible();
    expect(completions).toEqual([]);

    const copied = await browserHealth.newContext();
    await copied.addCookies(await page.context().cookies());
    await profile.resetAuthenticator.click();
    await profile.confirm();
    await profile.verification.getByLabel('Verify with').selectOption('Passkey');
    await profile.verifyAccount.click();
    await expect(page).toHaveURL(/\/Account\/Login$/);
    expect((await copied.request.get('/api/ui/me')).status()).toBe(401);
    await page.getByRole('button', { name: 'Sign in with a passkey', exact: true }).click();
    await expect(page).toHaveURL(/\/Secrets$/);
    await profile.open();
    await expect(page.getByText('Surviving UV key', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Remove', exact: true })).toBeDisabled();
    await expect(page.getByRole('button', { name: 'Generate codes', exact: true })).toBeDisabled();
    await expect(profile.resetAuthenticator).toBeDisabled();

    await profile.waitForNextThrottleWindow();
    await profile.signOutThisBrowser();
    await new AccountSignIn(page).submitPassword('e2e-profile-owner', password);
    await expect(page).toHaveURL(/\/Account\/EnrollTotp$/);
    const enrollment = new AuthenticatorEnrollment(page);
    secret = await enrollment.secret.innerText();
    await enrollment.confirm(await freshTotp(secret));
    lastStep = currentTotpStep();
    await expect(page).toHaveURL(/\/Secrets$/);
    await profile.open();
    await page.getByRole('button', { name: 'Remove', exact: true }).click();
    await profile.confirm();
    await expect(profile.verification.getByLabel('Verify with')).not.toContainText('Passkey');
    lastStep = await profile.verify(password, secret, lastStep);
    await expect(page.getByText('No passkeys registered yet.', { exact: true })).toBeVisible();
    ownerCookies = await page.context().cookies();
  } finally {
    await cdp.send('WebAuthn.removeVirtualAuthenticator', { authenticatorId });
    await cdp.detach();
  }
});

test('sign out everywhere ends this and copied sessions, and the account signs in again', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  await page.context().addCookies(ownerCookies);
  const copied = await browserHealth.newContext();
  await copied.addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await profile.waitForNextThrottleWindow();
  await page.getByRole('button', { name: 'Sign out everywhere', exact: true }).click();
  await expect(profile.cancelConfirmation).toBeFocused();
  await profile.confirm();
  await expect(page).toHaveURL(/\/Account\/Login$/);
  expect((await page.request.get('/api/ui/me')).status()).toBe(401);
  expect((await copied.request.get('/api/ui/me')).status()).toBe(401);
  await new AccountSignIn(page).submitPassword('e2e-profile-owner', password);
  await page.getByLabel('Authenticator code').fill(await freshTotp(secret, lastStep));
  lastStep = currentTotpStep();
  await page.getByRole('button', { name: 'Verify and sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/Secrets$/);
  ownerCookies = await page.context().cookies();
});

test('a rejected recovery mutation returns no codes and never replays generation', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await profile.waitForNextThrottleWindow();
  const generation = profileCredentialGeneration();
  const commits: string[] = [];
  await page.route('**/api/ui/profile/recovery-codes', async (route) => {
    commits.push(route.request().method());
    // Verification has already succeeded; rejection reaches the intended mutation transaction.
    rejectAuditWrites(true);
    try {
      const response = await route.fetch();
      await route.fulfill({ response });
    } finally {
      rejectAuditWrites(false);
    }
  });
  browserHealth.expectConsoleError({
    text: /status of 503/,
    url: /\/api\/ui\/profile\/recovery-codes$/,
  });
  await page.getByRole('button', { name: 'Generate codes', exact: true }).click();
  await profile.confirm();
  lastStep = await profile.verify(password, secret, lastStep);
  await expect(page.getByRole('alert')).toHaveText(
    'The audit trail is unavailable, so the vault refused the request.',
  );
  await expect(profile.codes).toHaveCount(0);
  expect(profileCredentialGeneration()).toBe(generation);
  expect(commits).toEqual(['POST']);
});

test('sign-out revocation stands and its audit failure warning reaches Legacy sign-in', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await profile.waitForNextThrottleWindow();
  const copied = await browserHealth.newContext();
  await copied.addCookies(ownerCookies);
  browserHealth.expectConsoleError({
    text: /status of 503/,
    url: /\/api\/ui\/profile\/sign-out-everywhere$/,
  });
  rejectAuditWrites(true);
  try {
    await page.getByRole('button', { name: 'Sign out everywhere', exact: true }).click();
    await profile.confirm();
    await expect(page).toHaveURL(/\/Account\/Login$/);
    await expect(page.getByText(/couldn't record it in the audit trail/)).toBeVisible();
  } finally {
    rejectAuditWrites(false);
  }
  expect((await copied.request.get('/api/ui/me')).status()).toBe(401);
  await new AccountSignIn(page).submitPassword('e2e-profile-owner', password);
  await page.getByLabel('Authenticator code').fill(await freshTotp(secret, lastStep));
  lastStep = currentTotpStep();
  await page.getByRole('button', { name: 'Verify and sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/Secrets$/);
  ownerCookies = await page.context().cookies();
});

test('a real session-store outage clears only this browser and preserves the incomplete warning', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await profile.waitForNextThrottleWindow();
  const copied = await browserHealth.newContext();
  await copied.addCookies(ownerCookies);
  await page.route('**/local-sign-out', async (route) => {
    const response = await route.fetch();
    if (route.request().method() === 'POST') pauseSessionStore(false);
    await route.fulfill({ response });
  });
  browserHealth.expectConsoleError({
    text: /status of 500/,
    url: /\/api\/ui\/profile\/sign-out-everywhere$/,
  });
  browserHealth.expectConsoleError({ text: /status of 503/, url: /\/local-sign-out$/ });
  pauseSessionStore(true);
  try {
    await page.getByRole('button', { name: 'Sign out everywhere', exact: true }).click();
    await profile.confirm();
    await expect(
      page.getByRole('button', { name: "Clear this browser's cookie and sign in", exact: true }),
    ).toBeVisible();
    await page
      .getByRole('button', { name: "Clear this browser's cookie and sign in", exact: true })
      .click();
    await expect(page).toHaveURL(/\/Account\/Login\?signOut=sign_out_incomplete$/);
    await expect(page.getByText(/Other devices may still be signed in/)).toBeVisible();
    expect(
      (await page.context().cookies()).some((cookie) => cookie.name === 'EclipsVault.Session'),
    ).toBe(false);
  } finally {
    pauseSessionStore(false);
  }
  expect((await copied.request.get('/api/ui/me')).status()).toBe(200);
  await new AccountSignIn(page).submitPassword('e2e-profile-owner', password);
  await page.getByLabel('Authenticator code').fill(await freshTotp(secret, lastStep));
  lastStep = currentTotpStep();
  await page.getByRole('button', { name: 'Verify and sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/Secrets$/);
  ownerCookies = await page.context().cookies();
});

test('a committed password with failed session renewal has a truthful sign-in-required outcome', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(300_000);
  await page.context().addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await profile.waitForNextThrottleWindow();
  const copied = await browserHealth.newContext();
  await copied.addCookies(ownerCookies);
  const generation = profileCredentialGeneration();
  const replacement = `E2e-committed-${randomBytes(24).toString('hex')}`;
  const commits: string[] = [];
  await page.route('**/api/ui/profile/password', async (route) => {
    commits.push(route.request().method());
    // The real completion notice holds execution after the database commit and before renewal.
    pauseSmtpTransport(true);
    const reply = route.fetch();
    try {
      await expect.poll(profileCredentialGeneration).toBe(generation + 1);
      pauseSessionStore(true);
      pauseSmtpTransport(false);
      const response = await reply;
      await route.fulfill({ response });
    } finally {
      pauseSmtpTransport(false);
      pauseSessionStore(false);
      await reply;
    }
  });
  browserHealth.expectConsoleError({ text: /status of 503/, url: /\/api\/ui\/profile\/password$/ });
  await new ChangePasswordPage(page).changeFromProfile(password, replacement);
  lastStep = await profile.verify(password, secret, lastStep);
  await expect(page.getByRole('alert')).toHaveText(
    'The change was saved, but this browser could not stay signed in. Sign in again to continue.',
  );
  await expect(page.getByRole('link', { name: 'Sign in again', exact: true })).toBeVisible();
  expect(commits).toEqual(['POST']);
  expect((await copied.request.get('/api/ui/me')).status()).toBe(401);
  const other = await (await browserHealth.newContext()).newPage();
  const signIn = new AccountSignIn(other);
  await signIn.open();
  await signIn.submitPassword('e2e-profile-owner', replacement);
  await other.getByLabel('Authenticator code').fill(await freshTotp(secret, lastStep));
  lastStep = currentTotpStep();
  await other.getByRole('button', { name: 'Verify and sign in', exact: true }).click();
  await expect(other).toHaveURL(/\/Secrets$/);
  password = replacement;
  ownerCookies = await other.context().cookies();
});

// Last, because the lost reply leaves this browser without a session it can trust.
test('a lost reply after a committed password change is reported as unknown and never replayed', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(300_000);
  await page.context().addCookies(ownerCookies);
  const copied = await browserHealth.newContext();
  await copied.addCookies(ownerCookies);
  const profile = new ProfilePage(page);
  await profile.open();
  await profile.waitForNextThrottleWindow();
  const commits: string[] = [];
  // The request reaches the real server and commits; only its reply is lost on the way back.
  await page.route('**/api/ui/profile/password', async (route) => {
    commits.push(route.request().method());
    await route.fetch();
    await route.abort('failed');
  });
  browserHealth.expectConsoleError({ text: /ERR_FAILED/, url: /\/api\/ui\/profile\/password$/ });
  const replacement = `E2e-lost-${randomBytes(24).toString('hex')}`;
  await new ChangePasswordPage(page).changeFromProfile(password, replacement);
  lastStep = await profile.verify(password, secret, lastStep);
  await expect(page.getByRole('alert')).toHaveText(
    'The response was lost. The change may have been saved. Check your account or sign in before deliberately trying again.',
  );
  await expect(page.getByRole('link', { name: 'Sign in again', exact: true })).toBeVisible();
  const form = new ChangePasswordPage(page);
  await expect(form.submit).toBeDisabled();
  await expect(form.newPassword).toHaveValue('');
  expect(commits).toEqual(['POST']);
  expect((await copied.request.get('/api/ui/me')).status()).toBe(401);

  const other = await (await browserHealth.newContext()).newPage();
  const signIn = new AccountSignIn(other);
  await signIn.open();
  await signIn.submitPassword('e2e-profile-owner', replacement);
  await expect(other.getByLabel('Authenticator code')).toBeVisible();
  expect(commits).toEqual(['POST']);
});
