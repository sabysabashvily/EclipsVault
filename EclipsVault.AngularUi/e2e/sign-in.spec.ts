import type { BrowserContext, Page } from '@playwright/test';
import { writeFile } from 'node:fs/promises';
import { randomBytes } from 'node:crypto';
import { test, expect } from './fixtures';
import { clearSignInBudget } from './profile-state';
import {
  type Activity,
  type ExpectedRow,
  feedRows,
  fulfilActivity,
  svgShapes,
  verifyMyActivity,
} from './activity';
import {
  createSecret,
  markDecoy,
  openPalette,
  paletteEntries,
  searchSecrets,
  sidebarPages,
} from './palette';
import {
  type SignIns,
  signInEvents,
  signInNotice,
  signInRows,
  signInVerdict,
  statCards,
  verifySignInHistory,
} from './sign-ins';
import { verifySidebar } from './sidebar';
import {
  allClear,
  atRisk,
  checksNeedingYou,
  failingRows,
  passingChecks,
  passingRows,
  securityVerdict,
  verifySecurityCheckup,
} from './security';
import { currentTotpStep, freshTotp } from './totp';
import { ProfilePage } from './profile';
import { SignedInDevices } from './sessions';
import { SecretsList } from './secrets-list';

// The tests share one vault: the first enrolls the administrator's authenticator, the next signs in with it.
test.describe.configure({ mode: 'serial' });
let authenticatorSecret = '';
let lastSignInStep = -1;
let administratorCookies: Awaited<ReturnType<BrowserContext['cookies']>> = [];
let standardCookies: Awaited<ReturnType<BrowserContext['cookies']>> = [];

function adminPassword(): string {
  const password = process.env['Seed__AdminPassword'];
  if (!password) throw new Error('Run through npm run e2e with Seed__AdminPassword supplied');
  return password;
}

/** The enrolled administrator's ordinary sign-in, with a code from an authenticator step not used before. */
async function signIn(page: Page): Promise<void> {
  await page.goto('/Account/Login');
  await page.getByLabel('Username').fill('vault-admin');
  await page.getByLabel('Password', { exact: true }).fill(adminPassword());
  await page.getByRole('button', { name: 'Continue', exact: true }).click();
  await expect(page).toHaveURL(/\/Account\/Totp$/);
  await page
    .getByLabel('Authenticator code')
    .fill(await freshTotp(authenticatorSecret, lastSignInStep));
  lastSignInStep = currentTotpStep();
  await page.getByRole('button', { name: 'Verify and sign in' }).click();
  await expect(page).toHaveURL(/\/Secrets$/);
  await expect(new SecretsList(page).heading).toBeVisible();
}

/** Spend empty credential requests until the limiter answers or twelve ordinary replies exhaust its budget. */
async function exhaustCredentialBudget(page: Page, token: string): Promise<number> {
  let status = 0;
  for (let attempt = 0; attempt < 12; attempt++) {
    const response = await page.request.post('/Account/Login', {
      headers: { RequestVerificationToken: token },
    });
    status = response.status();
    if (status !== 200) return status;
  }
  return status;
}

/** The ids of the sessions Signed-in devices offers to sign out: every session but the current one. */
async function otherSessionIds(page: Page): Promise<string[]> {
  await page.goto('/Sessions');
  const sessions = await new SignedInDevices(page).liveSessions();
  return sessions.filter((session) => !session.isCurrent).map((session) => session.sessionId);
}

test('sign in, lazy-load My access with Trusted Types, visit Legacy UI and return', async ({
  page,
  context,
  browserHealth,
}) => {
  const password = process.env['Seed__AdminPassword'];
  if (!password) throw new Error('Run through npm run e2e with Seed__AdminPassword supplied');

  await test.step('provision the production bootstrap administrator and enroll MFA', async () => {
    await page.goto('/Account/Login');
    await page.getByLabel('Username').fill('vault-admin');
    await page.getByLabel('Password', { exact: true }).fill(password);
    await page.getByRole('button', { name: 'Continue', exact: true }).click();
    await expect(page).toHaveURL(/\/Account\/EnrollTotp$/);
  });

  const secret = await page.locator('.secret-code').innerText();
  await page.getByLabel('Confirmation code').fill(await freshTotp(secret));
  const enrollmentStep = currentTotpStep();
  await page.getByRole('button', { name: 'Confirm enrollment' }).click();
  await expect(page).toHaveURL(/\/Secrets$/);
  await expect(new SecretsList(page).heading).toBeVisible();

  await test.step('sign out through the Angular account menu, which ends the session on the server', async () => {
    const enrolled = (await context.cookies()).find(
      (cookie) => cookie.name === 'EclipsVault.Session',
    );
    if (!enrolled) throw new Error('Enrollment created no session cookie');
    // Another browser holding a copy of this session signs out; this one keeps its copy to replay.
    const other = await browserHealth.newContext();
    await other.addCookies([enrolled]);
    const angular = await other.newPage();
    await angular.goto('/Access');
    await expect(angular.locator('.access-matrix tbody tr')).toHaveCount(4);

    const xsrf = (await other.cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
    expect(xsrf).toMatchObject({ secure: true, httpOnly: false, sameSite: 'Strict', path: '/' });
    expect(xsrf?.value).not.toBe(enrolled.value);

    await angular.locator('summary.user-card').click();
    const signOut = angular.waitForRequest(
      (request) =>
        request.method() === 'POST' && new URL(request.url()).pathname === '/Account/Logout',
    );
    await angular.getByRole('menuitem', { name: 'Sign out', exact: true }).click();
    expect((await signOut).headers()['requestverificationtoken']).toBe(xsrf?.value);
    await expect(angular).toHaveURL(/\/Account\/Login$/);
    await expect(angular.getByLabel('Username')).toBeVisible();
    expect((await other.cookies()).some((cookie) => cookie.name === 'EclipsVault.Session')).toBe(
      false,
    );

    const replayed = await page.request.get('/api/ui/me');
    expect(replayed.status()).toBe(401);
    expect(replayed.headers()['content-type']).toBe('application/problem+json');
  });

  // Exercise an enrolled account's ordinary two-factor sign-in.
  await page.goto('/Account/Login');
  await page.getByLabel('Username').fill('vault-admin');
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Continue', exact: true }).click();
  await expect(page).toHaveURL(/\/Account\/Totp$/);
  await page.getByLabel('Authenticator code').fill(await freshTotp(secret, enrollmentStep));
  authenticatorSecret = secret;
  lastSignInStep = currentTotpStep();
  await page.getByRole('button', { name: 'Verify and sign in' }).click();
  await expect(page).toHaveURL(/\/Secrets$/);
  await expect(new SecretsList(page).heading).toBeVisible();

  const signInRequests: string[] = [];
  page.on('request', (request) => {
    if (/\/Account\/(Login|Totp|EnrollTotp)/i.test(new URL(request.url()).pathname)) {
      signInRequests.push(request.url());
    }
  });
  const session = (await context.cookies()).find((cookie) => cookie.name === 'EclipsVault.Session');
  expect(session?.secure).toBe(true);
  expect(session?.httpOnly).toBe(true);

  // The access component is a dynamic import, not part of main.js. Observe its actual chunk load.
  // Cached shared chunks have no response body and cannot prove a new lazy load.
  const lazyAccess = page.waitForResponse(
    async (response) =>
      /\/ui\/chunk-[\w-]+\.js$/.test(response.url()) &&
      response.status() === 200 &&
      (await response.text()).includes('What you can open right now'),
  );
  const angularDocument = await page.goto('/Access');
  expect(angularDocument?.headers()['content-security-policy']).toContain(
    "; require-trusted-types-for 'script'; trusted-types angular angular#bundler",
  );
  expect((await lazyAccess).status()).toBe(200);
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  await expect(page.locator('.access-matrix tbody tr')).toHaveCount(4);

  await page.goto('/Access/Index');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();

  // Before any in-app navigation, so page.url() is the document's own address. A router link changes
  // the address bar while the violation report keeps the address the document was loaded from.
  await test.step('Trusted Types rejects a raw DOM assignment before it changes the DOM', async () => {
    // One precise, required violation is intentional; the shared fixture still rejects every other one.
    browserHealth.expectCspViolation({
      effectiveDirective: 'require-trusted-types-for',
      blockedURI: 'trusted-types-sink',
      sample: 'Element innerHTML|<p>tt-unsafe-assignment</p>',
      documentURI: page.url(),
      disposition: 'enforce',
    });
    browserHealth.expectConsoleError({
      text: /^This document requires 'TrustedHTML' assignment\. The action has been blocked\.$/,
      // Chromium attributes this browser-generated rejection to no source URL.
      url: /^$/,
    });
    const result = await page.evaluate(async () => {
      const target = document.createElement('div');
      target.textContent = 'safe original';
      document.body.append(target);
      const violation = new Promise<SecurityPolicyViolationEvent>((resolve) => {
        document.addEventListener('securitypolicyviolation', resolve, { once: true });
      });
      let threwTypeError = false;
      try {
        target.innerHTML = '<p>tt-unsafe-assignment</p>';
      } catch (error) {
        threwTypeError = error instanceof TypeError;
      }
      const event = await violation;
      const evidence = {
        threwTypeError,
        html: target.innerHTML,
        directive: event.effectiveDirective,
        disposition: event.disposition,
      };
      target.remove();
      return evidence;
    });
    expect(result).toEqual({
      threwTypeError: true,
      html: 'safe original',
      directive: 'require-trusted-types-for',
      disposition: 'enforce',
    });
  });

  await page.getByRole('link', { name: 'My access', exact: true }).click();
  await expect(page).toHaveURL(/\/Access$/);
  await expect(page.locator('.access-matrix tbody tr')).toHaveCount(4);

  const legacyDocument = page.waitForResponse((response) =>
    response.request().isNavigationRequest(),
  );
  await page.getByRole('link', { name: 'Dynamic secrets', exact: true }).click();
  expect((await legacyDocument).headers()['content-security-policy']).not.toContain(
    'trusted-types',
  );
  await expect(page).toHaveURL(/\/DynamicSecrets$/);
  await expect(page.getByRole('heading', { name: 'Dynamic secrets', exact: true })).toBeVisible();
  await expect(page.locator('.sidebar')).toContainText('Vault Administrator');
  await page.getByText('Access', { exact: true }).click();
  await page.getByRole('link', { name: 'My access', exact: true }).click();
  await expect(page).toHaveURL(/\/Access$/);
  await expect(page.locator('.access-matrix tbody tr')).toHaveCount(4);
  expect(signInRequests).toEqual([]);
  expect(
    (await context.cookies()).find((cookie) => cookie.name === 'EclipsVault.Session')?.value,
  ).toBe(session?.value);
  administratorCookies = await context.cookies();
});

test("My activity shows the administrator's own rows in both themes, newest first, and nothing past the end", async ({
  page,
}, info) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  // The first test's own actions, newest first: the ordinary two-factor sign-in and the accepted
  // code it was issued for, then the sign-in that confirming enrolment completes, then the enrolment.
  const rows: ExpectedRow[] = [
    { title: 'Signed in', category: 'Sign-in', tone: 'muted' },
    { title: 'Proved a sign-in factor', category: 'Sign-in', tone: 'muted' },
    { title: 'Signed in', category: 'Sign-in', tone: 'muted' },
    { title: 'Set up your authenticator', category: 'Account', tone: 'warn' },
  ];
  await test.step(`dark theme`, async () => {
    await verifyMyActivity(page, info, 'dark', rows);
  });
  await test.step(`light theme`, async () => {
    await verifyMyActivity(page, info, 'light', rows);
  });

  await test.step('a page past the end offers the way back to the latest page, in-app', async () => {
    await page.goto('/Activity?page=999');
    await expect(page.locator('.panel > p.muted')).toHaveText('Nothing further back to show.');
    await expect(page.locator('.activity-table, .pager, .empty-state')).toHaveCount(0);
    await expect(page.locator('style, [style]')).toHaveCount(0);
    const documents: string[] = [];
    page.on('request', (request) => {
      if (request.isNavigationRequest() && request.resourceType() === 'document')
        documents.push(request.url());
    });
    const latest = page.getByRole('link', { name: 'Back to latest', exact: true });
    await expect(latest).toHaveAttribute('href', '/Activity');
    await latest.click();
    await expect(page).toHaveURL(/\/Activity$/);
    await expect(page.locator('.activity-table tbody tr')).toHaveCount(rows.length);
    expect(documents).toEqual([]);
  });
});

