import { test, expect } from './fixtures';
import { savedAdministratorCookies } from './administrator-cookies';
import { EncryptionKeys } from './encryption';
import { AppShell } from './app-shell';
import { YourData } from './data';
import { SecretsList } from './secrets-list';

test('Encryption keys shows every secret under the current key and nothing to rotate', async ({
  page,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.open();
  await expect(encryption.heading).toBeVisible();
  await expect(encryption.notice).toContainText('✓ All secrets are wrapped under the current KEK.');
  await expect(encryption.notice).toContainText('Nothing to rotate.');
  await expect(encryption.currentKey).toHaveText(/^kek-[0-9a-f]{8}$/);
  await expect(encryption.rotate).toBeDisabled();
});

test('Rotate asks in the Legacy words before re-wrapping anything', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.reportOnePendingSecret();
  await encryption.open();
  await encryption.rotate.click();
  await expect(encryption.confirmation).toContainText(
    "Re-wrap every secret's data key under the current KEK now? This is safe and can be repeated.",
  );
  expect(encryption.rotationsSent).toEqual([]);
});

test('cancelling the confirmation rotates nothing and returns to the Rotate button', async ({
  page,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.reportOnePendingSecret();
  await encryption.open();
  await encryption.rotate.click();
  await encryption.keepKeys.click();
  await expect(encryption.confirmation).toBeHidden();
  await expect(encryption.rotate).toBeFocused();
  expect(encryption.rotationsSent).toEqual([]);
});

test('the Legacy sidebar opens Encryption keys', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.fromSidebar('/Secrets');
  await expect(encryption.heading).toBeVisible();
  await expect(page).toHaveURL(/\/Encryption$/);
});

test('the Angular sidebar opens Encryption keys', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.fromSidebar('/Access');
  await expect(encryption.heading).toBeVisible();
  await expect(page).toHaveURL(/\/Encryption$/);
});

test('the palette in the Legacy shell opens Encryption keys', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.fromPalette('/Secrets');
  await expect(encryption.heading).toBeVisible();
});

test('the palette in the Angular shell opens Encryption keys', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.fromPalette('/Access');
  await expect(encryption.heading).toBeVisible();
});

test('the sidebar on Encryption keys returns to the Legacy Secrets list', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.open();
  await expect(encryption.heading).toBeVisible();
  await new AppShell(page).secrets.click();
  await expect(new SecretsList(page).heading).toBeVisible();
  await expect(page).toHaveURL(/\/Secrets$/);
});

test('the sidebar on Encryption keys opens Your data in the Angular UI', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const encryption = new EncryptionKeys(page);
  await encryption.open();
  await expect(encryption.heading).toBeVisible();
  await new AppShell(page).yourData.click();
  await expect(new YourData(page).heading).toBeVisible();
  await expect(page).toHaveURL(/\/Data$/);
});

for (const theme of ['dark', 'light'] as const) {
  test(`visual parity: Encryption keys in ${theme} appearance`, async ({ page }, info) => {
    await page.context().addCookies(await savedAdministratorCookies());
    const encryption = new EncryptionKeys(page);
    await encryption.open();
    await encryption.useTheme(theme);
    // eslint-disable-next-line playwright/no-raw-locators -- The theme is an attribute of the document element.
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
    await expect(encryption.notice).toHaveClass(/notice-ok/);
    await page.screenshot({ path: info.outputPath(`encryption-${theme}.png`), fullPage: true });
  });

  test(`the confirmation opens, cycles and closes by keyboard in ${theme} appearance`, async ({
    page,
  }) => {
    await page.context().addCookies(await savedAdministratorCookies());
    const encryption = new EncryptionKeys(page);
    await encryption.reportOnePendingSecret();
    await encryption.open();
    await encryption.useTheme(theme);
    await encryption.rotate.focus();
    await page.keyboard.press('Enter');
    await expect(encryption.keepKeys).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(encryption.continue).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(encryption.keepKeys).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(encryption.confirmation).toBeHidden();
    await expect(encryption.rotate).toBeFocused();
    expect(encryption.rotationsSent).toEqual([]);
  });
}
