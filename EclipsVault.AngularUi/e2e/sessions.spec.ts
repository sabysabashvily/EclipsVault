import type { BrowserContext, Page } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { test, expect } from './fixtures';
import { openPalette } from './palette';
import { atRisk } from './security';
import { type Session, SignedInDevices } from './sessions';
import { freshTotp } from './totp';

test.describe.configure({ mode: 'serial' });
const username = 'e2e-signed-in-devices';
const password = `E2e-${randomBytes(24).toString('hex')}`;
let ownerCookies: Awaited<ReturnType<BrowserContext['cookies']>> = [];
let authenticatorSecret = '';
let lastStep = -1;

const current: Session = {
  sessionId: '11111111-1111-1111-1111-111111111111',
  device: 'Chrome on macOS',
  ipAddress: '198.51.100.1',
  createdAtUtc: '2026-10-20T10:00:00Z',
  lastSeenAtUtc: '2026-10-20T11:00:00Z',
  isCurrent: true,
};
const phone: Session = {
  sessionId: '22222222-2222-2222-2222-222222222222',
  device: 'Safari on iOS',
  ipAddress: '198.51.100.2',
  createdAtUtc: '2026-10-20T09:00:00Z',
  lastSeenAtUtc: '2026-10-20T10:00:00Z',
  isCurrent: false,
};
const terminal: Session = {
  sessionId: '33333333-3333-3333-3333-333333333333',
  device: 'curl',
  ipAddress: '198.51.100.3',
  createdAtUtc: '2026-10-19T12:00:00Z',
  lastSeenAtUtc: '2026-10-20T09:00:00Z',
  isCurrent: false,
};

async function openOwner(page: Page, path = '/Sessions'): Promise<SignedInDevices> {
  await page.context().addCookies(ownerCookies);
  await page.goto(path);
  return new SignedInDevices(page);
}

