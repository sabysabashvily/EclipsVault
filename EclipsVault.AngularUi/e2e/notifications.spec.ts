import { randomBytes } from 'node:crypto';
import { test, expect } from './fixtures';
import { expectLegacyShell, legacyShell } from './legacy-shell';
import { AccountCreation } from './account-creation';
import { savedAdministratorCookies } from './administrator-cookies';
import { refreshAdministratorProof } from './administrator-proof';
import { Notifications } from './notifications';
import { provisioningEmail } from './smtp-capture';
import { AppShell } from './app-shell';
import { YourData } from './data';
import { SecretsList } from './secrets-list';

test.describe.configure({ mode: 'serial' });
let delivered = { address: '', subject: '' };

test('the outbox shows the provisioning email this run delivered', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  await refreshAdministratorProof(page);
  const creation = new AccountCreation(page);
  await creation.open();
  await creation.create({
    firstName: 'Outbox',
    lastName: 'Recipient',
    username: 'e2e-outbox-recipient',
    password: `E2e-${randomBytes(24).toString('hex')}`,
    clearance: 'Standard',
    project: 'GLOBAL',
  });
  await expect(page).toHaveURL(/\/Admin\/Users$/);
  delivered = await provisioningEmail('e2e-outbox-recipient');

  const notifications = new Notifications(page);
  await notifications.open();
  await expect(notifications.noticeParts).toHaveText([
    'Delivering via SMTP to smtp:1025.',
    'Composed emails are sent to your mail server and recorded below.',
  ]);
  await expect(notifications.cells(delivered.address)).toHaveText([
    /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/,
    delivered.address,
    delivered.subject,
    'UserProvisioned',
    'Smtp',
    'sent',
  ]);
  await expect(notifications.subject(delivered.address, delivered.subject)).toHaveAttribute(
    'title',
    /username 'e2e-outbox-recipient'/,
  );
});

test('the Legacy sidebar opens Notifications', async ({ page, browserHealth }) => {
  expectLegacyShell(browserHealth);
  await page.context().addCookies(await savedAdministratorCookies());
  const notifications = new Notifications(page);
  await notifications.fromSidebar(legacyShell);
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

test('the palette in the Legacy shell opens Notifications', async ({ page, browserHealth }) => {
  expectLegacyShell(browserHealth);
  await page.context().addCookies(await savedAdministratorCookies());
  const notifications = new Notifications(page);
  await notifications.fromPalette(legacyShell);
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

for (const width of [300, 2000]) {
  test(`Notifications does not scroll sideways at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.context().addCookies(await savedAdministratorCookies());
    const notifications = new Notifications(page);
    await notifications.open();
    await expect(notifications.row(delivered.address)).toBeVisible();
    await expect(page.getByText('a delivery record, not a mailbox')).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(width);
  });
}

for (const theme of ['dark', 'light'] as const) {
  test(`the outbox filters by keyboard in ${theme} appearance`, async ({ page }) => {
    await page.context().addCookies(await savedAdministratorCookies());
    const notifications = new Notifications(page);
    await notifications.open();
    await notifications.useTheme(theme);
    await notifications.filter.focus();
    await page.keyboard.type(delivered.address);
    await expect(notifications.rows).toHaveCount(1);
    await expect(notifications.row(delivered.address)).toBeVisible();
    await page.keyboard.type('-no-such-recipient');
    await expect(notifications.rows).toHaveCount(0);
  });
}
