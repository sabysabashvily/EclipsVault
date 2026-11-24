import { test, expect } from './fixtures';
import { ResponsiveShell } from './responsive-shell';
import { AccountSignIn } from './account-sign-in';

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

// Edit user needs a real account's id, so it opens from the list: the administrator always exists.
for (const theme of ['dark', 'light']) {
  for (const width of [300, 2000]) {
    test(`Edit user fits ${width}px in ${theme} appearance`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await page
        .context()
        .addCookies([
          { name: 'EclipsVault.Theme', value: theme, url: process.env['E2E_BASE_URL']! },
        ]);
      await page.goto('/Admin/Users');
      const shell = new ResponsiveShell(page);
      await shell.rowMore.click();
      await shell.editUser.click();
      await expect(
        page.getByRole('heading', { name: 'Edit user', exact: true, level: 1 }),
      ).toBeVisible();
      await expect(page.getByRole('status').filter({ hasText: /^Loading/ })).toHaveCount(0);
      await expect
        .poll(() => page.evaluate(() => document.documentElement.scrollWidth))
        .toBe(width);
    });
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

test('the search circle on a phone opens the palette and Escape returns to it', async ({
  page,
}) => {
  await page.setViewportSize({ width: 375, height: 900 });
  await page.goto('/Access');
  const shell = new ResponsiveShell(page);

  await shell.search.click();
  await expect(shell.palette).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(shell.search).toBeFocused();
});

test('the search shortcut leaves mobile navigation before opening the palette', async ({
  page,
}) => {
  await page.setViewportSize({ width: 375, height: 900 });
  await page.goto('/Access');
  const shell = new ResponsiveShell(page);

  await shell.open.click();
  await page.keyboard.press('ControlOrMeta+k');
  await expect(shell.navigation).not.toBeVisible();
  await expect(shell.palette).toBeFocused();
});

test('choosing a mobile navigation link closes the drawer and opens the page', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 900 });
  await page.goto('/Access');
  const shell = new ResponsiveShell(page);

  // My access is in the You group, so the group is already open.
  await shell.open.click();
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

