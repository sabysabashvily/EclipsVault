import { randomBytes } from 'node:crypto';
import { test, expect } from './fixtures';
import { signInNewAccount } from './account-activation';
import { AccountCreation, emailOf } from './account-creation';
import { savedAdministratorCookies } from './administrator-cookies';
import { refreshAdministratorProof } from './administrator-proof';
import { Notifications } from './notifications';
import { ProfilePage } from './profile';
import { AppShell } from './app-shell';
import { YourData } from './data';
import { SecretsList } from './secrets-list';

test.describe.configure({ mode: 'serial' });
const recipient = emailOf('e2e-outbox-recipient');

// The invitation goes straight to the mail server, so the notice is the recipient's only outbox row.
test('the outbox shows the security notice this run delivered', async ({ page, browserHealth }) => {
  test.setTimeout(180_000);
  const password = `E2e-${randomBytes(24).toString('hex')}`;
  await page.context().addCookies(await savedAdministratorCookies());
  await refreshAdministratorProof(page);
  const creation = new AccountCreation(page);
  await creation.open();
  await creation.create({
    firstName: 'Outbox',
    lastName: 'Recipient',
    username: 'e2e-outbox-recipient',
    clearance: 'Standard',
    project: 'GLOBAL',
  });
  await expect(creation.status).toContainText('Invitation sent to');
  const owner = await (await browserHealth.newContext()).newPage();
  const { secret, step } = await signInNewAccount(owner, 'e2e-outbox-recipient', password);
  const profile = new ProfilePage(owner);
  await profile.open();
  await profile.resetAuthenticator.click();
  await profile.confirm();
  await profile.verify(password, secret, step);
  await expect(owner).toHaveURL(/\/Account\/Login$/);

  const notifications = new Notifications(page);
  await notifications.open();
  await expect(notifications.noticeParts).toHaveText([
    '✓ Delivering via SMTP → smtp:1025.',
    'Composed emails are sent to your mail server and recorded below.',
  ]);
  await expect(notifications.cells(recipient)).toHaveText([
    /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/,
    recipient,
    'Your EclipsVault security settings changed',
    'ResetAuthenticator',
    'Smtp',
    'Sent',
  ]);
  await expect(
    notifications.subject(recipient, 'Your EclipsVault security settings changed'),
  ).toHaveAttribute(
    'title',
    "A sensitive change was completed on your EclipsVault account. If this wasn't you, contact an administrator immediately.",
  );
});

test('the Legacy sidebar opens Notifications', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const notifications = new Notifications(page);
  await notifications.fromSidebar('/DynamicSecrets');
  await expect(notifications.heading).toBeVisible();
  await expect(page).toHaveURL(/\/Notifications$/);
});

test('the Angular sidebar opens Notifications', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const notifications = new Notifications(page);
  await notifications.fromSidebar('/Access');
  await expect(notifications.heading).toBeVisible();
  await expect(page).toHaveURL(/\/Notifications$/);
});

test('the palette in the Legacy shell opens Notifications', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const notifications = new Notifications(page);
  await notifications.fromPalette('/DynamicSecrets');
  await expect(notifications.heading).toBeVisible();
});

test('the palette in the Angular shell opens Notifications', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const notifications = new Notifications(page);
  await notifications.fromPalette('/Access');
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

for (const theme of ['dark', 'light'] as const) {
  test(`visual parity: Notifications in ${theme} appearance`, async ({ page }, info) => {
    await page.context().addCookies(await savedAdministratorCookies());
    const notifications = new Notifications(page);
    await notifications.open();
    await notifications.useTheme(theme);
    // eslint-disable-next-line playwright/no-raw-locators -- The theme is an attribute of the document element.
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
    await expect(notifications.notice).toHaveClass(/notice-ok/);
    await expect(notifications.row(recipient)).toBeVisible();
    await page.screenshot({ path: info.outputPath(`notifications-${theme}.png`), fullPage: true });
  });

  test(`the outbox filters by keyboard in ${theme} appearance`, async ({ page }) => {
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
