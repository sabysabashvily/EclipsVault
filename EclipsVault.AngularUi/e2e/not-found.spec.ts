import { savedAdministratorCookies } from './administrator-cookies';
import { test, expect } from './fixtures';
import { Overview } from './overview';

test('an unknown address shows the not-found page, and Back to safety opens the Overview', async ({
  page,
  browserHealth,
}) => {
  browserHealth.expectConsoleError({ text: /status of 404/, url: /\/e2e-no-such-page$/ });
  await page.context().addCookies(await savedAdministratorCookies());

  const response = await page.goto('/e2e-no-such-page');

  expect(response?.status()).toBe(404);
  await expect(page).toHaveTitle('Page not found — EclipsVault');
  await expect(
    page.getByRole('heading', { name: '404 — Page not found', exact: true }),
  ).toBeVisible();
  await expect(page.getByText('There is no page at this address.', { exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Back to safety', exact: true }).click();
  await expect(page).toHaveURL(/\/$/);
  await expect(new Overview(page).heading).toBeVisible();
});

test('a missing file answers 404, not the Angular document', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());

  const response = await page.request.get('/ui/e2e-missing.js');

  expect(response.status()).toBe(404);
  expect(await response.text()).not.toContain('<app-root');
});

test('signed out, the Overview address asks for sign-in and names itself as the return', async ({
  browserHealth,
}) => {
  const page = await (await browserHealth.newContext()).newPage();

  await page.goto('/');

  await expect(page).toHaveURL(/\/Account\/Login\?ReturnUrl=%2F$/);
  await expect(page.getByLabel('Username')).toBeVisible();
});
