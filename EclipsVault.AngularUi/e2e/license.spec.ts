import { test, expect } from './fixtures';
import { savedAdministratorCookies } from './administrator-cookies';
import { License } from './license';

test('License shows that this vault runs unlicensed', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const license = new License(page);
  await license.open();
  await expect(license.heading).toBeVisible();
  await expect(license.status).toHaveText('Missing');
  await expect(
    license.content.getByText('No license is configured — running unlicensed.', { exact: true }),
  ).toBeVisible();
  await expect(license.details).toHaveCount(0);
});

test('the Legacy licence notice opens License', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const license = new License(page);
  await page.goto('/Secrets');
  await license.viewLicense.click();
  await expect(license.heading).toBeVisible();
  await expect(page).toHaveURL(/\/License$/);
});

test('the Angular licence notice opens License', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const license = new License(page);
  await page.goto('/Access');
  await license.viewLicense.click();
  await expect(license.heading).toBeVisible();
  await expect(page).toHaveURL(/\/License$/);
});

test('the Legacy sidebar opens License', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const license = new License(page);
  await license.fromSidebar('/Secrets');
  await expect(license.heading).toBeVisible();
  await expect(page).toHaveURL(/\/License$/);
});

test('the Angular sidebar opens License', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const license = new License(page);
  await license.fromSidebar('/Access');
  await expect(license.heading).toBeVisible();
  await expect(page).toHaveURL(/\/License$/);
});

test('the palette in the Legacy shell opens License', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const license = new License(page);
  await license.fromPalette('/Secrets');
  await expect(license.heading).toBeVisible();
});

test('the palette in the Angular shell opens License', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const license = new License(page);
  await license.fromPalette('/Access');
  await expect(license.heading).toBeVisible();
});

test('the licence notice link opens License by keyboard', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const license = new License(page);
  await page.goto('/Access');
  await license.viewLicense.focus();
  await page.keyboard.press('Enter');
  await expect(license.heading).toBeVisible();
});

for (const theme of ['dark', 'light'] as const) {
  test(`visual parity: License in ${theme} appearance`, async ({ page }, info) => {
    await page.context().addCookies(await savedAdministratorCookies());
    const license = new License(page);
    await license.open();
    await license.useTheme(theme);
    // eslint-disable-next-line playwright/no-raw-locators -- The theme is an attribute of the document element.
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
    await expect(license.status).toHaveClass(/tone-muted/);
    await page.screenshot({ path: info.outputPath(`license-${theme}.png`), fullPage: true });
  });
}
