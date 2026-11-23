import { randomBytes } from 'node:crypto';
import { test as base, expect, type BrowserHealth } from './fixtures';
import { signInNewAccount } from './account-activation';
import { AccountCreation, emailOf } from './account-creation';
import { savedAdministratorCookies } from './administrator-cookies';
import { refreshAdministratorProof } from './administrator-proof';
import { Notifications } from './notifications';
import { ProfilePage } from './profile';
import { AppShell } from './app-shell';
import { YourData } from './data';
import { SecretsList } from './secrets-list';
import { serverTotp } from './totp';

// The invitation goes straight to the mail server, so the notice is the recipient's only outbox row.
// Delivered once per worker, by whichever case needs it first.
let delivered: Promise<string> | undefined;

const test = base.extend<{ recipient: string }>({
  recipient: async ({ browserHealth }, use) => {
    delivered ??= deliverSecurityNotice(browserHealth);
    await use(await delivered);
  },
});

/** Invites an account, activates it and resets its authenticator; returns the notice's address. */
async function deliverSecurityNotice(browserHealth: BrowserHealth): Promise<string> {
  const username = `e2e-outbox-${randomBytes(4).toString('hex')}`;
  const password = `E2e-${randomBytes(24).toString('hex')}`;
  const administrator = await (await browserHealth.newContext()).newPage();
  await administrator.context().addCookies(await savedAdministratorCookies());
  await refreshAdministratorProof(administrator);
  const creation = new AccountCreation(administrator);
  await creation.open();
  await creation.create({
    firstName: 'Outbox',
    lastName: 'Recipient',
    username,
    clearance: 'Standard',
    project: 'GLOBAL',
  });
  await expect(creation.status).toContainText(`Invitation sent to ${emailOf(username)}.`);

  const owner = await (await browserHealth.newContext()).newPage();
  const { secret, step } = await signInNewAccount(owner, username, password);
  const profile = new ProfilePage(owner);
  await profile.open();
  await profile.resetAuthenticator.click();
  await profile.confirm();
  await profile.verificationPassword.fill(password);
  await profile.verificationCode.fill((await serverTotp(owner.request, secret, step)).code);
  await profile.verifyAccount.click();
  await expect(owner).toHaveURL(/\/Account\/Login$/);
  return emailOf(username);
}

test('the outbox shows the security notice this run delivered', async ({ page, recipient }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const notifications = new Notifications(page);
  await notifications.open();
  await expect(notifications.noticeParts).toHaveText([
    'Delivering via SMTP to smtp:1025.',
    'Composed emails are sent to your mail server and recorded below.',
  ]);
  await expect(notifications.cells(recipient)).toHaveText([
    /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/,
    recipient,
    'Your EclipsVault security settings changed',
    'ResetAuthenticator',
    'Smtp',
    'sent',
  ]);
  await expect(
    notifications.subject(recipient, 'Your EclipsVault security settings changed'),
  ).toHaveAttribute(
    'title',
    "A sensitive change was completed on your EclipsVault account. If this wasn't you, contact an administrator immediately.",
  );
});

test('the Angular sidebar opens Notifications', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const notifications = new Notifications(page);
  await notifications.fromSidebar();
  await expect(notifications.heading).toBeVisible();
  await expect(page).toHaveURL(/\/Notifications$/);
});

test('the palette in the Angular shell opens Notifications', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const notifications = new Notifications(page);
  await notifications.fromPalette();
  await expect(notifications.heading).toBeVisible();
});

test('the sidebar on Notifications returns to the Secrets list', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const notifications = new Notifications(page);
  await notifications.open();
  await expect(notifications.heading).toBeVisible();
  await new AppShell(page).secrets.click();
  await expect(new SecretsList(page).heading).toBeVisible();
  await expect(page).toHaveURL(/\/Secrets$/);
});

test('the sidebar on Notifications opens Your data in the Angular UI', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const notifications = new Notifications(page);
  await notifications.open();
  await expect(notifications.heading).toBeVisible();
  // Your data is in the You group, which an administration page leaves folded.
  const shell = new AppShell(page);
  await shell.you.click();
  await shell.yourData.click();
  await expect(new YourData(page).heading).toBeVisible();
  await expect(page).toHaveURL(/\/Data$/);
});

for (const width of [300, 2000]) {
  test(`Notifications does not scroll sideways at ${width}px`, async ({ page, recipient }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.context().addCookies(await savedAdministratorCookies());
    const notifications = new Notifications(page);
    await notifications.open();
    await expect(notifications.row(recipient)).toBeVisible();
    await expect(notifications.deliveryRecordNote).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(width);
  });
}

for (const theme of ['dark', 'light'] as const) {
  test(`the outbox filters by keyboard in ${theme} appearance`, async ({ page, recipient }) => {
    await page.context().addCookies(await savedAdministratorCookies());
    const notifications = new Notifications(page);
    await notifications.open();
    await notifications.useTheme(theme);
    await notifications.filter.focus();
    await page.keyboard.type(recipient);
    await expect(notifications.rows).toHaveCount(1);
    await expect(notifications.row(recipient)).toBeVisible();
    await page.keyboard.type('-no-such-recipient');
    await expect(notifications.rows).toHaveCount(0);
  });
}