async function signInDevice(page: Page): Promise<void> {
  await page.goto('/Account/Login');
  await page.getByLabel('Username').fill(username);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Continue', exact: true }).click();
  await expect(page).toHaveURL(/\/Account\/Totp$/);
  await page.getByLabel('Authenticator code').fill(await freshTotp(authenticatorSecret, lastStep));
  lastStep = Math.floor(Date.now() / 30_000);
  await page.getByRole('button', { name: 'Verify and sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/Secrets$/);
  await page.goto('/Sessions');
}

test('a separate vault account enrolls MFA for its signed-in device scenarios', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(210_000);
  const state = JSON.parse(await readFile('test-results/sessions-administrator.json', 'utf8')) as {
    cookies: Awaited<ReturnType<BrowserContext['cookies']>>;
  };
  await page.context().addCookies(state.cookies);
  await page.goto('/Admin/CreateUser');
  await page.getByLabel('First name').fill('Devices');
  await page.getByLabel('Last name').fill('Test');
  await page.getByLabel('Username').fill(username);
  await page.getByLabel('Initial password', { exact: true }).fill(password);
  await page.getByLabel('Clearance').selectOption({ label: 'Standard' });
  await page.getByLabel('Project').fill('GLOBAL');
  await page.getByRole('button', { name: 'Create user', exact: true }).click();
  await expect(page).toHaveURL(/\/Admin\/Users$/);

  // The preceding passkey scenarios spend the shared source's credential budget; start a fresh window.
  const windowMilliseconds = 120_000;
  await page.waitForTimeout(windowMilliseconds - (Date.now() % windowMilliseconds) + 1_000);
  const owner = await (await browserHealth.newContext()).newPage();
  await owner.goto('/Account/Login');
  await owner.getByLabel('Username').fill(username);
  await owner.getByLabel('Password', { exact: true }).fill(password);
  await owner.getByRole('button', { name: 'Continue', exact: true }).click();
  await expect(owner).toHaveURL(/\/Account\/EnrollTotp$/);
  // Enrollment is the only place the production vault reveals this throwaway authenticator seed.
  // eslint-disable-next-line playwright/no-raw-locators -- The seed is displayed as unlabelled text.
  authenticatorSecret = await owner.locator('.secret-code').innerText();
  await owner.getByLabel('Confirmation code').fill(await freshTotp(authenticatorSecret));
  lastStep = Math.floor(Date.now() / 30_000);
  await owner.getByRole('button', { name: 'Confirm enrollment', exact: true }).click();
  await expect(owner).toHaveURL(/\/Secrets$/);
  ownerCookies = await owner.context().cookies();
});

for (const [theme, currentAccent, otherAccent] of [
  [
    'dark',
    ['rgb(48, 209, 88)', 'rgba(48, 209, 88, 0.14)'],
    ['rgb(182, 188, 202)', 'rgb(13, 14, 19)'],
  ],
  [
    'light',
    ['rgb(26, 127, 55)', 'rgba(26, 127, 55, 0.13)'],
    ['rgb(74, 78, 88)', 'rgb(236, 238, 242)'],
  ],
] as const) {
  test(`visual parity: Signed-in devices preserves Legacy rows and icons in ${theme}`, async ({
    page,
  }, info) => {
    await page.clock.setFixedTime(new Date('2026-10-20T12:00:00Z'));
    await page.context().addCookies(ownerCookies);
    await page
      .context()
      .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: process.env['E2E_BASE_URL']! }]);
    await page.route('**/api/ui/sessions', (route) =>
      route.fulfill({ json: { sessions: [current, phone, terminal] } }),
    );
    const response = await page.goto('/Sessions');
    const devices = new SignedInDevices(page);
    await expect(devices.rows).toHaveCount(3);
    expect(response?.headers()['content-security-policy']).toContain(
      "require-trusted-types-for 'script'",
    );
    await expect(page).toHaveTitle('Signed-in devices — EclipsVault');
    await expect(devices.heading).toBeVisible();
    await expect(
      page.getByText('Every place your account is currently signed in.', { exact: false }),
    ).toHaveText(
      "Every place your account is currently signed in. Don't recognise one? Sign it out — it loses access on its next request. To end every session at once (including this one), use sign out everywhere on your profile.",
    );
    await expect(devices.device('Chrome on macOS').getByRole('button')).toHaveCount(0);
    await expect(
      devices.device('Safari on iOS').getByRole('button', { name: 'Sign out', exact: true }),
    ).toBeVisible();
    expect(await devices.presentation()).toEqual([
      {
        device: 'Chrome on macOS',
        badges: ['This device'],
        meta: '198.51.100.1 · signed in 2h ago · last active 1h ago',
        action: 'Current',
        tooltip: '2026-10-20 11:00:00 UTC',
        shapes: ['rect:x=3,y=4,width=18,height=12,rx=2', 'path:d=M8 20h8M12 16v4'],
        accent: currentAccent,
      },
      {
        device: 'Safari on iOS',
        badges: [],
        meta: '198.51.100.2 · signed in 3h ago · last active 2h ago',
        action: 'Sign out',
        tooltip: '2026-10-20 10:00:00 UTC',
        shapes: ['rect:x=7,y=3,width=10,height=18,rx=2', 'path:d=M11 18h2'],
        accent: otherAccent,
      },
      {
        device: 'curl',
        badges: [],
        meta: '198.51.100.3 · signed in 1d ago · last active 3h ago',
        action: 'Sign out',
        tooltip: '2026-10-20 09:00:00 UTC',
        shapes: ['rect:x=3,y=4,width=18,height=16,rx=2', 'path:d=M7 9l3 3-3 3M13 15h4'],
        accent: otherAccent,
      },
    ]);
    const path = info.outputPath(`${theme}-signed-in-devices.png`);
    await page.screenshot({ path, fullPage: true });
    await info.attach(`${theme}-signed-in-devices`, { path, contentType: 'image/png' });
  });
}

