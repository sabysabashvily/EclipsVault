import { savedAdministratorCookies } from './administrator-cookies';
import { test, expect } from './fixtures';
import { NotFoundPage } from './not-found';
import { Overview } from './overview';

test('an unknown address shows the not-found page, and Back to safety opens the Overview', async ({
  page,
  browserHealth,
}) => {
  browserHealth.expectConsoleError({ text: /status of 404/, url: /\/e2e-no-such-page$/ });
  await page.context().addCookies(await savedAdministratorCookies());
  const notFound = new NotFoundPage(page);

  const response = await notFound.open('/e2e-no-such-page');

  expect(response?.status()).toBe(404);
  await expect(page).toHaveTitle('Page not found — EclipsVault');
  await expect(notFound.heading).toBeVisible();
  await expect(notFound.explanation).toBeVisible();
  await notFound.backToSafety.click();
  await expect(page).toHaveURL(/\/$/);
  await expect(new Overview(page).heading).toBeVisible();
});

test('a missing file answers with an empty 404', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());

  const response = await page.request.get('/ui/e2e-missing.js');

  expect(response.status()).toBe(404);
  expect(await response.text()).toBe('');
});

test('switching to dark in the vault carries the theme to the not-found page', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  await page.goto('/Access');
  await page.context().addCookies([
    {
      name: 'EclipsVault.Theme',
      value: 'light',
      url: new URL('/', page.url()).href,
      secure: true,
    },
  ]);
  await page.reload();
  await page.getByTitle('Your account — Top Secret clearance', { exact: true }).click();
  await page.getByRole('menuitem', { name: 'Switch to dark', exact: true }).click();
  const currentTheme = () => page.evaluate(() => document.documentElement.dataset['theme']);
  await expect.poll(currentTheme).toBe('dark');

  browserHealth.expectConsoleError({ text: /status of 404/, url: /\/e2e-theme-no-such-page$/ });
  const notFound = new NotFoundPage(page);
  await notFound.open('/e2e-theme-no-such-page');

  await expect(notFound.heading).toBeVisible();
  await expect.poll(currentTheme).toBe('dark');
});