test("Sign-in history shows the administrator's real sign-ins in both themes", async ({
  page,
}, info) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  await test.step(`dark theme`, async () => {
    await verifySignInHistory(page, info, 'dark');
  });
  await test.step(`light theme`, async () => {
    await verifySignInHistory(page, info, 'light');
  });
});

test("Security checkup shows the administrator's real posture without a passkey in both themes", async ({
  page,
}, info) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  await test.step(`dark theme`, async () => {
    await verifySecurityCheckup(page, info, 'dark');
  });
  await test.step(`light theme`, async () => {
    await verifySecurityCheckup(page, info, 'light');
  });
});

test('visual parity: the administrator and Standard sidebars match Legacy in both themes and navigate across UIs', async ({
  page,
  browserHealth,
}, info) => {
  test.setTimeout(180_000);
  // Reuse the real session established above. Extra sign-ins would exhaust the production per-IP
  // password budget before the revocation test; the limiter stays fully enabled.
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access');
  await test.step('administrator navigation and visual comparison', async () => {
    await verifySidebar(page, info, 'administrator');
  });

  const username = 'e2e-standard-sidebar';
  const password = `E2e-${randomBytes(24).toString('hex')}`;
  await test.step('provision a Standard account through the administrator UI', async () => {
    await page.goto('/Admin/CreateUser');
    await page.getByLabel('First name').fill('Standard');
    await page.getByLabel('Last name').fill('Sidebar');
    await page.getByLabel('Username').fill(username);
    await page.getByLabel('Initial password', { exact: true }).fill(password);
    await page.getByLabel('Clearance').selectOption({ label: 'Standard' });
    await page.getByLabel('Project').fill('GLOBAL');
    await page.getByRole('button', { name: 'Create user', exact: true }).click();
    await expect(page).toHaveURL(/\/Admin\/Users$/);
  });

  const standard = await (await browserHealth.newContext()).newPage();
  await test.step('Standard account signs in with its own password and enrolls MFA', async () => {
    await standard.goto('/Account/Login');
    await standard.getByLabel('Username').fill(username);
    await standard.getByLabel('Password', { exact: true }).fill(password);
    await standard.getByRole('button', { name: 'Continue', exact: true }).click();
    await expect(standard).toHaveURL(/\/Account\/EnrollTotp$/);
    await standard
      .getByLabel('Confirmation code')
      .fill(await freshTotp(await standard.locator('.secret-code').innerText()));
    await standard.getByRole('button', { name: 'Confirm enrollment' }).click();
    await expect(standard).toHaveURL(/\/Secrets$/);
  });
  await test.step('Standard navigation and visual comparison', async () => {
    await verifySidebar(standard, info, 'standard');
  });
  standardCookies = await standard.context().cookies();
});

test('a denied My access cell explains itself in a side panel, a bottom sheet on a phone', async ({
  page,
}) => {
  await page.context().addCookies(standardCookies);
  await page.goto('/Access');
  const cell = page.getByRole('button', {
    name: 'Clearance too low, Top Secret · Production',
    exact: true,
  });
  const panel = page.getByRole('dialog', { name: 'Top Secret · Production', exact: true });
  await cell.click();
  await expect(panel).toContainText('Not now: Clearance too low');
  await expect(panel.getByRole('definition').first()).toHaveText('Standard');
  await page.keyboard.press('Escape');
  await expect(panel).toBeHidden();
  await expect(cell).toBeFocused();

  await page.setViewportSize({ width: 375, height: 800 });
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(375);
  await cell.click();
  await expect
    .poll(async () => {
      const box = await panel.boundingBox();
      return box && [Math.round(box.x), Math.round(box.width), Math.round(box.y + box.height)];
    })
    .toEqual([0, 375, 800]);
  await page.keyboard.press('Escape');
  await expect(panel).toBeHidden();
  await expect(cell).toBeFocused();
});

test('both UIs share the theme, and the Angular UI applies it before it starts', async ({
  page,
}) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/DynamicSecrets');
  await page.context().addCookies([
    {
      name: 'EclipsVault.Theme',
      value: 'dark',
      url: new URL('/', page.url()).href,
      secure: true,
    },
  ]);
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');

  await test.step('switched in the Legacy UI, the theme is on the Angular document before Angular runs', async () => {
    await page.locator('summary.user-card').click();
    await page.getByRole('menuitem', { name: 'Switch to light', exact: true }).click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');

    // Hold the application bundle, so only the external boot script can have set the theme.
    let release: () => void = () => undefined;
    const held = new Promise<void>((resolve) => (release = resolve));
    const bundle = /\/ui\/main-[\w-]+\.js$/;
    await page.route(bundle, async (route) => {
      await held;
      await route.continue();
    });
    await page.goto('/Access', { waitUntil: 'commit' });
    await page.waitForFunction(() => document.documentElement.hasAttribute('data-theme'));
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');
    await expect(page.locator('app-root')).toBeEmpty();
    release();
    await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
    await page.unroute(bundle);
  });

  await test.step('switched in the Angular UI, the theme reaches the Legacy UI', async () => {
    await page.locator('summary.user-card').click();
    await page.getByRole('menuitem', { name: 'Switch to dark', exact: true }).click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    expect(
      (await page.context().cookies()).find((cookie) => cookie.name === 'EclipsVault.Theme')?.value,
    ).toBe('dark');
    await page.goto('/DynamicSecrets');
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  });
});

