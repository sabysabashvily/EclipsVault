import type { BrowserContext, Page } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { test, expect } from './fixtures';
import { AppShell } from './app-shell';
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

for (const [theme, textColor, accent] of [
  ['dark', 'rgb(237, 238, 242)', 'rgb(255, 159, 10)'],
  ['light', 'rgb(28, 29, 34)', 'rgb(180, 95, 4)'],
]) {
  test(`visual parity: Your data preserves its heading, grids and download styles in ${theme}`, async ({
    page,
  }, info) => {
    await page.setViewportSize({ width: 1440, height: 1200 });
    await page
      .context()
      .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: process.env['E2E_BASE_URL']! }]);
    const yourData = await openAsAdministrator(page);
    await expect(yourData.downloadCopy).toBeVisible();
    await expect(page).toHaveTitle('Your data — EclipsVault');
    await expect(yourData.heading).toHaveCSS('color', textColor);
    await expect(yourData.heading).toHaveCSS('font-size', '24px');
    await expect(yourData.heading).toHaveCSS('font-weight', '620');
    await expect(yourData.pageHeader).toHaveCSS('display', 'flex');
    await expect(yourData.pageHeader).toHaveCSS('justify-content', 'space-between');
    await expect(yourData.introduction).toHaveCSS('display', 'flex');
    await expect(yourData.accountGrid).toHaveCSS('display', 'grid');
    await expect(yourData.accountGrid).toHaveCSS('row-gap', '8px');
    await expect(yourData.accountGrid).toHaveCSS('column-gap', '16px');
    await expect(yourData.cardGrid).toHaveCSS('display', 'grid');
    await expect(yourData.cardGrid).toHaveCSS('gap', '16px');
    await expect(yourData.firstCard).toHaveCSS('border-radius', '12px');
    await expect(yourData.download).toHaveCSS('background-color', accent);
    await expect(yourData.downloadCopy).toHaveCSS('background-color', accent);
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

test('the sidebar opens Your data in-app with its entry active', async ({ page }) => {
  await openAsAdministrator(page, '/Access');
  const shell = new AppShell(page);
  await expect(shell.myAccess).toHaveAttribute('aria-current', 'page');
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(request.url());
  });
  await shell.yourData.click();
  await expect(new YourData(page).heading).toBeVisible();
  await expect(page).toHaveURL(/\/Data$/);
  await expect(page).toHaveTitle('Your data — EclipsVault');
  await expect(shell.yourData).toHaveAttribute('aria-current', 'page');
  expect(documents).toEqual([]);
});

test('the palette opens Your data without a document load', async ({ page }) => {
  await openAsAdministrator(page, '/Access');
  const shell = new AppShell(page);
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(request.url());
  });
  await openPalette(page);
  await shell.paletteQuery.fill('your data');
  await page.keyboard.press('Enter');
  await expect(new YourData(page).heading).toBeVisible();
  await expect(page).toHaveURL(/\/Data$/);
  expect(documents).toEqual([]);
});

test('a mixed-case Your data bookmark serves the titled Angular page', async ({ page }) => {
  await openAsAdministrator(page, '/dAtA/InDeX');
  await expect(new YourData(page).heading).toBeVisible();
  await expect(page).toHaveTitle('Your data — EclipsVault');
});

test('signed out, Your data bookmarks return through sign-in', async ({ page }) => {
  await page.goto('/Data');
  await expect(page).toHaveURL(/\/Account\/Login\?ReturnUrl=%2FData$/);
  await page.goto('/Data/Index');
  await expect(page).toHaveURL(/\/Account\/Login\?ReturnUrl=%2FData%2FIndex$/);
});