for (const width of [300, 768, 1199]) {
  test(`drawer links and small actions have 44px targets at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto('/Admin/Users');
    const shell = new ResponsiveShell(page);
    await expect(shell.rowMore).toBeVisible();
    const more = await shell.rowMore.boundingBox();
    // Boxes come back in floating point: a 2.75rem control can measure 43.99997px.
    expect(Math.round(more?.height ?? 0)).toBeGreaterThanOrEqual(44);
    expect(Math.round(more?.width ?? 0)).toBeGreaterThanOrEqual(44);
    const primary = await shell.newUser.boundingBox();
    expect(Math.round(primary?.height ?? 0)).toBeGreaterThanOrEqual(44);
    expect(Math.round(primary?.width ?? 0)).toBeGreaterThanOrEqual(44);
    await shell.open.click();
    const link = await shell.usersLink.boundingBox();
    expect(Math.round(link?.height ?? 0)).toBeGreaterThanOrEqual(44);
    expect(Math.round(link?.width ?? 0)).toBeGreaterThanOrEqual(44);
    await page.keyboard.press('Escape');
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBe(width);
    await page.goto('/Profile/ChangePassword');
    await expect(shell.showPassword).toBeVisible();
    const reveal = await shell.showPassword.boundingBox();
    expect(Math.round(reveal?.height ?? 0)).toBeGreaterThanOrEqual(44);
    expect(Math.round(reveal?.width ?? 0)).toBeGreaterThanOrEqual(44);
  });
}

test('the phone primary action opens New user with its full accessible name', async ({ page }) => {
  await page.setViewportSize({ width: 300, height: 900 });
  await page.goto('/Admin/Users');
  const shell = new ResponsiveShell(page);
  await expect(shell.newUser.getByText('New user', { exact: true })).toBeHidden();
  await shell.newUser.click();
  await expect(page.getByRole('heading', { name: 'New user', exact: true })).toBeVisible();
  await expect(shell.newUser).toHaveCount(0);
});

test('the tablet primary action keeps its visible label', async ({ page }) => {
  await page.setViewportSize({ width: 768, height: 900 });
  await page.goto('/Admin/Users');
  const shell = new ResponsiveShell(page);
  await expect(shell.newUser.getByText('New user', { exact: true })).toBeVisible();
});

test('an overflowing Audit table shows a scroll hint above the minimum table width', async ({
  page,
}) => {
  await page.setViewportSize({ width: 800, height: 900 });
  await page.goto('/Audit');
  const entries = page.getByRole('region', { name: 'Audit entries', exact: true });
  await expect(entries.getByRole('row').nth(1)).toBeVisible();
  expect(await entries.evaluate((region) => region.clientWidth)).toBeGreaterThan(640);
  await expect
    .poll(() => entries.evaluate((region) => region.scrollWidth > region.clientWidth))
    .toBe(true);
  await expect(
    entries.getByText('Scroll horizontally to see all columns.', { exact: true }),
  ).toBeVisible();
  await expect(entries).toHaveAttribute('tabindex', '0');

  await page.setViewportSize({ width: 2000, height: 900 });
  await page.goto('/Access');
  await expect(page.getByRole('rowheader', { name: 'Top Secret', exact: true })).toBeVisible();
  await expect(
    page.getByText('Scroll horizontally to see all columns.', { exact: true }),
  ).toBeHidden();
});

test('reduced-motion navigation can close and reopen without losing keyboard focus', async ({
  page,
}) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.setViewportSize({ width: 768, height: 900 });
  await page.goto('/Access');
  const shell = new ResponsiveShell(page);
  await shell.open.click();
  await expect(shell.close).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(shell.navigation).toBeHidden();
  await expect(shell.open).toBeFocused();
  await shell.open.click();
  await expect(shell.close).toBeFocused();
});

test.describe('signed-out sign-in', () => {
  // Clear the file-level administrator storage state so /Account/Login renders instead of redirecting.
  test.use({ storageState: { cookies: [], origins: [] } });

  test('sign-in follows the saved theme and preserves a changed theme after reload', async ({
    context,
    page,
  }) => {
    const signIn = new AccountSignIn(page);
    await signIn.open();
    await context.addCookies([
      {
        name: 'EclipsVault.Theme',
        value: 'dark',
        url: new URL('/', page.url()).href,
        secure: true,
      },
    ]);
    await page.reload();
    const theme = () => page.evaluate(() => document.documentElement.dataset['theme']);
    await expect.poll(theme).toBe('dark');

    await signIn.themeSwitch.click();

    await expect.poll(theme).toBe('light');
    expect(
      (await context.cookies()).find((cookie) => cookie.name === 'EclipsVault.Theme')?.value,
    ).toBe('light');
    await page.reload();
    await expect.poll(theme).toBe('light');
  });

  const anonymousPages = [
    ['/Account/Login', 'Sign in'],
    ['/Account/Recover', 'Recover access'],
  ];

  for (const theme of ['dark', 'light']) {
    for (const width of [300, 2000]) {
      for (const [route, title] of anonymousPages) {
        test(`${title} fits ${width}px in ${theme} appearance`, async ({ context, page }) => {
          await page.setViewportSize({ width, height: 900 });
          await context.addCookies([
            {
              name: 'EclipsVault.Theme',
              value: theme,
              url: process.env['E2E_BASE_URL']!,
              secure: true,
            },
          ]);
          const optionsLoaded =
            route === '/Account/Login'
              ? page.waitForResponse(
                  (response) => new URL(response.url()).pathname === '/api/ui/auth/options',
                )
              : null;
          await page.goto(route);
          if (optionsLoaded) await optionsLoaded;
          await expect(
            page.getByRole('heading', { name: title, exact: true, level: 1 }),
          ).toBeVisible();
          await expect
            .poll(() => page.evaluate(() => document.documentElement.scrollWidth))
            .toBe(width);
        });
      }
    }
  }

  for (const [label, provider] of [
    ['multi-word', 'North America Corporate Identity Provider Sign-in'],
    ['unbroken', 'EnterpriseSingleSignOnIdentityProviderWithoutSpacesOrWordBreaks1'],
  ]) {
    for (const width of [300, 768]) {
      test(`a long ${label} SSO provider name wraps inside its button at ${width}px`, async ({
        page,
      }) => {
        await page.setViewportSize({ width, height: 900 });
        await page.route('**/api/ui/auth/options', (route) =>
          route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({ ssoEnabled: true, ssoDisplayName: provider }),
          }),
        );
        const signIn = new AccountSignIn(page);
        await signIn.open();
        const ssoButton = signIn.singleSignOnButton(provider);
        await expect(ssoButton).toBeVisible();
        await expect
          .poll(() => page.evaluate(() => document.documentElement.scrollWidth))
          .toBe(width);
        const fits = await ssoButton.evaluate(
          (el, viewportWidth) =>
            el.scrollWidth <= el.clientWidth + 1 &&
            el.getBoundingClientRect().right <= viewportWidth,
          width,
        );
        expect(fits).toBe(true);
      });
    }
  }

  test('sign-in controls keep 44px touch targets at 300px', async ({ page }) => {
    await page.setViewportSize({ width: 300, height: 900 });
    const signIn = new AccountSignIn(page);
    await signIn.open();

    const themeSwitch = await signIn.themeSwitch.boundingBox();
    expect(Math.round(themeSwitch?.height ?? 0)).toBeGreaterThanOrEqual(44);
    expect(Math.round(themeSwitch?.width ?? 0)).toBeGreaterThanOrEqual(44);

    const reveal = await signIn.showPassword.boundingBox();
    expect(Math.round(reveal?.height ?? 0)).toBeGreaterThanOrEqual(44);
    expect(Math.round(reveal?.width ?? 0)).toBeGreaterThanOrEqual(44);

    const submit = await signIn.continueButton.boundingBox();
    expect(Math.round(submit?.height ?? 0)).toBeGreaterThanOrEqual(44);
  });
});