test("the ⌘K palette offers exactly the sidebar's pages, as the Legacy palette does, for an administrator and a Standard account", async ({
  page,
  browserHealth,
}) => {
  expect(standardCookies).not.toHaveLength(0);
  await page.context().addCookies(administratorCookies);
  const standard = await (await browserHealth.newContext()).newPage();
  await standard.context().addCookies(standardCookies);

  await test.step(`administrator: Legacy and Angular palettes list the same pages as the sidebar`, async () => {
    await page.goto('/DynamicSecrets');
    // eslint-disable-next-line playwright/no-raw-locators -- Visual parity compares the displayed shortcut inside each shell command button.
    const legacyChord = await page.locator('.command-trigger kbd').textContent();
    await openPalette(page);
    const legacy = await paletteEntries(page);
    await page.keyboard.press('Escape');
    await page.goto('/Access');
    await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
    // eslint-disable-next-line playwright/no-raw-locators -- Visual parity compares the displayed shortcut inside each shell command button.
    await expect(page.locator('.topbar > .search-pill kbd')).toHaveText(legacyChord ?? '');
    expect(legacyChord).toMatch(/^(⌘K|Ctrl K)$/);
    const sidebar = await sidebarPages(page);
    await openPalette(page);
    const angular = await paletteEntries(page);
    // Creation is a palette shortcut after the navigation destinations.
    expect(angular).toEqual([...sidebar.map((label) => ['Go to', label]), ['Go to', 'New secret']]);
    expect([...angular].sort()).toEqual([...legacy].sort());
    expect(angular).toHaveLength(21);
    await expect(page.locator('style, [style]')).toHaveCount(0);
  });
  await test.step(`standard: Legacy and Angular palettes list the same pages as the sidebar`, async () => {
    await standard.goto('/DynamicSecrets');
    // eslint-disable-next-line playwright/no-raw-locators -- Visual parity compares the displayed shortcut inside each shell command button.
    const legacyChord = await standard.locator('.command-trigger kbd').textContent();
    await openPalette(standard);
    const legacy = await paletteEntries(standard);
    await standard.keyboard.press('Escape');
    await standard.goto('/Access');
    await expect(standard.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
    // eslint-disable-next-line playwright/no-raw-locators -- Visual parity compares the displayed shortcut inside each shell command button.
    await expect(standard.locator('.topbar > .search-pill kbd')).toHaveText(legacyChord ?? '');
    expect(legacyChord).toMatch(/^(⌘K|Ctrl K)$/);
    const sidebar = await sidebarPages(standard);
    await openPalette(standard);
    const angular = await paletteEntries(standard);
    // New secret left the sidebar for the page's own button, so the palette offers it after the pages.
    expect(angular).toEqual([...sidebar.map((label) => ['Go to', label]), ['Go to', 'New secret']]);
    expect([...angular].sort()).toEqual([...legacy].sort());
    expect(angular).toHaveLength(14);
    expect(sidebar).not.toContain('Users');
    expect(sidebar).not.toContain('Networks');
    expect(sidebar).not.toContain('Service accounts');
    expect(sidebar).not.toContain('Audit log');
    expect(sidebar).not.toContain('Notifications');
    expect(sidebar).not.toContain('Encryption keys');
    expect(sidebar).not.toContain('License');
    await expect(standard.locator('style, [style]')).toHaveCount(0);
  });
});

test('palette search lists only secrets the account could already list, never a decoy, and nothing for one character', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(150_000);
  await page.context().addCookies(administratorCookies);

  await test.step('the administrator stores a Standard-visible secret, one above Standard clearance, and a decoy', async () => {
    await createSecret(page, 'Palette_Probe_Listed', 'Internal');
    await createSecret(page, 'Palette_Probe_Confidential', 'Confidential');
    // Visible to Standard in every attribute; only being a decoy keeps it out of every list.
    await createSecret(page, 'Palette_Probe_Decoy', 'Internal');
    markDecoy('Palette_Probe_Decoy');
  });

  await test.step("the administrator's search finds both real secrets and no decoy", async () => {
    await page.goto('/Access');
    await openPalette(page);
    expect(await searchSecrets(page, 'Palette_Probe')).toEqual({
      status: 200,
      names: ['Palette_Probe_Confidential', 'Palette_Probe_Listed'],
    });
    expect((await searchSecrets(page, 'Decoy')).names).toEqual([]);
  });

  const standard = await (await browserHealth.newContext()).newPage();
  await standard.context().addCookies(standardCookies);
  await standard.goto('/Access');
  await expect(standard.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();

  await test.step('a Standard account finds only the secret its clearance lists, and neither hidden one by name', async () => {
    await openPalette(standard);
    expect(await searchSecrets(standard, 'Palette_Probe')).toEqual({
      status: 200,
      names: ['Palette_Probe_Listed'],
    });
    const listed = standard.locator('.palette-item').filter({ hasText: 'Palette_Probe_Listed' });
    await expect(listed.locator('.palette-meta')).toHaveText('GLOBAL · Development');
    await expect(listed.locator('.badge.sens-1')).toHaveText('Internal');
    expect((await searchSecrets(standard, 'Palette_Probe_Decoy')).names).toEqual([]);
    expect((await searchSecrets(standard, 'Palette_Probe_Confidential')).names).toEqual([]);
    await expect(standard.locator('.palette-empty')).toHaveText('Nothing matches that.');
  });

  // That the palette never sends such a query is pinned by its unit test, on a controlled clock.
  await test.step('a one-character query lists no secrets, and the server would answer nothing', async () => {
    await standard.locator('.palette-input').fill('P');
    expect((await paletteEntries(standard)).filter(([kind]) => kind === 'Secret')).toEqual([]);
    await test.step('an uppercase one-character query returns no secrets', async () => {
      const direct = await standard.request.get(
        `/api/ui/secrets/search?q=${encodeURIComponent('P')}`,
        {
          headers: { Accept: 'application/json' },
        },
      );
      expect(direct.status()).toBe(200);
      expect(await direct.json()).toEqual([]);
    });
    await test.step('a lowercase one-character query returns no secrets', async () => {
      const direct = await standard.request.get(
        `/api/ui/secrets/search?q=${encodeURIComponent('p')}`,
        {
          headers: { Accept: 'application/json' },
        },
      );
      expect(direct.status()).toBe(200);
      expect(await direct.json()).toEqual([]);
    });
    await test.step('a punctuation-only query returns no secrets', async () => {
      const direct = await standard.request.get(
        `/api/ui/secrets/search?q=${encodeURIComponent('_')}`,
        {
          headers: { Accept: 'application/json' },
        },
      );
      expect(direct.status()).toBe(200);
      expect(await direct.json()).toEqual([]);
    });
    await test.step('a padded one-character query returns no secrets', async () => {
      const direct = await standard.request.get(
        `/api/ui/secrets/search?q=${encodeURIComponent(' P ')}`,
        {
          headers: { Accept: 'application/json' },
        },
      );
      expect(direct.status()).toBe(200);
      expect(await direct.json()).toEqual([]);
    });
  });

  await test.step('Enter opens the chosen secret in the Legacy UI', async () => {
    await searchSecrets(standard, 'Palette_Probe');
    await standard.keyboard.press('Enter');
    await expect(standard).toHaveURL(/\/Secrets\/Details\/[0-9a-f-]{36}$/);
    await expect(
      standard.getByRole('heading', { name: 'Palette_Probe_Listed', exact: true }),
    ).toBeVisible();
  });
});

test('the palette is driven from the keyboard, traps focus, and hands it back on Escape', async ({
  page,
}) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access/Index');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  const options = page.locator('.palette-item');

  await test.step('Escape returns focus to the element that had it', async () => {
    const origin = page.getByRole('link', { name: 'My access', exact: true });
    await origin.focus();
    await openPalette(page);
    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(origin).toBeFocused();

    const trigger = page.getByRole('button', { name: 'Search secrets or jump to a page' });
    await trigger.click();
    // eslint-disable-next-line playwright/no-raw-locators -- The command-palette input is the keyboard focus-trap target.
    await expect(page.locator('.palette-input')).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(trigger).toBeFocused();
  });

  await test.step('the chord toggles it, arrows move the one highlight, and Tab stays in the dialog', async () => {
    await openPalette(page);
    await page.keyboard.press('ControlOrMeta+k');
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await openPalette(page);

    await expect(options.nth(0)).toHaveAttribute('aria-selected', 'true');
    await page.keyboard.press('ArrowUp');
    await expect(options.nth(0)).toHaveAttribute('aria-selected', 'true');
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('ArrowDown');
    await expect(options.nth(2)).toHaveAttribute('aria-selected', 'true');
    await expect(page.locator('[aria-selected="true"]')).toHaveCount(1);
    await expect(page.locator('.palette-input')).toHaveAttribute(
      'aria-activedescendant',
      'palette-option-2',
    );
    await page.keyboard.press('ArrowUp');
    await expect(options.nth(1)).toHaveAttribute('aria-selected', 'true');

    await page.keyboard.press('Tab');
    // eslint-disable-next-line playwright/no-raw-locators -- The command-palette input is the keyboard focus-trap target.
    await expect(page.locator('.palette-input')).toBeFocused();
    await page.keyboard.press('Shift+Tab');
    // eslint-disable-next-line playwright/no-raw-locators -- The command-palette input is the keyboard focus-trap target.
    await expect(page.locator('.palette-input')).toBeFocused();
    await page.keyboard.press('Tab');
    // eslint-disable-next-line playwright/no-raw-locators -- The command-palette input is the keyboard focus-trap target.
    await expect(page.locator('.palette-input')).toBeFocused();
    // A press inside the panel keeps the field focused; one on the backdrop dismisses it.
    await page.locator('.palette-hint').click();
    // eslint-disable-next-line playwright/no-raw-locators -- The command-palette input is the keyboard focus-trap target.
    await expect(page.locator('.palette-input')).toBeFocused();
    await page.locator('.palette-backdrop').click({ position: { x: 5, y: 5 } });
    await expect(page.getByRole('dialog')).toHaveCount(0);
  });

  await test.step('Enter opens an Angular UI page in place, and a Legacy UI page with a page load', async () => {
    const documents: string[] = [];
    page.on('request', (request) => {
      if (request.isNavigationRequest() && request.resourceType() === 'document')
        documents.push(new URL(request.url()).pathname);
    });
    await openPalette(page);
    await page.locator('.palette-input').fill('my access');
    await expect(options).toHaveText([/My access/]);
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/Access$/);
    await expect(page.getByRole('dialog')).toHaveCount(0);
    expect(documents).toEqual([]);

    await openPalette(page);
    await page.locator('.palette-input').fill('signed-in dev');
    await expect(options).toHaveText([/Signed-in devices/]);
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/Sessions$/);
    expect(documents).toEqual([]);

    await openPalette(page);
    await page.getByRole('combobox').fill('your data');
    await expect(options).toHaveText([/Your data/]);
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/Data$/);
    expect(documents).toEqual([]);

    await openPalette(page);
    await page.getByRole('combobox').fill('access requests');
    await expect(options).toHaveText([/Access requests/]);
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/AccessRequests$/);
    expect(documents).toEqual([]);

    await openPalette(page);
    await page.getByRole('combobox').fill('dynamic secrets');
    await expect(options).toHaveText([/Dynamic secrets/]);
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/DynamicSecrets$/);
    expect(documents).toEqual(['/DynamicSecrets']);
  });
});

