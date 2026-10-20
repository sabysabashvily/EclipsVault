import type { BrowserContext, Page } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { test, expect } from './fixtures';
import { type YourDataSummary, YourData } from './data';
import { openPalette } from './palette';

test.describe.configure({ mode: 'serial' });

const summary: YourDataSummary = {
  account: {
    username: 'e2e-your-data',
    displayName: 'Your Data',
    email: 'your.data.1@vault.local',
    clearance: 'Elevated',
    projectKey: 'GLOBAL',
    hasCustomAvatar: true,
  },
  twoStepEnabled: false,
  passkeyCount: 2,
  backupCodesRemaining: 7,
  deviceCount: 3,
  accessRequestCount: 1,
  activityCount: 42,
  notice:
    'This file contains the account and security metadata EclipsVault holds about you. It never includes secret values, passwords, authenticator seeds, or backup codes — those are encrypted and access-controlled and are never exported.',
};

/** Opens a page as the administrator the sign-in suite enrolled, on its disposable session. */
async function openAsAdministrator(page: Page, path = '/Data'): Promise<YourData> {
  const state = JSON.parse(await readFile('test-results/sessions-administrator.json', 'utf8')) as {
    cookies: Awaited<ReturnType<BrowserContext['cookies']>>;
  };
  await page.context().addCookies(state.cookies);
  await page.goto(path);
  return new YourData(page);
}

for (const theme of ['dark', 'light']) {
  test(`visual parity: Your data matches the Legacy page's text and styles in ${theme}`, async ({
    page,
  }, info) => {
    await page.setViewportSize({ width: 1440, height: 1200 });
    await page
      .context()
      .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: process.env['E2E_BASE_URL']! }]);
    // Only the conventional MVC route still reaches the Legacy page, which this page replaces.
    const legacy = await openAsAdministrator(page, '/Data/Index/legacy');
    await expect(legacy.downloadCopy).toBeVisible();
    await expect(page).toHaveTitle('Your data — EclipsVault');
    const reference = await legacy.presentation();

    const response = await page.goto('/Data');
    const angular = new YourData(page);
    await expect(angular.downloadCopy).toBeVisible();

    expect(response?.headers()['content-security-policy']).toContain(
      "require-trusted-types-for 'script'",
    );
    await expect(page).toHaveTitle('Your data — EclipsVault');
    expect(await angular.presentation()).toEqual(reference);
    const path = info.outputPath(`${theme}-your-data.png`);
    await page.screenshot({ path, fullPage: true });
    await info.attach(`${theme}-your-data`, { path, contentType: 'image/png' });
  });
}

test("Your data shows the account, its six What's included cards, the notice and both downloads", async ({
  page,
}) => {
  await page.route('**/api/ui/data', (route) => route.fulfill({ json: summary }));
  const yourData = await openAsAdministrator(page);

  await expect(yourData.heading).toBeVisible();
  await expect(yourData.introduction).toHaveText(
    "Everything EclipsVault holds about your account, in one place. Download a portable copy for your records — or just to see what's stored. Only your data is included.",
  );
  await expect(yourData.accountLabels).toHaveText([
    'Username',
    'Display name',
    'Email',
    'Clearance',
    'Project',
    'Profile picture',
  ]);
  await expect(yourData.accountValues).toHaveText([
    'e2e-your-data',
    'Your Data',
    'your.data.1@vault.local',
    'Elevated',
    'GLOBAL',
    'Custom image',
  ]);
  expect(await yourData.cards()).toEqual([
    ['Off', 'Two-step sign-in'],
    ['2', 'Passkeys'],
    ['7', 'Backup codes left'],
    ['3', 'Signed-in devices'],
    ['1', 'Access requests filed'],
    ['42', 'Recent activity entries'],
  ]);
  await expect(yourData.notice).toHaveText(summary.notice);
  await expect(yourData.download).toBeVisible();
  await expect(yourData.downloadCopy).toBeVisible();
});

