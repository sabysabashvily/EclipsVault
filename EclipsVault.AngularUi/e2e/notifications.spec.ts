import { randomBytes } from 'node:crypto';
import { test, expect } from './fixtures';
import { AccountCreation } from './account-creation';
import { savedAdministratorCookies } from './administrator-cookies';
import { refreshAdministratorProof } from './administrator-proof';
import { Notifications } from './notifications';
import { provisioningEmail } from './smtp-capture';

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
    '✓ Delivering via SMTP → smtp:1025.',
    'Composed emails are sent to your mail server and recorded below.',
  ]);
  const row = notifications.row(delivered.address);
  await expect(row.getByRole('cell')).toHaveText([
    /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/,
    delivered.address,
    delivered.subject,
    'UserProvisioned',
    'Smtp',
    'Sent',
  ]);
  await expect(row.getByRole('cell', { name: delivered.subject })).toHaveAttribute(
    'title',
    /username 'e2e-outbox-recipient'/,
  );
});

test('the Legacy sidebar opens Notifications', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const notifications = new Notifications(page);
  await notifications.fromSidebar('/Secrets');
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
  await notifications.fromPalette('/Secrets');
  await expect(notifications.heading).toBeVisible();
});

test('the palette in the Angular shell opens Notifications', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const notifications = new Notifications(page);
  await notifications.fromPalette('/Access');
  await expect(notifications.heading).toBeVisible();
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
    await expect(notifications.row(delivered.address)).toBeVisible();
    await page.screenshot({ path: info.outputPath(`notifications-${theme}.png`), fullPage: true });
  });

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