test('an ended session sends palette search to sign-in with its return URL', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  // Only this browser forgets the session; the administrator's session itself stays valid for later tests.
  await page.context().clearCookies({ name: 'EclipsVault.Session' });
  browserHealth.expectConsoleError({ text: /status of 401/, url: /\/api\/ui\/secrets\/search\?/ });

  const refusals: { status: number; body: unknown }[] = [];
  // Navigation discards browser response bodies, so capture the real reply before delivering it.
  await page.route(/\/api\/ui\/secrets\/search\?/, async (route) => {
    const response = await route.fetch();
    refusals.push({ status: response.status(), body: await response.json() });
    await route.fulfill({ response });
  });

  await openPalette(page);
  await page.getByRole('combobox').fill('Palette_Probe');
  await expect(page).toHaveURL(/\/Account\/Login\?ReturnUrl=%2FAccess$/);
  expect(refusals).toEqual([
    { status: 401, body: expect.objectContaining({ code: 'unauthenticated' }) },
  ]);
});

test('an access failure shows a retry and the next request restores the live grid', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(administratorCookies);
  browserHealth.expectConsoleError({ text: /status of 503/, url: /\/api\/ui\/access$/ });
  await page.route(
    '**/api/ui/access',
    async (route) => {
      await route.fulfill({
        status: 503,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'audit_unavailable' }),
      });
    },
    { times: 1 },
  );
  await page.goto('/Access');
  await expect(page.getByRole('alert')).toHaveText(
    'Could not load your access. Reload the page to try again.',
  );
  await expect(page.locator('.access-matrix')).toHaveCount(0);
  await page.getByRole('button', { name: 'Try again', exact: true }).click();
  await expect(page.locator('.access-matrix tbody tr')).toHaveCount(4);
  await expect(page.getByRole('alert')).toHaveCount(0);
});

const emptyActivity: Activity = {
  page: 1,
  pageSize: 25,
  hasMore: false,
  hasPrevious: false,
  notableCount: 0,
  items: [],
};
const longName = 'Payments_Production_Database_Primary_Connection_String_Rotated_Quarterly';

/** Two mocked My activity pages, timed from now so their relative times read as the test expects. */
function activityPages(): { first: Activity; second: Activity } {
  const minute = 60_000;
  const now = Date.now();
  const ago = (milliseconds: number) => new Date(now - milliseconds).toISOString();
  const first: Activity = {
    page: 1,
    pageSize: 25,
    hasMore: true,
    hasPrevious: false,
    notableCount: 2,
    items: [
      {
        timestampUtc: ago(20_000),
        category: 'Secrets',
        title: 'Revealed a secret value',
        severity: 'Notable',
        resourceName: longName,
        sourceIp: '203.0.113.7',
      },
      {
        timestampUtc: ago(5 * minute + 20_000),
        category: 'Security',
        title: 'A session was force-revoked',
        severity: 'Critical',
        resourceName: null,
        sourceIp: '203.0.113.8',
      },
      {
        timestampUtc: ago(3 * 60 * minute + minute),
        category: 'Authentication',
        title: 'Signed in',
        severity: 'Routine',
        resourceName: '   ',
        sourceIp: '203.0.113.7',
      },
      {
        timestampUtc: ago(2 * 24 * 60 * minute + 60 * minute),
        category: 'Sharing',
        title: 'Shared a secret',
        severity: 'Notable',
        resourceName: 'Shared_Key',
        sourceIp: '2001:db8::5',
      },
    ],
  };
  const second: Activity = {
    page: 2,
    pageSize: 25,
    hasMore: false,
    hasPrevious: true,
    notableCount: 0,
    items: [
      {
        timestampUtc: '2026-01-02T03:04:05.678Z',
        category: 'Administration',
        title: 'Created a user',
        severity: 'Routine',
        resourceName: null,
        sourceIp: '198.51.100.20',
      },
      {
        timestampUtc: '2025-12-31T23:59:59Z',
        category: 'Account',
        title: 'Changed your password',
        severity: 'Routine',
        resourceName: null,
        sourceIp: '198.51.100.20',
      },
      {
        timestampUtc: '2025-12-30T08:00:00Z',
        category: 'Automation',
        title: 'Issued an API key',
        severity: 'Routine',
        resourceName: null,
        sourceIp: 'internal',
      },
      {
        timestampUtc: '2025-12-29T08:00:00Z',
        category: 'Other',
        title: 'Some new action',
        severity: 'Routine',
        resourceName: null,
        sourceIp: 'internal',
      },
    ],
  };
  return { first, second };
}

test("My activity's empty state and pager, and paging is in-app with back and forward refetching", async ({
  page,
}) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(new URL(request.url()).pathname);
  });

  await test.step('an account with no activity sees the empty state', async () => {
    const requested = await fulfilActivity(page, { '': emptyActivity });
    await page.getByRole('link', { name: 'My activity', exact: true }).click();
    // The Legacy partial's own element, not a host element carrying its class.
    await expect(page.locator('div.empty-state')).toHaveCount(1);
    await expect(page.locator('.empty-state')).toHaveCount(1);
    await expect(page.locator('.empty-state h3')).toHaveText('No activity yet');
    await expect(page.locator('.empty-state p')).toHaveText(
      'As you sign in, view and reveal secrets, and update your account, each action is recorded here so you can review it later.',
    );
    await expect(page.locator('.activity-table, .pager, .page-header > .badge')).toHaveCount(0);
    await expect(page.locator('.panel > p.muted')).toHaveCount(0);
    await expect(page.locator('style, [style]')).toHaveCount(0);
    expect(requested).toEqual(['']);
    await page.unrouteAll({ behavior: 'wait' });
  });

  const { first, second } = activityPages();
  const requested = await fulfilActivity(page, { '': first, '1': first, '2': second });
  const pager = page.locator('nav.pager[aria-label="Activity pages"]');
  const pagerLinks = async () =>
    pager
      .locator('a')
      .evaluateAll((links) =>
        links.map((link) => [link.textContent?.trim(), link.getAttribute('href')]),
      );

  await test.step('the first page shows each row, its accent and badges, and only the older link', async () => {
    await page.getByRole('link', { name: 'My access', exact: true }).click();
    await page.getByRole('link', { name: 'My activity', exact: true }).click();
    await expect(page).toHaveURL(/\/Activity$/);
    await expect(page.locator('.activity-table tbody tr')).toHaveCount(4);
    const exactTime = /^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d UTC$/;
    expect(await feedRows(page)).toEqual([
      {
        title: 'Revealed a secret value',
        kind: 'Secrets',
        tone: 'tone-warn',
        resource: longName,
        from: '203.0.113.7',
        time: expect.stringMatching(exactTime),
      },
      {
        title: 'A session was force-revoked',
        kind: 'Security',
        tone: 'tone-critical',
        resource: null,
        from: '203.0.113.8',
        time: expect.stringMatching(exactTime),
      },
      {
        title: 'Signed in',
        kind: 'Sign-in',
        tone: 'tone-muted',
        resource: null,
        from: '203.0.113.7',
        time: expect.stringMatching(exactTime),
      },
      {
        title: 'Shared a secret',
        kind: 'Sharing',
        tone: 'tone-warn',
        resource: 'Shared_Key',
        from: '2001:db8::5',
        time: expect.stringMatching(exactTime),
      },
    ]);
    await expect(page.locator('.page-sub')).toContainText('2 notable on this page.');
    await expect(pager.locator('.pager-page')).toHaveText('Page 1');
    expect(await pagerLinks()).toEqual([['Older →', '/Activity?page=2']]);
    await expect(page.locator('style, [style]')).toHaveCount(0);
  });

  await test.step('Older → changes ?page= in-app and shows the older page', async () => {
    await pager.getByRole('link', { name: 'Older →', exact: true }).click();
    await expect(page).toHaveURL(/\/Activity\?page=2$/);
    await expect(pager.locator('.pager-page')).toHaveText('Page 2');
    expect(await pagerLinks()).toEqual([['← Newer', '/Activity?page=1']]);
    await expect(page.locator('.page-sub')).not.toContainText('notable');
    expect(await feedRows(page)).toEqual([
      {
        title: 'Created a user',
        kind: 'Admin',
        tone: 'tone-muted',
        resource: null,
        from: '198.51.100.20',
        time: '2026-01-02 03:04:05 UTC',
      },
      {
        title: 'Changed your password',
        kind: 'Account',
        tone: 'tone-muted',
        resource: null,
        from: '198.51.100.20',
        time: '2025-12-31 23:59:59 UTC',
      },
      {
        title: 'Issued an API key',
        kind: 'Automation',
        tone: 'tone-muted',
        resource: null,
        from: 'internal',
        time: '2025-12-30 08:00:00 UTC',
      },
      {
        title: 'Some new action',
        kind: 'Other',
        tone: 'tone-muted',
        resource: null,
        from: 'internal',
        time: '2025-12-29 08:00:00 UTC',
      },
    ]);
  });

  await test.step('back and forward move between the visited pages and refetch each', async () => {
    await page.goBack();
    await expect(page).toHaveURL(/\/Activity$/);
    await expect(pager.locator('.pager-page')).toHaveText('Page 1');
    await expect(page.locator('.activity-title').first()).toHaveText('Revealed a secret value');
    await page.goForward();
    await expect(page).toHaveURL(/\/Activity\?page=2$/);
    await expect(pager.locator('.pager-page')).toHaveText('Page 2');
    await pager.getByRole('link', { name: '← Newer', exact: true }).click();
    await expect(page).toHaveURL(/\/Activity\?page=1$/);
    await expect(pager.locator('.pager-page')).toHaveText('Page 1');
    expect(requested).toEqual(['', '2', '', '2', '1']);
    expect(documents).toEqual([]);
  });
  await page.unrouteAll({ behavior: 'wait' });
});