test('the confirmation announces its text, defaults to Cancel, traps focus and returns it on Escape', async ({
  page,
}) => {
  await page.route('**/api/ui/sessions', (route) =>
    route.fulfill({ json: { sessions: [current, phone] } }),
  );
  const devices = await openOwner(page);
  const signOut = devices
    .device('Safari on iOS')
    .getByRole('button', { name: 'Sign out', exact: true });
  const writes: string[] = [];
  page.on('request', (request) => {
    if (request.method() === 'DELETE') writes.push(request.url());
  });
  await signOut.click();
  await expect(devices.confirmation).toHaveAccessibleName(
    'Sign out this device? It loses access on its next request.',
  );
  await expect(devices.cancel).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(devices.confirmation).not.toBeVisible();
  await expect(signOut).toBeFocused();
  await signOut.click();
  await expect(devices.cancel).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(devices.continue).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(devices.cancel).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(devices.confirmation).not.toBeVisible();
  await expect(signOut).toBeFocused();
  expect(writes).toEqual([]);
});

test('a session_not_found response refreshes the device list without an error or stale row', async ({
  page,
  browserHealth,
}) => {
  let sessions = [current, phone];
  await page.route('**/api/ui/sessions', (route) => route.fulfill({ json: { sessions } }));
  await page.route(`**/api/ui/sessions/${phone.sessionId}`, (route) => {
    sessions = [current];
    return route.fulfill({
      status: 404,
      contentType: 'application/problem+json',
      json: { code: 'session_not_found', title: 'Gone.' },
    });
  });
  browserHealth.expectConsoleError({
    text: /status of 404/,
    url: new RegExp(`/api/ui/sessions/${phone.sessionId}$`),
  });
  const devices = await openOwner(page);
  await devices
    .device('Safari on iOS')
    .getByRole('button', { name: 'Sign out', exact: true })
    .click();
  await devices.continue.click();

  await expect(devices.rows).toHaveCount(1);
  await expect(devices.device('Safari on iOS')).toHaveCount(0);
  await expect(devices.error).toBeEmpty();
  await expect(devices.status).toBeEmpty();
});

test('Signed-in devices reports a load failure with retry and then its Legacy empty state', async ({
  page,
  browserHealth,
}) => {
  browserHealth.expectConsoleError({ text: /status of 503/, url: /\/api\/ui\/sessions$/ });
  await page.route(
    '**/api/ui/sessions',
    (route) => route.fulfill({ status: 503, json: { code: 'internal_error' } }),
    { times: 1 },
  );
  const devices = await openOwner(page);
  await expect(
    page
      .getByRole('alert')
      .filter({ hasText: 'Could not load your signed-in devices. Try again.' }),
  ).toBeVisible();
  await page.route('**/api/ui/sessions', (route) => route.fulfill({ json: { sessions: [] } }), {
    times: 1,
  });
  await page.getByRole('button', { name: 'Try again', exact: true }).click();

  await expect(
    page.getByRole('heading', { name: 'No recorded sessions yet', exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText('Your active sign-ins will appear here.', { exact: false }),
  ).toHaveText(
    'Your active sign-ins will appear here. If this looks empty, sign out and back in to register this device.',
  );
  await expect(devices.rows).toHaveCount(0);
  await expect(devices.signOutOthers).toHaveCount(0);
});

test('the sidebar and palette open Signed-in devices inside Angular and preserve mixed-case bookmarks', async ({
  page,
}) => {
  await openOwner(page, '/Access');
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(request.url());
  });
  await page.getByText('Security', { exact: true }).click();
  const link = page.getByRole('link', { name: 'Signed-in devices', exact: true });
  await link.click();
  await expect(page).toHaveURL(/\/Sessions$/);
  await expect(link).toHaveAttribute('aria-current', 'page');
  await expect(page.getByText('Security', { exact: true })).toHaveClass(/has-active/);
  await page.getByRole('link', { name: 'My activity', exact: true }).click();
  await openPalette(page);
  await page.getByRole('combobox').fill('signed-in devices');
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/Sessions$/);
  expect(documents).toEqual([]);
  await page.goto('/sEsSiOnS/iNdEx');
  await expect(new SignedInDevices(page).heading).toBeVisible();
  await expect(page).toHaveTitle('Signed-in devices — EclipsVault');
});