for (const [label, button] of [
  ['Download (JSON)', 'download'],
  ['Download a copy of my data (JSON)', 'downloadCopy'],
] as const) {
  test(`${label} downloads the signed-in account's own file under its dated name`, async ({
    page,
  }) => {
    const yourData = await openAsAdministrator(page);
    await expect(yourData.accountValues.first()).toHaveText('vault-admin');
    const exported = page.waitForRequest('**/api/ui/data/export');
    const downloaded = page.waitForEvent('download');

    await yourData[button].click();

    expect((await exported).headers()['requestverificationtoken']).toBeTruthy();
    const file = await downloaded;
    expect(file.suggestedFilename()).toMatch(/^eclipsvault-data-vault-admin-\d{8}\.json$/);
    const body = JSON.parse(await readFile((await file.path())!, 'utf8')) as {
      SchemaVersion: string;
      Account: { Username: string };
    };
    expect(body.SchemaVersion).toBe('1.0');
    expect(body.Account.Username).toBe('vault-admin');
    await expect(yourData.error).toBeEmpty();
  });
}

test('a refused export shows why and downloads nothing', async ({ page, browserHealth }) => {
  const refusal = 'The audit trail is unavailable, so the vault refused the request.';
  await page.route('**/api/ui/data/export', (route) =>
    route.fulfill({
      status: 503,
      contentType: 'application/problem+json',
      json: { status: 503, code: 'audit_unavailable', title: refusal },
    }),
  );
  browserHealth.expectConsoleError({ text: /status of 503/, url: /\/api\/ui\/data\/export$/ });
  const downloads: string[] = [];
  page.on('download', (download) => downloads.push(download.suggestedFilename()));
  const yourData = await openAsAdministrator(page);

  await yourData.download.click();

  await expect(yourData.error).toHaveText(refusal);
  await expect(yourData.download).toBeEnabled();
  expect(downloads).toEqual([]);
});

test('Your data reports a load failure and retries in place', async ({ page, browserHealth }) => {
  browserHealth.expectConsoleError({ text: /status of 503/, url: /\/api\/ui\/data$/ });
  await page.route(
    '**/api/ui/data',
    (route) => route.fulfill({ status: 503, json: { code: 'internal_error' } }),
    { times: 1 },
  );
  const yourData = await openAsAdministrator(page);
  await expect(
    yourData.error.filter({ hasText: 'Could not load your data. Try again.' }),
  ).toBeVisible();
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(request.url());
  });

  await yourData.retry.click();

  await expect(yourData.accountValues.first()).toHaveText('vault-admin');
  await expect(yourData.error).toBeEmpty();
  expect(documents).toEqual([]);
});

test('the sidebar and palette open Your data inside Angular and keep mixed-case bookmarks', async ({
  page,
}) => {
  await openAsAdministrator(page, '/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(request.url());
  });
  const link = page.getByRole('link', { name: 'Your data', exact: true });

  await link.click();
  await expect(new YourData(page).heading).toBeVisible();
  await expect(page).toHaveURL(/\/Data$/);
  await expect(page).toHaveTitle('Your data — EclipsVault');
  await expect(link).toHaveAttribute('aria-current', 'page');
  await page.getByText('Access', { exact: true }).click();
  await page.getByRole('link', { name: 'My access', exact: true }).click();
  await openPalette(page);
  await page.getByRole('combobox').fill('your data');
  await page.keyboard.press('Enter');
  await expect(new YourData(page).heading).toBeVisible();
  await expect(page).toHaveURL(/\/Data$/);
  expect(documents).toEqual([]);

  await page.goto('/dAtA/InDeX');
  await expect(new YourData(page).heading).toBeVisible();
  await expect(page).toHaveTitle('Your data — EclipsVault');
});

test('signed out, Your data bookmarks return through sign-in', async ({ page }) => {
  await page.goto('/Data');
  await expect(page).toHaveURL(/\/Account\/Login\?ReturnUrl=%2FData$/);
  await page.goto('/Data/Index');
  await expect(page).toHaveURL(/\/Account\/Login\?ReturnUrl=%2FData%2FIndex$/);
});