test('the sidebar and palette open My activity in-app, and its Profile link opens Angular on the same session', async ({
  page,
}) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  const session = (await page.context().cookies()).find(
    (cookie) => cookie.name === 'EclipsVault.Session',
  );
  const documents: string[] = [];
  const signInRequests: string[] = [];
  page.on('request', (request) => {
    if (/\/Account\/(Login|Totp|EnrollTotp)/i.test(new URL(request.url()).pathname))
      signInRequests.push(request.url());
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(new URL(request.url()).pathname);
  });

  await test.step('the sidebar link keeps the document and marks My activity and its group active', async () => {
    await page.getByRole('link', { name: 'My activity', exact: true }).click();
    await expect(page).toHaveURL(/\/Activity$/);
    await expect(page).toHaveTitle('My activity — EclipsVault');
    await expect(page.getByRole('heading', { name: 'My activity', exact: true })).toBeVisible();
    await expect(page.locator('.sidenav a.active')).toHaveText('My activity');
    await expect(page.locator('.sidenav a.active')).toHaveAttribute('aria-current', 'page');
    await expect(page.locator('.sidenav summary.has-active')).toHaveText('You');
    await expect(page.locator('.sidenav details:has(a.active)')).toHaveAttribute('open', '');
    expect(documents).toEqual([]);
  });

  await test.step('the palette opens My activity in place', async () => {
    await page.getByRole('link', { name: 'My access', exact: true }).click();
    await expect(page).toHaveURL(/\/Access$/);
    await openPalette(page);
    await page.locator('.palette-input').fill('my activity');
    await expect(page.locator('.palette-item')).toHaveText([/My activity/]);
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/Activity$/);
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page.locator('.activity-table tbody tr').first()).toBeVisible();
    expect(documents).toEqual([]);
  });

  await test.step('the Profile link stays inside Angular on the same session', async () => {
    await page
      .getByRole('link', { name: 'Change your password or sign out everywhere', exact: true })
      .click();
    await expect(page).toHaveURL(/\/Profile$/);
    await expect(page.getByRole('heading', { name: 'Profile', exact: true })).toBeVisible();
    expect(documents).toEqual([]);
    expect(signInRequests).toEqual([]);
    expect(
      (await page.context().cookies()).find((cookie) => cookie.name === 'EclipsVault.Session')
        ?.value,
    ).toBe(session?.value);
  });
});

test('an activity failure shows a retry and the next request restores the feed', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(administratorCookies);
  browserHealth.expectConsoleError({ text: /status of 503/, url: /\/api\/ui\/activity$/ });
  await page.route(
    '**/api/ui/activity',
    async (route) => {
      await route.fulfill({
        status: 503,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'audit_unavailable' }),
      });
    },
    { times: 1 },
  );
  await page.goto('/Activity');
  await expect(page.getByRole('alert')).toHaveText(
    'Could not load your activity. Reload the page to try again.',
  );
  await expect(page.locator('.activity-table')).toHaveCount(0);
  await page.getByRole('button', { name: 'Try again', exact: true }).click();
  await expect(page.locator('.activity-table tbody tr').first()).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(page.locator('style, [style]')).toHaveCount(0);
});

test('signed out, My activity sends the browser to sign-in with its return URL', async ({
  page,
}) => {
  await test.step('My activity retains its return URL', async () => {
    await page.goto('/Activity');
    await expect(page).toHaveURL(new RegExp(`/Account/Login\\?ReturnUrl=%2FActivity$`));
    await expect(page.getByLabel('Username')).toBeVisible();
  });
  await test.step('the My activity alias retains its return URL', async () => {
    await page.goto('/Activity/Index');
    await expect(page).toHaveURL(new RegExp(`/Account/Login\\?ReturnUrl=%2FActivity%2FIndex$`));
    await expect(page.getByLabel('Username')).toBeVisible();
  });
});

const emptySignIns: SignIns = {
  summary: {
    successCount: 0,
    failedCount: 0,
    suspiciousCount: 0,
    distinctLocations: 0,
    lastSuccessUtc: null,
    lastFailedUtc: null,
    needsAttention: false,
  },
  events: [],
};

/** An unfamiliar-location attempt among other sign-ins, timed from now. */
function unfamiliarSignIns(): SignIns {
  const minute = 60_000;
  const now = Date.now();
  const ago = (milliseconds: number) => new Date(now - milliseconds).toISOString();
  return {
    summary: {
      successCount: 3,
      failedCount: 3,
      suspiciousCount: 2,
      distinctLocations: 2,
      lastSuccessUtc: ago(2 * 24 * 60 * minute + 60 * minute),
      lastFailedUtc: ago(20_000),
      needsAttention: true,
    },
    events: [
      {
        timestampUtc: ago(20_000),
        outcome: 'Failed',
        method: 'Password',
        title: 'Sign-in attempt failed',
        sourceIp: '203.0.113.50',
        locationFlag: 'Unfamiliar',
      },
      {
        timestampUtc: ago(5 * minute + 20_000),
        outcome: 'Blocked',
        method: 'System',
        title: 'Account locked after repeated failures',
        sourceIp: '203.0.113.50',
        locationFlag: 'Unfamiliar',
      },
      {
        timestampUtc: ago(3 * 60 * minute + minute),
        outcome: 'Info',
        method: 'System',
        title: 'Account unlocked',
        sourceIp: 'internal',
        locationFlag: 'None',
      },
      {
        timestampUtc: ago(2 * 24 * 60 * minute + 60 * minute),
        outcome: 'Success',
        method: 'Passkey',
        title: 'Signed in with a passkey',
        sourceIp: '2001:db8::7',
        locationFlag: 'FirstSeen',
      },
      {
        timestampUtc: '2026-01-02T03:04:05.678Z',
        outcome: 'Success',
        method: 'RecoveryCode',
        title: 'Signed in with a recovery code',
        sourceIp: '2001:db8::7',
        locationFlag: 'None',
      },
      {
        timestampUtc: '2025-12-31T23:59:59Z',
        outcome: 'Success',
        method: 'StepUp',
        title: 'Re-authenticated for a sensitive action',
        sourceIp: '2001:db8::7',
        locationFlag: 'None',
      },
      {
        timestampUtc: '2025-12-30T08:00:00Z',
        outcome: 'Failed',
        method: 'TwoFactor',
        title: 'Two-factor code rejected',
        sourceIp: '2001:db8::7',
        locationFlag: 'None',
      },
    ],
  };
}

test('Sign-in history shows its empty state, and an unfamiliar-location attempt leads with the notice', async ({
  page,
}) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(new URL(request.url()).pathname);
  });

  await test.step('an account with no sign-in events sees the empty state, without figures or notice', async () => {
    await page.route(
      '**/api/ui/sign-ins',
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(emptySignIns),
        });
      },
      { times: 1 },
    );
    await page.getByRole('link', { name: 'Sign-in history', exact: true }).click();
    // The Legacy partial's own element, not a host element carrying its class.
    await expect(page.locator('div.empty-state')).toHaveCount(1);
    await expect(page.locator('.empty-state')).toHaveCount(1);
    await expect(page.locator('.empty-state h3')).toHaveText('No sign-in history yet');
    await expect(page.locator('.empty-state p')).toHaveText(
      'As you sign in — with your password, a passkey, or a recovery code — each attempt is recorded here ' +
        'so you can review it later and spot anything unfamiliar.',
    );
    await expect(page.locator('.stat-grid')).toHaveCount(0);
    await expect(signInNotice(page)).toHaveCount(0);
    await expect(page.getByRole('table')).toHaveCount(0);
    await expect(signInVerdict(page)).toHaveCount(0);
    await expect(page.locator('style, [style]')).toHaveCount(0);
  });

  const unfamiliar = unfamiliarSignIns();

  await test.step('an unfamiliar-location attempt raises the verdict, the alert figure and the notice', async () => {
    await page.route(
      '**/api/ui/sign-ins',
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(unfamiliar),
        });
      },
      { times: 1 },
    );
    await page.getByRole('link', { name: 'My activity', exact: true }).click();
    await expect(page).toHaveURL(/\/Activity$/);
    await page.getByRole('link', { name: 'Sign-in history', exact: true }).click();
    await expect(page).toHaveURL(/\/SignIns$/);
    await expect(signInEvents(page)).toHaveCount(7);

    await expect(signInVerdict(page)).toHaveText('Needs a look');
    await expect(
      page.getByText(
        "2 failed attempts came from places you've never signed in from · only your own sign-ins appear here.",
        { exact: true },
      ),
    ).toBeVisible();
    const utcTooltip = expect.stringMatching(/^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d UTC$/);
    expect(await statCards(page)).toEqual([
      {
        classes: '',
        value: '3',
        label: 'Successful sign-ins',
        detail: 'last 2d ago',
        tooltip: utcTooltip,
      },
      {
        classes: 'attention',
        value: '3',
        label: 'Rejected attempts',
        detail: 'last just now',
        tooltip: utcTooltip,
      },
      {
        classes: 'alert',
        value: '2',
        label: 'From unfamiliar places',
        detail: 'failed, never signed in from there',
        tooltip: null,
      },
      {
        classes: '',
        value: '2',
        label: 'Distinct locations',
        detail: 'by source address',
        tooltip: null,
      },
    ]);
    // The notice leads, then the figures, then the events.
    const notice = signInNotice(page);
    const top = async (box: Promise<{ y: number } | null>) => (await box)!.y;
    const order = [
      await top(notice.boundingBox()),
      // eslint-disable-next-line playwright/no-raw-locators -- The figures strip has no role or name of its own.
      await top(page.locator('.stat-grid').boundingBox()),
      await top(page.getByRole('table').boundingBox()),
    ];
    expect(order).toEqual([...order].sort((a, b) => a - b));
    await expect(notice.getByRole('heading', { level: 2 })).toHaveText(
      "Don't recognise these attempts?",
    );
    await expect(
      notice.getByText(
        "Someone tried to sign in from a place you've never signed in from. Your account wasn't opened from there " +
          "— every attempt failed — but it's worth locking things down: change your password and sign out of every device.",
        { exact: true },
      ),
    ).toBeVisible();
    expect(
      await notice
        .getByRole('link')
        .evaluateAll((links) =>
          links.map((link) => [link.textContent!.trim(), link.getAttribute('href')]),
        ),
    ).toEqual([
      ['Review signed-in devices', '/Sessions'],
      ['Change password', '/Profile'],
    ]);

    const row = (
      when: string,
      title: string,
      method: string,
      outcome: string,
      address: string,
      location = '',
      tooltip: unknown = utcTooltip,
    ) => ({ when, tooltip, title, method, outcome, address, location });
    expect(await signInRows(page)).toEqual([
      row(
        'just now',
        'Sign-in attempt failed',
        'Password + 2FA',
        'Failed',
        '203.0.113.50',
        'Unfamiliar location',
      ),
      row(
        '5m ago',
        'Account locked after repeated failures',
        'System',
        'Blocked',
        '203.0.113.50',
        'Unfamiliar location',
      ),
      row('3h ago', 'Account unlocked', 'System', 'Info', 'internal'),
      row(
        '2d ago',
        'Signed in with a passkey',
        'Passkey',
        'Success',
        '2001:db8::7',
        'New location',
      ),
      row(
        '2026-01-02',
        'Signed in with a recovery code',
        'Recovery code',
        'Success',
        '2001:db8::7',
        '',
        '2026-01-02 03:04:05 UTC',
      ),
      row(
        '2025-12-31',
        'Re-authenticated for a sensitive action',
        'Step-up',
        'Success',
        '2001:db8::7',
        '',
        '2025-12-31 23:59:59 UTC',
      ),
      row(
        '2025-12-30',
        'Two-factor code rejected',
        'Two-factor',
        'Failed',
        '2001:db8::7',
        '',
        '2025-12-30 08:00:00 UTC',
      ),
    ]);
    await expect(page.locator('style, [style]')).toHaveCount(0);
    expect(documents).toEqual([]);
  });
  await page.unrouteAll({ behavior: 'wait' });
});