test('Sign-in history and Security checkup open devices in-app while the profile link loads Legacy', async ({
  page,
}) => {
  await page.route('**/api/ui/sign-ins', (route) =>
    route.fulfill({
      json: {
        summary: {
          successCount: 0,
          failedCount: 1,
          suspiciousCount: 1,
          distinctLocations: 1,
          lastSuccessUtc: null,
          lastFailedUtc: '2026-10-20T11:00:00Z',
          needsAttention: true,
        },
        events: [
          {
            timestampUtc: '2026-10-20T11:00:00Z',
            outcome: 'Failed',
            method: 'Password',
            title: 'Sign-in attempt failed',
            sourceIp: '198.51.100.2',
            locationFlag: 'Unfamiliar',
          },
        ],
      },
    }),
  );
  await openOwner(page, '/SignIns');
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(new URL(request.url()).pathname);
  });
  await page.getByRole('link', { name: 'Review signed-in devices', exact: true }).click();
  await expect(new SignedInDevices(page).heading).toBeVisible();
  expect(documents).toEqual([]);
  await page.route('**/api/ui/security', (route) => route.fulfill({ json: atRisk }));
  await page.getByRole('link', { name: 'Security checkup', exact: true }).click();
  await page.getByRole('link', { name: 'Review devices', exact: true }).click();
  await expect(new SignedInDevices(page).heading).toBeVisible();
  expect(documents).toEqual([]);
  await page.getByRole('link', { name: 'sign out everywhere', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Profile', exact: true })).toBeVisible();
  expect(documents).toEqual(['/Profile']);
});

test('confirming a real device sign-out ends the second browser context on its next request', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(100_000);
  const otherContext = await browserHealth.newContext();
  await otherContext.setExtraHTTPHeaders({ 'User-Agent': 'python/3.12' });
  const other = await otherContext.newPage();
  await signInDevice(other);
  const devices = await openOwner(page);
  await devices.device('python').getByRole('button', { name: 'Sign out', exact: true }).click();
  await devices.continue.click();

  await expect(devices.status).toHaveText(
    'That session was signed out — it loses access on its next request.',
  );
  await expect(devices.rows).toHaveCount(1);
  expect((await other.request.get('/api/ui/me')).status()).toBe(401);
  await other.goto('/Sessions');
  await expect(other).toHaveURL(/\/Account\/Login\?ReturnUrl=%2FSessions$/);
  await expect(other.getByLabel('Username')).toBeVisible();
});

test('signing out all other sessions ends both other browser contexts and announces the count', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(150_000);
  const first = await (await browserHealth.newContext()).newPage();
  await signInDevice(first);
  const second = await (await browserHealth.newContext()).newPage();
  await signInDevice(second);
  const devices = await openOwner(page);
  await expect(devices.rows).toHaveCount(3);
  await devices.signOutOthers.click();
  await expect(devices.confirmation).toHaveAccessibleName(
    'Sign out all other devices? Each loses access on its next request.',
  );
  await devices.continue.click();

  await expect(devices.status).toHaveText('Signed out 2 other sessions.');
  await expect(devices.rows).toHaveCount(1);
  expect((await first.request.get('/api/ui/me')).status()).toBe(401);
  expect((await second.request.get('/api/ui/me')).status()).toBe(401);
  expect((await page.request.get('/api/ui/me')).status()).toBe(200);
});

test('signed out, device bookmarks return through sign-in', async ({ page }) => {
  await page.goto('/Sessions');
  await expect(page).toHaveURL(/\/Account\/Login\?ReturnUrl=%2FSessions$/);
  await page.goto('/Sessions/Index');
  await expect(page).toHaveURL(/\/Account\/Login\?ReturnUrl=%2FSessions%2FIndex$/);
});
