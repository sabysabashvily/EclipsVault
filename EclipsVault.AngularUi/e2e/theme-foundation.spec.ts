import { test, expect } from './fixtures';
import { ResponsiveShell } from './responsive-shell';

test.use({
  storageState: `${process.env['E2E_TEST_RESULTS'] || 'test-results'}/sessions-administrator.json`,
});

const migratedPages = [
  ['/AccessRequests', 'Access requests'],
  ['/Secrets/SharedWithMe', 'Shared with me'],
  ['/Secrets/SharedByMe', 'Shared by me'],
  ['/Security', 'Security checkup'],
  ['/Encryption', 'Encryption keys'],
  ['/Admin/Users', 'Users'],
  ['/Admin/CreateUser', 'New user'],
  ['/SignIns', 'Sign-in history'],
  ['/Sessions', 'Signed-in devices'],
  ['/ServiceAccounts', 'Service accounts'],
  ['/ServiceAccounts/Create', 'New service account'],
  ['/Admin/Networks', 'Networks'],
  ['/Profile', 'Profile'],
  ['/Profile/ChangePassword', 'Change password'],
  ['/License', 'License'],
  ['/Notifications', 'Notifications'],
  ['/Activity', 'My activity'],
  ['/Data', 'Your data'],
  ['/Audit', 'Audit log'],
];

for (const theme of ['dark', 'light']) {
  for (const width of [300, 2000]) {
    for (const [route, title] of migratedPages) {
      test(`${title} fits ${width}px in ${theme} appearance`, async ({ page }) => {
        await page.setViewportSize({ width, height: 900 });
        await page
          .context()
          .addCookies([
            { name: 'EclipsVault.Theme', value: theme, url: process.env['E2E_BASE_URL']! },
          ]);
        await page.goto(route);
        await expect(
          page.getByRole('heading', { name: title, exact: true, level: 1 }),
        ).toBeVisible();
        // The heading renders before the data: measure only once the page has loaded its tables.
        await expect(page.getByRole('status').filter({ hasText: /^Loading/ })).toHaveCount(0);
        await expect
          .poll(() => page.evaluate(() => document.documentElement.scrollWidth))
          .toBe(width);
      });
    }
  }
}

for (const theme of ['dark', 'light']) {
  for (const width of [300, 375, 768, 1280, 2000]) {
    test(`My access fits ${width}px in ${theme} appearance`, async ({ page }, info) => {
      await page.setViewportSize({ width, height: 900 });
      await page
        .context()
        .addCookies([
          { name: 'EclipsVault.Theme', value: theme, url: process.env['E2E_BASE_URL']! },
        ]);
      await page.goto('/Access');
      await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
      await expect(page.getByRole('rowheader', { name: 'Top Secret', exact: true })).toBeVisible();
      await expect
        .poll(() => page.evaluate(() => document.documentElement.scrollWidth))
        .toBe(width);
      await page.evaluate(() => document.fonts.ready);
      const path = info.outputPath(`my-access-${theme}-${width}.png`);
      await page.screenshot({ path, fullPage: true });
      await info.attach(`my-access-${theme}-${width}`, { path, contentType: 'image/png' });
    });
  }
}

test('mobile navigation traps focus and Escape returns to its opener', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 900 });
  await page.goto('/Access');
  const shell = new ResponsiveShell(page);

  await shell.open.click();
  await expect(shell.navigation).toBeVisible();
  await expect(shell.open).toHaveAttribute('aria-expanded', 'true');
  await expect(shell.close).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect
    .poll(() =>
      page.evaluate(() => document.activeElement?.closest('dialog')?.getAttribute('aria-label')),
    )
    .toBe('Navigation');
  await page.keyboard.press('Tab');
  await expect(shell.close).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(shell.navigation).not.toBeVisible();
  await expect(shell.open).toBeFocused();
  await expect(shell.open).toHaveAttribute('aria-expanded', 'false');
});

test('search opened from mobile navigation receives focus above the page', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 900 });
  await page.goto('/Access');
  const shell = new ResponsiveShell(page);

  await shell.open.click();
  await shell.search.click();
  await expect(shell.navigation).not.toBeVisible();
  await expect(shell.palette).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(shell.open).toBeFocused();
});

test('the search shortcut leaves mobile navigation before opening the palette', async ({
  page,
}) => {
  await page.setViewportSize({ width: 375, height: 900 });
  await page.goto('/Access');
  const shell = new ResponsiveShell(page);

  await shell.open.click();
  await page.keyboard.press('Control+k');
  await expect(shell.navigation).not.toBeVisible();
  await expect(shell.palette).toBeFocused();
});

test('choosing a mobile navigation link closes the drawer and opens the page', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 900 });
  await page.goto('/Access');
  const shell = new ResponsiveShell(page);

  await shell.open.click();
  await shell.navigation.getByText('Security', { exact: true }).click();
  await shell.navigation.getByRole('link', { name: 'Sign-in history', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Sign-in history', exact: true })).toBeVisible();
  await expect(shell.navigation).not.toBeVisible();
});

test('resizing an open mobile drawer restores desktop navigation', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 900 });
  await page.goto('/Access');
  const shell = new ResponsiveShell(page);

  await shell.open.click();
  await page.setViewportSize({ width: 1280, height: 900 });
  await expect(shell.navigation).toHaveCount(0);
  await expect(shell.open).toHaveCount(0);
  await page.getByRole('link', { name: 'My access', exact: true }).click();
  await expect(page).toHaveURL(/\/Access$/);
});