test('on a phone Sign-in history events are cards, and nothing scrolls sideways at 300 or 2000px', async ({
  page,
}) => {
  await page.context().addCookies(administratorCookies);
  for (const width of [375, 300, 2000]) {
    await test.step(`${width}px`, async () => {
      await page.setViewportSize({ width, height: 900 });
      await page.route(
        '**/api/ui/sign-ins',
        (route) => route.fulfill({ status: 200, json: unfamiliarSignIns() }),
        { times: 1 },
      );
      await page.goto('/SignIns');
      await expect(signInEvents(page)).toHaveCount(7);
      await expect
        .poll(() => page.evaluate(() => document.documentElement.scrollWidth))
        .toBe(width);
    });
  }
  // Back at 375px the rows reflow into cards: the outcome pill sits under the title.
  await page.setViewportSize({ width: 375, height: 900 });
  const first = signInEvents(page).first();
  const title = (await first.getByText('Sign-in attempt failed', { exact: true }).boundingBox())!;
  const outcome = (await first.getByText('Failed', { exact: true }).boundingBox())!;
  expect(outcome.y).toBeGreaterThan(title.y + title.height);
  await page.unrouteAll({ behavior: 'wait' });
});

test("visual parity: Sign-in history's empty state keeps its icon", async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  const fulfilSignIns = (body: SignIns) =>
    page.route(
      '**/api/ui/sign-ins',
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(body),
        });
      },
      { times: 1 },
    );

  await test.step('the empty state keeps its icon', async () => {
    await fulfilSignIns(emptySignIns);
    await page.getByRole('link', { name: 'Sign-in history', exact: true }).click();
    await expect(
      page.getByRole('heading', { name: 'No sign-in history yet', exact: true }),
    ).toBeVisible();
    expect(await svgShapes(page.locator('.empty-state .empty-icon svg'))).toEqual([
      [
        'rect:x=4,y=10,width=16,height=10,rx=2',
        'path:d=M8 10V7a4 4 0 0 1 8 0v3',
        'circle:cx=12,cy=15,r=1.4',
      ],
    ]);
  });
  await page.unrouteAll({ behavior: 'wait' });
});

test('the sidebar and palette open Sign-in history in-app, and its notice opens Profile on the same session', async ({
  page,
}) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  const session = (await page.context().cookies()).find(
    (cookie) => cookie.name === 'EclipsVault.Session',
  );
  const documents: string[] = [];
  const signInRequests: string[] = [];
  page.on('request', (request) => {
    if (/\/Account\/(Login|Totp|EnrollTotp)/i.test(new URL(request.url()).pathname))
      signInRequests.push(request.url());
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(new URL(request.url()).pathname);
  });

  await test.step('the sidebar link keeps the document and marks Sign-in history and its group active', async () => {
    await page.getByRole('link', { name: 'Sign-in history', exact: true }).click();
    await expect(page).toHaveURL(/\/SignIns$/);
    await expect(page).toHaveTitle('Sign-in history — EclipsVault');
    await expect(page.getByRole('heading', { name: 'Sign-in history', exact: true })).toBeVisible();
    await expect(signInEvents(page).first()).toBeVisible();
    await expect(page.locator('.sidenav a.active')).toHaveText('Sign-in history');
    await expect(page.locator('.sidenav a.active')).toHaveAttribute('aria-current', 'page');
    await expect(page.locator('.sidenav summary.has-active')).toHaveText('You');
    await expect(page.locator('.sidenav details:has(a.active)')).toHaveAttribute('open', '');
    expect(documents).toEqual([]);
  });

  await test.step('the palette opens Sign-in history in place', async () => {
    await page.getByRole('link', { name: 'My access', exact: true }).click();
    await expect(page).toHaveURL(/\/Access$/);
    await openPalette(page);
    await page.locator('.palette-input').fill('sign-in history');
    await expect(page.locator('.palette-item')).toHaveText([/Sign-in history/]);
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/SignIns$/);
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(signInEvents(page).first()).toBeVisible();
    expect(documents).toEqual([]);
  });

  // The notice needs an unfamiliar-location attempt, which no real sign-in in this stack can make.
  const now = new Date().toISOString();
  const unfamiliar: SignIns = {
    summary: {
      successCount: 0,
      failedCount: 1,
      suspiciousCount: 1,
      distinctLocations: 1,
      lastSuccessUtc: null,
      lastFailedUtc: now,
      needsAttention: true,
    },
    events: [
      {
        timestampUtc: now,
        outcome: 'Failed',
        method: 'Password',
        title: 'Sign-in attempt failed',
        sourceIp: '203.0.113.50',
        locationFlag: 'Unfamiliar',
      },
    ],
  };
  await test.step('Change password opens Profile inside Angular on the same session', async () => {
    await page.route(
      '**/api/ui/sign-ins',
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(unfamiliar),
        });
      },
      { times: 1 },
    );
    await page.goto('/SignIns');
    await expect(signInNotice(page)).toBeVisible();
    documents.length = 0;
    await signInNotice(page).getByRole('link', { name: 'Change password', exact: true }).click();
    await expect(page).toHaveURL(/\/Profile$/);
    await expect(page.getByRole('heading', { name: 'Profile', exact: true })).toBeVisible();
    expect(documents).toEqual([]);
    expect(signInRequests).toEqual([]);
    expect(
      (await page.context().cookies()).find((cookie) => cookie.name === 'EclipsVault.Session')
        ?.value,
    ).toBe(session?.value);
  });
});

test('a sign-in history failure shows a retry and the next request restores the timeline', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(administratorCookies);
  browserHealth.expectConsoleError({ text: /status of 503/, url: /\/api\/ui\/sign-ins$/ });
  await page.route(
    '**/api/ui/sign-ins',
    async (route) => {
      await route.fulfill({
        status: 503,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'audit_unavailable' }),
      });
    },
    { times: 1 },
  );
  await page.goto('/SignIns');
  await expect(page.getByRole('alert')).toHaveText(
    'Could not load your sign-in history. Reload the page to try again.',
  );
  await expect(page.getByRole('table')).toHaveCount(0);
  await expect(page.locator('.stat-grid')).toHaveCount(0);
  await page.getByRole('button', { name: 'Try again', exact: true }).click();
  await expect(signInEvents(page).first()).toBeVisible();
  await expect(page.locator('.stat-grid')).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(page.locator('style, [style]')).toHaveCount(0);
});

test('signed out, Sign-in history sends the browser to sign-in with its return URL', async ({
  page,
}) => {
  await test.step('Sign-in history retains its return URL', async () => {
    await page.goto('/SignIns');
    await expect(page).toHaveURL(new RegExp(`/Account/Login\\?ReturnUrl=%2FSignIns$`));
    await expect(page.getByLabel('Username')).toBeVisible();
  });
  await test.step('the Sign-in history alias retains its return URL', async () => {
    await page.goto('/SignIns/Index');
    await expect(page).toHaveURL(new RegExp(`/Account/Login\\?ReturnUrl=%2FSignIns%2FIndex$`));
    await expect(page.getByLabel('Username')).toBeVisible();
  });
});

test('Security checkup shows all-clear and at-risk postures with only actionable fixes', async ({
  page,
}) => {
  await page.context().addCookies(administratorCookies);
  await page.route(
    '**/api/ui/security',
    async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(allClear),
      });
    },
    { times: 1 },
  );
  await page.goto('/sEcUrItY/INDEX');
  await expect(page).toHaveTitle('Security checkup — EclipsVault');
  await expect(securityVerdict(page)).toHaveText('All good');
  await expect(
    page.getByText('All 4 checks pass. Everything here is about your account only.', {
      exact: true,
    }),
  ).toBeVisible();
  // All clear: no score tile, no list of checks to fix, no link to follow and no zero count.
  await expect(page.getByRole('img', { name: /^Security score / })).toHaveCount(0);
  await expect(checksNeedingYou(page)).toHaveCount(0);
  await expect(passingChecks(page).getByRole('link')).toHaveCount(0);
  await expect(page.getByRole('main').getByText('0', { exact: true })).toHaveCount(0);
  expect(await passingRows(page)).toEqual([
    { title: 'Two-step sign-in', detail: 'An authenticator app is protecting your sign-in.' },
    { title: 'Backup codes', detail: 'You have 10 one-time backup codes.' },
    { title: 'Passkey', detail: 'You have 1 passkey for phishing-resistant sign-in.' },
    { title: 'Signed-in devices', detail: 'Only this device is signed in.' },
  ]);
  await expect(page.locator('style, [style]')).toHaveCount(0);

  await page.route(
    '**/api/ui/security',
    async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(atRisk),
      });
    },
    { times: 1 },
  );
  await page.goto('/Security/Index');
  await expect(securityVerdict(page)).toHaveText('Action needed');
  await expect(
    page.getByText(
      "4 checks need you · next: Turn on an authenticator app so a stolen password isn't enough to get in.",
      { exact: true },
    ),
  ).toBeVisible();
  const tile = page.getByRole('img', {
    name: 'Security score 30 out of 100 — At risk',
    exact: true,
  });
  await expect(tile).toContainText('30 / 100');
  await expect(tile).toContainText('At risk');
  expect(await failingRows(page)).toEqual([
    {
      title: 'Two-step sign-in',
      status: 'Action needed',
      detail: 'Your account relies on a password alone.',
      recommendation: "Turn on an authenticator app so a stolen password isn't enough to get in.",
      fix: ['Set up two-step', '/Profile'],
    },
    {
      title: 'Backup codes',
      status: 'Recommended',
      detail: 'Backup codes become available once two-step sign-in is on.',
      recommendation: 'Turn on two-step sign-in, then generate a set of one-time backup codes.',
      fix: ['Generate codes', '/Profile'],
    },
    {
      title: 'Passkey',
      status: 'Recommended',
      detail: "You haven't added a passkey yet.",
      recommendation:
        "Add a passkey for sign-in that can't be phished, even if your password leaks.",
      fix: ['Add a passkey', '/Profile'],
    },
    {
      title: 'Signed-in devices',
      status: 'Recommended',
      detail: '4 devices are signed in to your account.',
      recommendation: "Review the list and sign out anything you don't recognise.",
      fix: ['Review devices', '/Sessions'],
    },
  ]);
  await expect(passingChecks(page)).toHaveCount(0);
  await expect(page.locator('style, [style]')).toHaveCount(0);

  // None suppresses the link even for a non-secured check; the server remains the source of prose.
  const noFix: typeof atRisk = {
    ...atRisk,
    checks: atRisk.checks.map((check) => ({ ...check, fix: 'None' })),
  };
  await page.route(
    '**/api/ui/security',
    async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(noFix),
      });
    },
    { times: 1 },
  );
  await page.goto('/Security');
  await expect(checksNeedingYou(page).getByRole('listitem')).toHaveCount(4);
  await expect(checksNeedingYou(page).getByRole('link')).toHaveCount(0);
});

test('Security checkup at risk fits 300px with its longest copy', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  await page.setViewportSize({ width: 300, height: 900 });
  await page.route('**/api/ui/security', (route) => route.fulfill({ json: atRisk }), {
    times: 1,
  });
  await page.goto('/Security');
  await expect(checksNeedingYou(page).getByRole('listitem')).toHaveCount(4);
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(300);
});

test('the sidebar and palette open Security checkup in-app, and its fixes open Profile on the same session', async ({
  page,
}) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  const session = (await page.context().cookies()).find(
    (cookie) => cookie.name === 'EclipsVault.Session',
  );
  const documents: string[] = [];
  const signInRequests: string[] = [];
  page.on('request', (request) => {
    if (/\/Account\/(Login|Totp|EnrollTotp)/i.test(new URL(request.url()).pathname))
      signInRequests.push(request.url());
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(new URL(request.url()).pathname);
  });
  await page.getByRole('link', { name: 'Security checkup', exact: true }).click();
  await expect(page).toHaveURL(/\/Security$/);
  await expect(page).toHaveTitle('Security checkup — EclipsVault');
  await expect(
    page.getByRole('list', { name: /^(Needs you|Passing)$/ }).getByRole('listitem'),
  ).toHaveCount(4);
  await expect(page.locator('.sidenav a.active')).toHaveText('Security checkup');
  await expect(page.locator('.sidenav a.active')).toHaveAttribute('aria-current', 'page');
  await expect(page.locator('.sidenav summary.has-active')).toHaveText('You');
  await expect(page.locator('.sidenav details:has(a.active)')).toHaveAttribute('open', '');
  expect(documents).toEqual([]);

  await page.getByRole('link', { name: 'My access', exact: true }).click();
  await expect(page).toHaveURL(/\/Access$/);
  await openPalette(page);
  await page.locator('.palette-input').fill('security checkup');
  await expect(page.locator('.palette-item')).toHaveText([/Security checkup/]);
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/Security$/);
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(
    page.getByRole('list', { name: /^(Needs you|Passing)$/ }).getByRole('listitem'),
  ).toHaveCount(4);
  expect(documents).toEqual([]);

  await test.step(`Set up two-step opens Profile inside Angular on the same session`, async () => {
    await page.route(
      '**/api/ui/security',
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(atRisk),
        });
      },
      { times: 1 },
    );
    await page.goto('/Security');
    await expect(checksNeedingYou(page).getByRole('link')).toHaveCount(4);
    documents.length = 0;
    await checksNeedingYou(page)
      .getByRole('link', { name: 'Set up two-step', exact: true })
      .click();
    await expect(page).toHaveURL(/\/Profile$/);
    await expect(page.getByRole('heading', { name: 'Profile', exact: true })).toBeVisible();
    expect(documents).toEqual([]);
    expect(signInRequests).toEqual([]);
    expect(
      (await page.context().cookies()).find((cookie) => cookie.name === 'EclipsVault.Session')
        ?.value,
    ).toBe(session?.value);
  });
  await test.step(`Generate codes opens Profile inside Angular on the same session`, async () => {
    await page.route(
      '**/api/ui/security',
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(atRisk),
        });
      },
      { times: 1 },
    );
    await page.goto('/Security');
    await expect(checksNeedingYou(page).getByRole('link')).toHaveCount(4);
    documents.length = 0;
    await checksNeedingYou(page).getByRole('link', { name: 'Generate codes', exact: true }).click();
    await expect(page).toHaveURL(/\/Profile$/);
    await expect(page.getByRole('heading', { name: 'Profile', exact: true })).toBeVisible();
    expect(documents).toEqual([]);
    expect(signInRequests).toEqual([]);
    expect(
      (await page.context().cookies()).find((cookie) => cookie.name === 'EclipsVault.Session')
        ?.value,
    ).toBe(session?.value);
  });
  await test.step(`Add a passkey opens Profile inside Angular on the same session`, async () => {
    await page.route(
      '**/api/ui/security',
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(atRisk),
        });
      },
      { times: 1 },
    );
    await page.goto('/Security');
    await expect(checksNeedingYou(page).getByRole('link')).toHaveCount(4);
    documents.length = 0;
    await checksNeedingYou(page).getByRole('link', { name: 'Add a passkey', exact: true }).click();
    await expect(page).toHaveURL(/\/Profile$/);
    await expect(page.getByRole('heading', { name: 'Profile', exact: true })).toBeVisible();
    expect(documents).toEqual([]);
    expect(signInRequests).toEqual([]);
    expect(
      (await page.context().cookies()).find((cookie) => cookie.name === 'EclipsVault.Session')
        ?.value,
    ).toBe(session?.value);
  });
});

test('Security checkup loads, reports a fetch failure, and retries in place', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(administratorCookies);
  browserHealth.expectConsoleError({ text: /status of 503/, url: /\/api\/ui\/security$/ });
  let release!: () => void;
  const pending = new Promise<void>((resolve) => {
    release = resolve;
  });
  await page.route(
    '**/api/ui/security',
    async (route) => {
      await pending;
      await route.fulfill({
        status: 503,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'audit_unavailable' }),
      });
    },
    { times: 1 },
  );
  const requested = page.waitForRequest('**/api/ui/security');
  await page.goto('/Security');
  await requested;
  await expect(page.getByRole('status')).toHaveText('Loading your security checkup…');
  await expect(page.getByRole('list', { name: /^(Needs you|Passing)$/ })).toHaveCount(0);
  release();
  await expect(page.getByRole('alert')).toHaveText(
    'Could not load your security checkup. Reload the page to try again.',
  );
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(request.url());
  });
  await page.getByRole('button', { name: 'Try again', exact: true }).click();
  await expect(
    page.getByRole('list', { name: /^(Needs you|Passing)$/ }).getByRole('listitem'),
  ).toHaveCount(4);
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(page.locator('style, [style]')).toHaveCount(0);
  expect(documents).toEqual([]);
});

test('signed out, Security checkup sends the browser to sign-in with its return URL', async ({
  page,
}) => {
  await test.step('Security checkup retains its return URL', async () => {
    await page.goto('/Security');
    await expect(page).toHaveURL(new RegExp(`/Account/Login\\?ReturnUrl=%2FSecurity$`));
    await expect(page.getByLabel('Username')).toBeVisible();
  });
  await test.step('the Security checkup alias retains its return URL', async () => {
    await page.goto('/Security/Index');
    await expect(page).toHaveURL(new RegExp(`/Account/Login\\?ReturnUrl=%2FSecurity%2FIndex$`));
    await expect(page.getByLabel('Username')).toBeVisible();
  });
});

test('a session revoked from another browser signs in again and returns to My access', async ({
  page,
  browserHealth,
}) => {
  // A fresh sign-in budget, then three sign-ins, each of which may wait for a new authenticator step.
  test.setTimeout(330_000);
  expect(authenticatorSecret).not.toBe('');
  // Every credential POST in this suite comes from one address, and the vault allows 11 per
  // 120-second window: its six, with the earlier tests' seven, would exceed it.
  clearSignInBudget();

  // Every session but the first browser's, as Signed-in devices shows them to it.
  let othersOfFirst: string[] = [];
  await test.step('the first browser signs in and opens the Angular UI', async () => {
    await signIn(page);
    othersOfFirst = await otherSessionIds(page);
    await page.goto('/Access/Index');
    await expect(page.locator('.access-matrix tbody tr')).toHaveCount(4);
  });

  const second = await (await browserHealth.newContext()).newPage();
  await test.step("a second browser signs the first browser's session out from Signed-in devices", async () => {
    await signIn(second);
    const first = (await otherSessionIds(second)).filter((id) => !othersOfFirst.includes(id));
    expect(first).toHaveLength(1);
    await new SignedInDevices(second).signOutSession(first[0]!);
    await second.getByRole('alertdialog').getByRole('button', { name: 'Continue' }).click();
    await expect(second.getByRole('status')).toHaveText(
      'That session was signed out — it loses access on its next request.',
    );
  });

  await test.step("the first browser's next UI API action sends it to sign-in, returning to My access", async () => {
    // The browser logs the refused request as a console error; that one error is the point of this step.
    browserHealth.expectConsoleError({ text: /status of 401/, url: /\/api\/ui\/access$/ });
    const refused = page.waitForResponse(
      (response) => new URL(response.url()).pathname === '/api/ui/access',
    );
    await page.getByRole('link', { name: 'My access', exact: true }).click();
    const response = await refused;
    expect(response.status()).toBe(401);
    expect(response.headers()['content-type']).toBe('application/problem+json');
    expect(response.headers()['cache-control']).toContain('no-store');
    await expect(page).toHaveURL(/\/Account\/Login\?ReturnUrl=%2FAccess$/);
    await expect(page.getByLabel('Username')).toBeVisible();
    await page.getByLabel('Username').fill('vault-admin');
    await page.getByLabel('Password', { exact: true }).fill(adminPassword());
    await page.getByRole('button', { name: 'Continue', exact: true }).click();
    await expect(page).toHaveURL(/\/Account\/Totp\?ReturnUrl=%2FAccess$/);
    await page
      .getByLabel('Authenticator code')
      .fill(await freshTotp(authenticatorSecret, lastSignInStep));
    lastSignInStep = currentTotpStep();
    await page.getByRole('button', { name: 'Verify and sign in' }).click();
    await expect(page).toHaveURL(/\/Access$/);
    await expect(page.locator('.access-matrix tbody tr')).toHaveCount(4);
  });

  await test.step("the production image serves none of the integration tests' test-only endpoints", async () => {
    // Signed in, so a missing endpoint answers 404 rather than asking for a session.
    expect((await second.request.get('/api/ui/test-only/throw/unhandled')).status()).toBe(404);
    expect((await second.request.get('/test-only/throw/unhandled')).status()).toBe(404);
  });
});

test('a stale-session problem ends the real session through protected sign-out before sign-in', async ({
  page,
  context,
  browserHealth,
}) => {
  await context.addCookies(standardCookies);
  await page.goto('/Access/Index');
  await expect(page.locator('.access-matrix tbody tr')).toHaveCount(4);
  const session = (await context.cookies()).find((cookie) => cookie.name === 'EclipsVault.Session');
  expect(session).toBeDefined();
  const xsrf = (await context.cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
  expect(xsrf).toBeDefined();
  // Exhaust the real credential budget deliberately, instead of depending on how many earlier
  // sign-ins happen to land in this rate window. Empty models cannot lock out an account.
  // API requests generate no browser console errors; the shared browser-health guard stays strict.
  expect(await exhaustCredentialBudget(page, xsrf!.value)).toBe(429);
  // Integration tests exercise real protected cookies with missing claims. Here the transport
  // supplies that contract so a production browser must perform the real CSRF-protected sign-out.
  browserHealth.expectConsoleError({ text: /status of 401/, url: /\/api\/ui\/access$/ });
  await page.route(
    '**/api/ui/access',
    async (route) => {
      await route.fulfill({
        status: 401,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'stale_session' }),
      });
    },
    { times: 1 },
  );
  const logout = page.waitForRequest(
    (request) =>
      request.method() === 'POST' && new URL(request.url()).pathname === '/Account/Logout',
  );
  const logoutResponse = page.waitForResponse(
    (response) =>
      response.request().method() === 'POST' &&
      new URL(response.url()).pathname === '/Account/Logout',
  );
  await page.getByRole('link', { name: 'My access', exact: true }).click();
  const sent = await logout;
  // headers() omits Cookie. /api/ui/me issues a new request token after the grid is visible,
  // including on the navigation that ends the session, and Angular copies the cookie it sees
  // when it builds this sign-out. The header has to match the cookie on this same request.
  const xsrfAtSignOut = decodeURIComponent(
    (await sent.allHeaders())['cookie']
      ?.split(';')
      .map((part) => part.trim())
      .find((part) => part.startsWith('XSRF-TOKEN='))
      ?.slice('XSRF-TOKEN='.length) ?? '',
  );
  expect(xsrfAtSignOut).not.toBe('');
  expect(sent.headers()['requestverificationtoken']).toBe(xsrfAtSignOut);
  expect((await logoutResponse).status()).toBe(302);
  await expect(page).toHaveURL(/\/Account\/Login$/);
  expect((await context.cookies()).some((cookie) => cookie.name === 'EclipsVault.Session')).toBe(
    false,
  );
  const replay = await browserHealth.newContext();
  await replay.addCookies([session!]);
  expect((await replay.request.get('/api/ui/access')).status()).toBe(401);
});

test('a real passkey follows the server redirect for local and external return targets', async ({
  page,
  context,
}) => {
  test.setTimeout(180_000);
  await context.addCookies(administratorCookies);
  const cdp = await context.newCDPSession(page);
  await cdp.send('WebAuthn.enable');
  const { authenticatorId } = await cdp.send('WebAuthn.addVirtualAuthenticator', {
    options: {
      protocol: 'ctap2',
      transport: 'internal',
      hasResidentKey: true,
      hasUserVerification: true,
      isUserVerified: true,
      automaticPresenceSimulation: true,
    },
  });
  try {
    await page.goto('/Profile');
    clearSignInBudget();
    await page.getByLabel('New passkey name').fill('issue53-browser');
    await page.getByRole('button', { name: 'Add a passkey', exact: true }).click();
    const profile = new ProfilePage(page);
    lastSignInStep = await profile.verify(adminPassword(), authenticatorSecret, lastSignInStep);
    await expect(page.getByText('Passkey added. You can now sign in with it.')).toBeVisible();
    // Adding a passkey ends the account's earlier sessions and gives this browser a fresh one. The
    // dependent suites reuse it: Signed-in devices provisions its own account with it, and Your data
    // downloads the administrator's own file.
    await context.storageState({
      path: `${process.env['E2E_TEST_RESULTS'] || 'test-results'}/sessions-administrator.json`,
    });

    await writeFile(
      `${process.env['E2E_TEST_RESULTS'] || 'test-results'}/users-administrator-authenticator.txt`,
      authenticatorSecret,
      { mode: 0o600 },
    );

    // Preparation and verification spend two requests; the four sign-ins spend eight more.
    // All ten fit this window's eleven-request budget.
    await test.step('a local return target keeps its query and fragment', async () => {
      // The previous pass's page may still be loading. Leave it first: a request it sends after the
      // cookies are cleared gets a 401, which the browser logs, and its redirect to sign-in races ours.
      await page.goto('about:blank');
      await context.clearCookies();
      await page.goto(`/Account/Login?ReturnUrl=${encodeURIComponent('/Access?view=mine#matrix')}`);
      await page.getByRole('button', { name: 'Sign in with a passkey', exact: true }).click();
      await expect(page).toHaveURL(new URL('/Access?view=mine#matrix', page.url()).href);
      expect(
        (await context.cookies()).some((cookie) => cookie.name === 'EclipsVault.Session'),
      ).toBe(true);
    });
    await test.step('an absolute external return target falls back to Secrets', async () => {
      // The previous pass's page may still be loading. Leave it first: a request it sends after the
      // cookies are cleared gets a 401, which the browser logs, and its redirect to sign-in races ours.
      await page.goto('about:blank');
      await context.clearCookies();
      await page.goto(`/Account/Login?ReturnUrl=${encodeURIComponent('https://outside.example/')}`);
      await page.getByRole('button', { name: 'Sign in with a passkey', exact: true }).click();
      await expect(page).toHaveURL(new URL('/Secrets', page.url()).href);
      expect(
        (await context.cookies()).some((cookie) => cookie.name === 'EclipsVault.Session'),
      ).toBe(true);
    });
    await test.step('a scheme-relative return target falls back to Secrets', async () => {
      // The previous pass's page may still be loading. Leave it first: a request it sends after the
      // cookies are cleared gets a 401, which the browser logs, and its redirect to sign-in races ours.
      await page.goto('about:blank');
      await context.clearCookies();
      await page.goto(`/Account/Login?ReturnUrl=${encodeURIComponent('//outside.example/')}`);
      await page.getByRole('button', { name: 'Sign in with a passkey', exact: true }).click();
      await expect(page).toHaveURL(new URL('/Secrets', page.url()).href);
      expect(
        (await context.cookies()).some((cookie) => cookie.name === 'EclipsVault.Session'),
      ).toBe(true);
    });
    await test.step('a backslash return target falls back to Secrets', async () => {
      // The previous pass's page may still be loading. Leave it first: a request it sends after the
      // cookies are cleared gets a 401, which the browser logs, and its redirect to sign-in races ours.
      await page.goto('about:blank');
      await context.clearCookies();
      await page.goto(`/Account/Login?ReturnUrl=${encodeURIComponent('/\\outside.example/')}`);
      await page.getByRole('button', { name: 'Sign in with a passkey', exact: true }).click();
      await expect(page).toHaveURL(new URL('/Secrets', page.url()).href);
      expect(
        (await context.cookies()).some((cookie) => cookie.name === 'EclipsVault.Session'),
      ).toBe(true);
    });
  } finally {
    await cdp.send('WebAuthn.removeVirtualAuthenticator', { authenticatorId });
    await cdp.detach();
  }
});
