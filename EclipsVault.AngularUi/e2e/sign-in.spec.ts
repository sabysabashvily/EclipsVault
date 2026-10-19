import type { BrowserContext, Page } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { test, expect } from './fixtures';
import { type Activity, type ExpectedRow, feedIcons, feedRows, fulfilActivity, verifyMyActivity } from './activity';
import { createSecret, markDecoy, openPalette, paletteEntries, searchSecrets, sidebarPages } from './palette';
import { type SignIns, passwordIcon, signInRows, statCards, verifySignInHistory } from './sign-ins';
import { verifySidebar } from './sidebar';
import { freshTotp } from './totp';

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
  await page.getByLabel('Authenticator code').fill(await freshTotp(authenticatorSecret, lastSignInStep));
  lastSignInStep = Math.floor(Date.now() / 30_000);
  await page.getByRole('button', { name: 'Verify and sign in' }).click();
  await expect(page).toHaveURL(/\/Secrets$/);
}

/**
 * Waits for a new production throttle window. Every credential POST in this suite comes from one
 * address, and the vault allows 11 per clock-aligned 120-second window. A scenario that needs
 * several starts its own window instead of depending on how the earlier tests happened to time.
 */
async function freshThrottleWindow(page: Page): Promise<void> {
  const windowMilliseconds = 120_000;
  await page.waitForTimeout(windowMilliseconds - Date.now() % windowMilliseconds + 1_000);
}

/** The ids of the sessions Signed-in devices offers to sign out: every session but the current one. */
async function otherSessionIds(page: Page): Promise<string[]> {
  await page.goto('/Sessions');
  return page.locator('form input[name="id"]').evaluateAll((inputs) => inputs.map((input) => (input as HTMLInputElement).value));
}

test('sign in, lazy-load My access with Trusted Types, visit Legacy UI and return', async ({ page, context, browserHealth }) => {
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
  const enrollmentStep = Math.floor(Date.now() / 30_000);
  await page.getByRole('button', { name: 'Confirm enrollment' }).click();
  await expect(page).toHaveURL(/\/Secrets$/);

  await test.step('sign out through the Angular account menu, which ends the session on the server', async () => {
    const enrolled = (await context.cookies()).find((cookie) => cookie.name === 'EclipsVault.Session');
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
    const signOut = angular.waitForRequest((request) =>
      request.method() === 'POST' && new URL(request.url()).pathname === '/Account/Logout');
    await angular.getByRole('menuitem', { name: 'Sign out', exact: true }).click();
    expect((await signOut).headers()['requestverificationtoken']).toBe(xsrf?.value);
    await expect(angular).toHaveURL(/\/Account\/Login$/);
    await expect(angular.getByLabel('Username')).toBeVisible();
    expect((await other.cookies()).some((cookie) => cookie.name === 'EclipsVault.Session')).toBe(false);

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
  lastSignInStep = Math.floor(Date.now() / 30_000);
  await page.getByRole('button', { name: 'Verify and sign in' }).click();
  await expect(page).toHaveURL(/\/Secrets$/);

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
  const lazyAccess = page.waitForResponse(async (response) =>
    /\/ui\/chunk-[\w-]+\.js$/.test(response.url()) && (await response.text()).includes('What you can open right now'));
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

  const legacyDocument = page.waitForResponse((response) => response.request().isNavigationRequest());
  await page.getByRole('link', { name: 'Secrets', exact: true }).click();
  expect((await legacyDocument).headers()['content-security-policy']).not.toContain('trusted-types');
  await expect(page).toHaveURL(/\/Secrets$/);
  await expect(page.getByRole('heading', { name: 'Secrets', exact: true })).toBeVisible();
  await expect(page.locator('.sidebar')).toContainText('Vault Administrator');
  await page.getByText('Access', { exact: true }).click();
  await page.getByRole('link', { name: 'My access', exact: true }).click();
  await expect(page).toHaveURL(/\/Access$/);
  await expect(page.locator('.access-matrix tbody tr')).toHaveCount(4);
  expect(signInRequests).toEqual([]);
  expect((await context.cookies()).find((cookie) => cookie.name === 'EclipsVault.Session')?.value).toBe(session?.value);
  administratorCookies = await context.cookies();
});

test('My activity shows the administrator\'s own rows in both themes, and nothing past the end', async ({ page }, info) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  // The first test's own actions, newest first: the ordinary two-factor sign-in, then the sign-in
  // that confirming enrolment completes, then the enrolment itself.
  const rows: ExpectedRow[] = [
    { title: 'Signed in', category: 'Sign-in', accent: 'sev-muted' },
    { title: 'Signed in', category: 'Sign-in', accent: 'sev-muted' },
    { title: 'Set up your authenticator', category: 'Account', accent: 'sev-warn' },
  ];
  for (const theme of ['dark', 'light']) {
    await test.step(`${theme} theme`, async () => {
      await verifyMyActivity(page, info, theme, rows);
    });
  }

  await test.step('a page past the end offers the way back to the latest page, in-app', async () => {
    await page.goto('/Activity?page=999');
    await expect(page.locator('.panel > p.muted')).toHaveText('Nothing further back to show.');
    await expect(page.locator('.activity-feed, .pager, .empty-state')).toHaveCount(0);
    await expect(page.locator('style, [style]')).toHaveCount(0);
    const documents: string[] = [];
    page.on('request', (request) => {
      if (request.isNavigationRequest() && request.resourceType() === 'document') documents.push(request.url());
    });
    const latest = page.getByRole('link', { name: 'Back to latest', exact: true });
    await expect(latest).toHaveAttribute('href', '/Activity');
    await latest.click();
    await expect(page).toHaveURL(/\/Activity$/);
    await expect(page.locator('.activity-feed > li')).toHaveCount(rows.length);
    expect(documents).toEqual([]);
  });
});

test('Sign-in history shows the administrator\'s real sign-ins in both themes', async ({ page }, info) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  for (const theme of ['dark', 'light']) {
    await test.step(`${theme} theme`, async () => {
      await verifySignInHistory(page, info, theme);
    });
  }
});

test('the administrator and Standard sidebars match Legacy in both themes and navigate across UIs', async ({ page, browserHealth }, info) => {
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
    await standard.getByLabel('Confirmation code').fill(await freshTotp(await standard.locator('.secret-code').innerText()));
    await standard.getByRole('button', { name: 'Confirm enrollment' }).click();
    await expect(standard).toHaveURL(/\/Secrets$/);
  });
  await test.step('Standard navigation and visual comparison', async () => {
    await verifySidebar(standard, info, 'standard');
  });
  standardCookies = await standard.context().cookies();
});

test('both UIs share the theme, and the Angular UI applies it before it starts', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Secrets');
  await page.context().addCookies([{ name: 'EclipsVault.Theme', value: 'dark', url: new URL('/', page.url()).href, secure: true }]);
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
    expect((await page.context().cookies()).find((cookie) => cookie.name === 'EclipsVault.Theme')?.value).toBe('dark');
    await page.goto('/Secrets');
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  });
});

test('the ⌘K palette offers exactly the sidebar\'s pages, as the Legacy palette does, for an administrator and a Standard account', async ({ page, browserHealth }) => {
  if (!standardCookies.length) throw new Error('Runs after the sidebar test, which signs in the Standard account');
  await page.context().addCookies(administratorCookies);
  const standard = await (await browserHealth.newContext()).newPage();
  await standard.context().addCookies(standardCookies);

  for (const [account, browser, links] of [['administrator', page, 21], ['standard', standard, 14]] as const) {
    await test.step(`${account}: Legacy and Angular palettes list the same pages as the sidebar`, async () => {
      await browser.goto('/Secrets');
      const legacyChord = await browser.locator('.command-trigger kbd').textContent();
      await openPalette(browser);
      const legacy = await paletteEntries(browser);
      await browser.keyboard.press('Escape');

      await browser.goto('/Access');
      await expect(browser.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
      await expect(browser.locator('.sidebar > .command-trigger kbd')).toHaveText(legacyChord ?? '');
      expect(legacyChord).toMatch(/^(⌘K|Ctrl K)$/);
      const sidebar = await sidebarPages(browser);
      await openPalette(browser);
      const angular = await paletteEntries(browser);

      expect(angular).toEqual(sidebar.map((label) => ['Go to', label]));
      expect(angular).toEqual(legacy);
      expect(angular).toHaveLength(links);
      if (account === 'standard') {
        for (const admin of ['Users', 'Networks', 'Service accounts', 'Audit log', 'Notifications', 'Encryption keys', 'License']) {
          expect(sidebar).not.toContain(admin);
        }
      }
      await expect(browser.locator('style, [style]')).toHaveCount(0);
    });
  }
});

test('palette search lists only secrets the account could already list, never a decoy, and nothing for one character', async ({ page, browserHealth }) => {
  test.setTimeout(150_000);
  await page.context().addCookies(administratorCookies);

  await test.step('the administrator stores a Standard-visible secret, one above Standard clearance, and a decoy', async () => {
    await createSecret(page, 'Palette_Probe_Listed', 'Internal');
    await createSecret(page, 'Palette_Probe_Confidential', 'Confidential');
    // Visible to Standard in every attribute; only being a decoy keeps it out of every list.
    await createSecret(page, 'Palette_Probe_Decoy', 'Internal');
    markDecoy('Palette_Probe_Decoy');
  });

  await test.step('the administrator\'s search finds both real secrets and no decoy', async () => {
    await page.goto('/Access');
    await openPalette(page);
    expect(await searchSecrets(page, 'Palette_Probe')).toEqual({
      status: 200, names: ['Palette_Probe_Confidential', 'Palette_Probe_Listed'],
    });
    expect((await searchSecrets(page, 'Decoy')).names).toEqual([]);
  });

  const standard = await (await browserHealth.newContext()).newPage();
  await standard.context().addCookies(standardCookies);
  await standard.goto('/Access');
  await expect(standard.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();

  await test.step('a Standard account finds only the secret its clearance lists, and neither hidden one by name', async () => {
    await openPalette(standard);
    expect(await searchSecrets(standard, 'Palette_Probe')).toEqual({ status: 200, names: ['Palette_Probe_Listed'] });
    const listed = standard.locator('.palette-item').filter({ hasText: 'Palette_Probe_Listed' });
    await expect(listed.locator('.palette-meta')).toHaveText('GLOBAL · Development');
    await expect(listed.locator('.badge.sens-1')).toHaveText('Internal');
    expect((await searchSecrets(standard, 'Palette_Probe_Decoy')).names).toEqual([]);
    expect((await searchSecrets(standard, 'Palette_Probe_Confidential')).names).toEqual([]);
    await expect(standard.locator('.palette-empty')).toHaveText('Nothing matches that.');
  });

  await test.step('a one-character query asks the server nothing, and the server would answer nothing', async () => {
    const searches: string[] = [];
    standard.on('request', (request) => {
      if (new URL(request.url()).pathname === '/Secrets/Search') searches.push(request.url());
    });
    await standard.locator('.palette-input').fill('P');
    // Longer than the debounce, so a search that was going to be sent would have been.
    await standard.waitForTimeout(600);
    expect(searches).toEqual([]);
    expect((await paletteEntries(standard)).filter(([kind]) => kind === 'Secret')).toEqual([]);
    for (const q of ['P', 'p', '_', ' P ']) {
      const direct = await standard.request.get(`/Secrets/Search?q=${encodeURIComponent(q)}`, { headers: { Accept: 'application/json' } });
      expect(direct.status()).toBe(200);
      expect(await direct.json()).toEqual([]);
    }
  });

  await test.step('Enter opens the chosen secret in the Legacy UI', async () => {
    await searchSecrets(standard, 'Palette_Probe');
    await standard.keyboard.press('Enter');
    await expect(standard).toHaveURL(/\/Secrets\/Details\/[0-9a-f-]{36}$/);
    await expect(standard.getByRole('heading', { name: 'Palette_Probe_Listed', exact: true })).toBeVisible();
  });
});

test('the palette is driven from the keyboard, traps focus, and hands it back on Escape', async ({ page }) => {
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
    await expect(page.locator('.palette-input')).toHaveAttribute('aria-activedescendant', 'palette-option-2');
    await page.keyboard.press('ArrowUp');
    await expect(options.nth(1)).toHaveAttribute('aria-selected', 'true');

    for (const key of ['Tab', 'Shift+Tab', 'Tab']) {
      await page.keyboard.press(key);
      await expect(page.locator('.palette-input')).toBeFocused();
    }
    // A press inside the panel keeps the field focused; one on the backdrop dismisses it.
    await page.locator('.palette-hint').click();
    await expect(page.locator('.palette-input')).toBeFocused();
    await page.locator('.palette-backdrop').click({ position: { x: 5, y: 5 } });
    await expect(page.getByRole('dialog')).toHaveCount(0);
  });

  await test.step('Enter opens an Angular UI page in place, and a Legacy UI page with a page load', async () => {
    const documents: string[] = [];
    page.on('request', (request) => {
      if (request.isNavigationRequest() && request.resourceType() === 'document') documents.push(new URL(request.url()).pathname);
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
    expect(documents).toEqual(['/Sessions']);
  });
});

test('an ended session makes palette search report a failure, without a console error', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  // Only this browser forgets the session; the administrator's session itself stays valid for later tests.
  await page.context().clearCookies({ name: 'EclipsVault.Session' });

  await openPalette(page);
  const redirected = page.waitForResponse((response) => new URL(response.url()).pathname === '/Secrets/Search');
  await page.locator('.palette-input').fill('Palette_Probe');
  // /Secrets/Search is a Legacy UI path: no session is answered by the sign-in redirect, not a 401 problem.
  const answer = await redirected;
  expect(answer.status()).toBe(302);
  expect(answer.headers()['location']).toMatch(/\/Account\/Login\?ReturnUrl=/);
  await expect(page.getByRole('alert')).toHaveText(/Secret search failed/);
  await expect(page.locator('.palette-empty')).toHaveText('Nothing matches that.');
  await expect(page.locator('.palette-input')).toBeFocused();
});

test('an access failure shows a retry and the next request restores the live grid', async ({ page, browserHealth }) => {
  await page.context().addCookies(administratorCookies);
  browserHealth.expectConsoleError({ text: /status of 503/, url: /\/api\/ui\/access$/ });
  await page.route('**/api/ui/access', async (route) => {
    await route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ code: 'audit_unavailable' }) });
  }, { times: 1 });
  await page.goto('/Access');
  await expect(page.getByRole('alert')).toHaveText('Could not load your access. Reload the page to try again.');
  await expect(page.locator('.access-matrix')).toHaveCount(0);
  await page.getByRole('button', { name: 'Try again', exact: true }).click();
  await expect(page.locator('.access-matrix tbody tr')).toHaveCount(4);
  await expect(page.getByRole('alert')).toHaveCount(0);
});

test('My activity\'s empty state and pager match Legacy, and paging is in-app with back and forward refetching', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document') documents.push(new URL(request.url()).pathname);
  });

  await test.step('an account with no activity sees the empty state', async () => {
    const empty: Activity = { page: 1, pageSize: 25, hasMore: false, hasPrevious: false, notableCount: 0, items: [] };
    const requested = await fulfilActivity(page, { '': empty });
    await page.getByText('Security', { exact: true }).click();
    await page.getByRole('link', { name: 'My activity', exact: true }).click();
    // The Legacy partial's own element, not a host element carrying its class.
    await expect(page.locator('div.empty-state')).toHaveCount(1);
    await expect(page.locator('.empty-state')).toHaveCount(1);
    await expect(page.locator('.empty-state h3')).toHaveText('No activity yet');
    await expect(page.locator('.empty-state p')).toHaveText(
      'As you sign in, view and reveal secrets, and update your account, each action is recorded here so you can review it later.');
    expect(await page.locator('.empty-state .empty-icon svg').evaluate((icon) => [...icon.children].map((shape) => shape.getAttribute('d'))))
      .toEqual(['M9 4h6a1 1 0 0 1 1 1v1h2a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h2V5a1 1 0 0 1 1-1Z', 'M9 14l2 2 4-4']);
    await expect(page.locator('.activity-feed, .pager, .page-header > .badge')).toHaveCount(0);
    await expect(page.locator('.panel > p.muted')).toHaveCount(0);
    await expect(page.locator('style, [style]')).toHaveCount(0);
    expect(requested).toEqual(['']);
    await page.unrouteAll({ behavior: 'wait' });
  });

  const minute = 60_000;
  const now = Date.now();
  const ago = (milliseconds: number) => new Date(now - milliseconds).toISOString();
  const longName = 'Payments_Production_Database_Primary_Connection_String_Rotated_Quarterly';
  const first: Activity = { page: 1, pageSize: 25, hasMore: true, hasPrevious: false, notableCount: 2, items: [
    { timestampUtc: ago(20_000), category: 'Secrets', title: 'Revealed a secret value', severity: 'Notable', resourceName: longName, sourceIp: '203.0.113.7' },
    { timestampUtc: ago(5 * minute + 20_000), category: 'Security', title: 'A session was force-revoked', severity: 'Critical', resourceName: null, sourceIp: '203.0.113.8' },
    { timestampUtc: ago(3 * 60 * minute + minute), category: 'Authentication', title: 'Signed in', severity: 'Routine', resourceName: '   ', sourceIp: '203.0.113.7' },
    { timestampUtc: ago(2 * 24 * 60 * minute + 60 * minute), category: 'Sharing', title: 'Shared a secret', severity: 'Notable', resourceName: 'Shared_Key', sourceIp: '2001:db8::5' },
  ] };
  const second: Activity = { page: 2, pageSize: 25, hasMore: false, hasPrevious: true, notableCount: 0, items: [
    { timestampUtc: '2026-01-02T03:04:05.678Z', category: 'Administration', title: 'Created a user', severity: 'Routine', resourceName: null, sourceIp: '198.51.100.20' },
    { timestampUtc: '2025-12-31T23:59:59Z', category: 'Account', title: 'Changed your password', severity: 'Routine', resourceName: null, sourceIp: '198.51.100.20' },
    { timestampUtc: '2025-12-30T08:00:00Z', category: 'Automation', title: 'Issued an API key', severity: 'Routine', resourceName: null, sourceIp: 'internal' },
    { timestampUtc: '2025-12-29T08:00:00Z', category: 'Other', title: 'Some new action', severity: 'Routine', resourceName: null, sourceIp: 'internal' },
  ] };
  const requested = await fulfilActivity(page, { '': first, '1': first, '2': second });
  const pager = page.locator('nav.pager[aria-label="Activity pages"]');
  const pagerLinks = async () => pager.locator('a').evaluateAll((links) => links.map((link) => [link.textContent?.trim(), link.getAttribute('href')]));

  await test.step('the first page shows each row, its accent, icon and badges, and only the older link', async () => {
    await page.getByText('Access', { exact: true }).click();
    await page.getByRole('link', { name: 'My access', exact: true }).click();
    await page.getByText('Security', { exact: true }).click();
    await page.getByRole('link', { name: 'My activity', exact: true }).click();
    await expect(page).toHaveURL(/\/Activity$/);
    await expect(page.locator('.activity-feed > li')).toHaveCount(4);
    expect(await feedRows(page)).toEqual([
      { title: 'Revealed a secret value', badges: ['Secrets'], resource: { text: longName, tooltip: longName }, meta: 'from 203.0.113.7', time: 'just now', tooltip: expect.stringMatching(/^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d UTC$/), accent: 'sev-warn' },
      { title: 'A session was force-revoked', badges: ['Security'], resource: null, meta: 'from 203.0.113.8', time: '5m ago', tooltip: expect.any(String), accent: 'sev-critical' },
      { title: 'Signed in', badges: ['Sign-in'], resource: null, meta: 'from 203.0.113.7', time: '3h ago', tooltip: expect.any(String), accent: 'sev-muted' },
      { title: 'Shared a secret', badges: ['Sharing'], resource: { text: 'Shared_Key', tooltip: 'Shared_Key' }, meta: 'from 2001:db8::5', time: '2d ago', tooltip: expect.any(String), accent: 'sev-warn' },
    ]);
    expect(await page.locator('.feed-resource').first().evaluate((resource) => getComputedStyle(resource).textOverflow)).toBe('ellipsis');
    expect(await feedIcons(page)).toEqual([
      ['rect:x=4,y=10,width=16,height=10,rx=2', 'path:d=M8 10V7a4 4 0 0 1 8 0v3', 'circle:cx=12,cy=15,r=1.4'],
      ['path:d=M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6z', 'path:d=M9 12l2 2 4-4'],
      ['path:d=M15 3h3a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-3', 'path:d=M8 7l5 5-5 5M13 12H3'],
      ['circle:cx=18,cy=5,r=2.6', 'circle:cx=6,cy=12,r=2.6', 'circle:cx=18,cy=19,r=2.6', 'path:d=M8.3 10.7l7.4-4.4M8.3 13.3l7.4 4.4'],
    ]);
    // A critical entry's chip is solid danger, in white.
    expect(await page.locator('.sev-critical .feed-icon').evaluate((icon) => getComputedStyle(icon).color)).toBe('rgb(255, 255, 255)');
    await expect(page.locator('.page-header > .badge')).toHaveText('2 notable');
    await expect(pager.locator('.pager-page')).toHaveText('Page 1');
    expect(await pagerLinks()).toEqual([['Older →', '/Activity?page=2']]);
    await expect(page.locator('style, [style]')).toHaveCount(0);
  });

  await test.step('Older → changes ?page= in-app and shows the older page', async () => {
    await pager.getByRole('link', { name: 'Older →', exact: true }).click();
    await expect(page).toHaveURL(/\/Activity\?page=2$/);
    await expect(pager.locator('.pager-page')).toHaveText('Page 2');
    expect(await pagerLinks()).toEqual([['← Newer', '/Activity?page=1']]);
    await expect(page.locator('.page-header > .badge')).toHaveCount(0);
    expect(await feedRows(page)).toEqual([
      { title: 'Created a user', badges: ['Admin'], resource: null, meta: 'from 198.51.100.20', time: '2026-01-02', tooltip: '2026-01-02 03:04:05 UTC', accent: 'sev-muted' },
      { title: 'Changed your password', badges: ['Account'], resource: null, meta: 'from 198.51.100.20', time: '2025-12-31', tooltip: '2025-12-31 23:59:59 UTC', accent: 'sev-muted' },
      { title: 'Issued an API key', badges: ['Automation'], resource: null, meta: 'from internal', time: '2025-12-30', tooltip: '2025-12-30 08:00:00 UTC', accent: 'sev-muted' },
      { title: 'Some new action', badges: ['Other'], resource: null, meta: 'from internal', time: '2025-12-29', tooltip: '2025-12-29 08:00:00 UTC', accent: 'sev-muted' },
    ]);
    expect(await feedIcons(page)).toEqual([
      ['circle:cx=12,cy=12,r=3', 'path:d=M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M18.4 5.6l-2.1 2.1M7.7 16.3l-2.1 2.1'],
      ['circle:cx=12,cy=8,r=3.4', 'path:d=M5 20c.7-3.7 3-5.5 7-5.5s6.3 1.8 7 5.5'],
      ['rect:x=5,y=7,width=14,height=12,rx=2', 'path:d=M9 3v4M15 3v4M9 12h.01M15 12h.01M9 16h6'],
      ['circle:cx=12,cy=12,r=9', 'path:d=M12 8h.01M11 12h1v4h1'],
    ]);
  });

  await test.step('back and forward move between the visited pages and refetch each', async () => {
    await page.goBack();
    await expect(page).toHaveURL(/\/Activity$/);
    await expect(pager.locator('.pager-page')).toHaveText('Page 1');
    await expect(page.locator('.feed-title').first()).toHaveText('Revealed a secret value');
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

test('the sidebar and palette open My activity in-app, and its Profile link loads the Legacy UI on the same session', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  const session = (await page.context().cookies()).find((cookie) => cookie.name === 'EclipsVault.Session');
  const documents: string[] = [];
  const signInRequests: string[] = [];
  page.on('request', (request) => {
    if (/\/Account\/(Login|Totp|EnrollTotp)/i.test(new URL(request.url()).pathname)) signInRequests.push(request.url());
    if (request.isNavigationRequest() && request.resourceType() === 'document') documents.push(new URL(request.url()).pathname);
  });

  await test.step('the sidebar link keeps the document and marks My activity and its group active', async () => {
    await page.getByText('Security', { exact: true }).click();
    await page.getByRole('link', { name: 'My activity', exact: true }).click();
    await expect(page).toHaveURL(/\/Activity$/);
    await expect(page).toHaveTitle('My activity — EclipsVault');
    await expect(page.getByRole('heading', { name: 'My activity', exact: true })).toBeVisible();
    await expect(page.locator('.sidenav a.active')).toHaveText('My activity');
    await expect(page.locator('.sidenav a.active')).toHaveAttribute('aria-current', 'page');
    await expect(page.locator('.sidenav summary.has-active')).toHaveText('Security');
    await expect(page.locator('.sidenav details:has(a.active)')).toHaveAttribute('open', '');
    expect(documents).toEqual([]);
  });

  await test.step('the palette opens My activity in place', async () => {
    await page.getByText('Access', { exact: true }).click();
    await page.getByRole('link', { name: 'My access', exact: true }).click();
    await expect(page).toHaveURL(/\/Access$/);
    await openPalette(page);
    await page.locator('.palette-input').fill('my activity');
    await expect(page.locator('.palette-item')).toHaveText([/My activity/]);
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/Activity$/);
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page.locator('.activity-feed > li').first()).toBeVisible();
    expect(documents).toEqual([]);
  });

  await test.step('the Profile link is a full load into the Legacy UI on the same session', async () => {
    const legacyDocument = page.waitForResponse((response) => response.request().isNavigationRequest());
    await page.getByRole('link', { name: 'Change your password or sign out everywhere', exact: true }).click();
    expect((await legacyDocument).headers()['content-security-policy']).not.toContain('trusted-types');
    await expect(page).toHaveURL(/\/Profile$/);
    await expect(page.getByRole('heading', { name: 'Profile', exact: true })).toBeVisible();
    expect(documents).toEqual(['/Profile']);
    expect(signInRequests).toEqual([]);
    expect((await page.context().cookies()).find((cookie) => cookie.name === 'EclipsVault.Session')?.value).toBe(session?.value);
  });
});

test('an activity failure shows a retry and the next request restores the feed', async ({ page, browserHealth }) => {
  await page.context().addCookies(administratorCookies);
  browserHealth.expectConsoleError({ text: /status of 503/, url: /\/api\/ui\/activity$/ });
  await page.route('**/api/ui/activity', async (route) => {
    await route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ code: 'audit_unavailable' }) });
  }, { times: 1 });
  await page.goto('/Activity');
  await expect(page.getByRole('alert')).toHaveText('Could not load your activity. Reload the page to try again.');
  await expect(page.locator('.activity-feed')).toHaveCount(0);
  await page.getByRole('button', { name: 'Try again', exact: true }).click();
  await expect(page.locator('.activity-feed > li').first()).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(page.locator('style, [style]')).toHaveCount(0);
});

test('signed out, My activity sends the browser to sign-in with its return URL', async ({ page }) => {
  for (const [path, returnUrl] of [['/Activity', '%2FActivity'], ['/Activity/Index', '%2FActivity%2FIndex']] as const) {
    await page.goto(path);
    await expect(page).toHaveURL(new RegExp(`/Account/Login\\?ReturnUrl=${returnUrl}$`));
    await expect(page.getByLabel('Username')).toBeVisible();
  }
});

test('Sign-in history\'s empty state and an unfamiliar-location attempt match Legacy', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document') documents.push(new URL(request.url()).pathname);
  });

  await test.step('an account with no sign-in events sees the empty state, without summary or callout', async () => {
    const empty: SignIns = {
      summary: { successCount: 0, failedCount: 0, suspiciousCount: 0, distinctLocations: 0, lastSuccessUtc: null, lastFailedUtc: null, needsAttention: false },
      events: [],
    };
    await page.route('**/api/ui/sign-ins', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(empty) });
    }, { times: 1 });
    await page.getByText('Security', { exact: true }).click();
    await page.getByRole('link', { name: 'Sign-in history', exact: true }).click();
    // The Legacy partial's own element, not a host element carrying its class.
    await expect(page.locator('div.empty-state')).toHaveCount(1);
    await expect(page.locator('.empty-state')).toHaveCount(1);
    await expect(page.locator('.empty-state h3')).toHaveText('No sign-in history yet');
    await expect(page.locator('.empty-state p')).toHaveText(
      'As you sign in — with your password, a passkey, or a recovery code — each attempt is recorded here ' +
      'so you can review it later and spot anything unfamiliar.');
    expect(await page.locator('.empty-state .empty-icon svg').evaluate((icon) =>
      [...icon.children].map((shape) => `${shape.tagName}:${[...shape.attributes].map((a) => `${a.name}=${a.value}`).join(',')}`)))
      .toEqual(['rect:x=4,y=10,width=16,height=10,rx=2', 'path:d=M8 10V7a4 4 0 0 1 8 0v3', 'circle:cx=12,cy=15,r=1.4']);
    await expect(page.locator('.stat-grid, .callout-warn, .activity-feed, .page-header > .badge')).toHaveCount(0);
    await expect(page.locator('style, [style]')).toHaveCount(0);
  });

  const minute = 60_000;
  const now = Date.now();
  const ago = (milliseconds: number) => new Date(now - milliseconds).toISOString();
  const unfamiliar: SignIns = {
    summary: {
      successCount: 3, failedCount: 3, suspiciousCount: 2, distinctLocations: 2,
      lastSuccessUtc: ago(2 * 24 * 60 * minute + 60 * minute), lastFailedUtc: ago(20_000), needsAttention: true,
    },
    events: [
      { timestampUtc: ago(20_000), outcome: 'Failed', method: 'Password', title: 'Sign-in attempt failed', sourceIp: '203.0.113.50', locationFlag: 'Unfamiliar' },
      { timestampUtc: ago(5 * minute + 20_000), outcome: 'Blocked', method: 'System', title: 'Account locked after repeated failures', sourceIp: '203.0.113.50', locationFlag: 'Unfamiliar' },
      { timestampUtc: ago(3 * 60 * minute + minute), outcome: 'Info', method: 'System', title: 'Account unlocked', sourceIp: 'internal', locationFlag: 'None' },
      { timestampUtc: ago(2 * 24 * 60 * minute + 60 * minute), outcome: 'Success', method: 'Passkey', title: 'Signed in with a passkey', sourceIp: '2001:db8::7', locationFlag: 'FirstSeen' },
      { timestampUtc: '2026-01-02T03:04:05.678Z', outcome: 'Success', method: 'RecoveryCode', title: 'Signed in with a recovery code', sourceIp: '2001:db8::7', locationFlag: 'None' },
      { timestampUtc: '2025-12-31T23:59:59Z', outcome: 'Success', method: 'StepUp', title: 'Re-authenticated for a sensitive action', sourceIp: '2001:db8::7', locationFlag: 'None' },
      { timestampUtc: '2025-12-30T08:00:00Z', outcome: 'Failed', method: 'TwoFactor', title: 'Two-factor code rejected', sourceIp: '2001:db8::7', locationFlag: 'None' },
    ],
  };

  await test.step('an unfamiliar-location attempt raises the badge, the alert card, the header badge and the callout', async () => {
    await page.route('**/api/ui/sign-ins', async (route) => {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(unfamiliar) });
    }, { times: 1 });
    await page.getByRole('link', { name: 'My activity', exact: true }).click();
    await expect(page).toHaveURL(/\/Activity$/);
    await page.getByRole('link', { name: 'Sign-in history', exact: true }).click();
    await expect(page).toHaveURL(/\/SignIns$/);
    await expect(page.locator('.activity-feed > li')).toHaveCount(7);

    const header = page.locator('.page-header > .badge');
    await expect(header).toHaveText('2 to review');
    await expect(header).toHaveClass('badge tone-critical');
    await expect(header).toHaveAttribute('title', "Failed attempts from a location you've never signed in from");
    const utcTooltip = expect.stringMatching(/^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d UTC$/);
    expect(await statCards(page)).toEqual([
      { classes: '', value: '3', label: 'Successful sign-ins', detail: 'last 2d ago', tooltip: utcTooltip },
      { classes: 'attention', value: '3', label: 'Rejected attempts', detail: 'last just now', tooltip: utcTooltip },
      { classes: 'alert', value: '2', label: 'From unfamiliar places', detail: 'failed, never signed in from there', tooltip: null },
      { classes: '', value: '2', label: 'Distinct locations', detail: 'by source address', tooltip: null },
    ]);
    // The highlight and the alert come from the ported stat-card rules.
    expect(await page.locator('.stat-card.attention, .stat-card.alert').evaluateAll((cards) =>
      cards.map((card) => getComputedStyle(card).borderTopColor))).toEqual(['rgba(255, 214, 10, 0.35)', 'rgba(255, 69, 58, 0.4)']);

    // The summary, then the callout, then the timeline, as on the Legacy page.
    expect(await page.locator('section.panel').evaluateAll((panels) => panels.map((panel) =>
      `${panel.className} > ${panel.firstElementChild!.className}`)))
      .toEqual(['panel > stat-grid', 'panel callout-warn > panel-header', 'panel > activity-feed']);
    const callout = page.locator('section.callout-warn');
    await expect(callout.getByRole('heading', { level: 2 })).toHaveText("Don't recognise these attempts?");
    await expect(callout.locator('p.muted')).toHaveText(
      "Someone tried to sign in from a place you've never signed in from. Your account wasn't opened from there " +
      "— every attempt failed — but it's worth locking things down: change your password and sign out of every device.");
    expect(await callout.locator('.button-row > a').evaluateAll((links) =>
      links.map((link) => [link.textContent!.trim(), link.getAttribute('href'), link.className])))
      .toEqual([['Review signed-in devices', '/Sessions', 'button primary'], ['Change password', '/Profile', 'button']]);

    expect(await signInRows(page)).toEqual([
      { title: 'Sign-in attempt failed', badges: [['Failed', 'tone-warn'], ['Password + 2FA', 'tone-muted'], ['Unfamiliar location', 'tone-danger']],
        meta: 'from 203.0.113.50', time: 'just now', tooltip: utcTooltip, accent: 'sev-warn' },
      { title: 'Account locked after repeated failures', badges: [['Blocked', 'tone-danger'], ['System', 'tone-muted'], ['Unfamiliar location', 'tone-danger']],
        meta: 'from 203.0.113.50', time: '5m ago', tooltip: utcTooltip, accent: 'sev-critical' },
      { title: 'Account unlocked', badges: [['Info', 'tone-muted'], ['System', 'tone-muted']],
        meta: 'from internal', time: '3h ago', tooltip: utcTooltip, accent: '' },
      { title: 'Signed in with a passkey', badges: [['Success', 'tone-ok'], ['Passkey', 'tone-muted'], ['New location', 'tone-muted']],
        meta: 'from 2001:db8::7', time: '2d ago', tooltip: utcTooltip, accent: '' },
      { title: 'Signed in with a recovery code', badges: [['Success', 'tone-ok'], ['Recovery code', 'tone-muted']],
        meta: 'from 2001:db8::7', time: '2026-01-02', tooltip: '2026-01-02 03:04:05 UTC', accent: '' },
      { title: 'Re-authenticated for a sensitive action', badges: [['Success', 'tone-ok'], ['Step-up', 'tone-muted']],
        meta: 'from 2001:db8::7', time: '2025-12-31', tooltip: '2025-12-31 23:59:59 UTC', accent: '' },
      { title: 'Two-factor code rejected', badges: [['Failed', 'tone-warn'], ['Two-factor', 'tone-muted']],
        meta: 'from 2001:db8::7', time: '2025-12-30', tooltip: '2025-12-30 08:00:00 UTC', accent: 'sev-warn' },
    ]);
    const system = ['circle:cx=12,cy=12,r=3', 'path:d=M12 4v2M12 18v2M4 12h2M18 12h2M6.3 6.3l1.4 1.4M16.3 16.3l1.4 1.4M17.7 6.3l-1.4 1.4M7.7 16.3l-1.4 1.4'];
    expect(await feedIcons(page)).toEqual([
      passwordIcon,
      system,
      system,
      ['rect:x=3,y=11,width=18,height=10,rx=2', 'path:d=M7 11V8a5 5 0 0 1 9-3', 'circle:cx=12,cy=16,r=1.4'],
      ['rect:x=3,y=5,width=18,height=14,rx=2', 'path:d=M7 9h4M7 13h6M15 9h2'],
      ['path:d=M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6z', 'path:d=M12 8v4M12 15h.01'],
      ['rect:x=6,y=10,width=12,height=9,rx=1.6', 'path:d=M9 10V7.5a3 3 0 0 1 6 0V10'],
    ]);
    // A blocked event's chip is solid danger, in white.
    expect(await page.locator('.sev-critical .feed-icon').evaluate((icon) => getComputedStyle(icon).color)).toBe('rgb(255, 255, 255)');
    await expect(page.locator('style, [style]')).toHaveCount(0);
    expect(documents).toEqual([]);
  });
  await page.unrouteAll({ behavior: 'wait' });
});

test('the sidebar and palette open Sign-in history in-app, and its callout links load the Legacy UI on the same session', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  await page.goto('/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  const session = (await page.context().cookies()).find((cookie) => cookie.name === 'EclipsVault.Session');
  const documents: string[] = [];
  const signInRequests: string[] = [];
  page.on('request', (request) => {
    if (/\/Account\/(Login|Totp|EnrollTotp)/i.test(new URL(request.url()).pathname)) signInRequests.push(request.url());
    if (request.isNavigationRequest() && request.resourceType() === 'document') documents.push(new URL(request.url()).pathname);
  });

  await test.step('the sidebar link keeps the document and marks Sign-in history and its group active', async () => {
    await page.getByText('Security', { exact: true }).click();
    await page.getByRole('link', { name: 'Sign-in history', exact: true }).click();
    await expect(page).toHaveURL(/\/SignIns$/);
    await expect(page).toHaveTitle('Sign-in history — EclipsVault');
    await expect(page.getByRole('heading', { name: 'Sign-in history', exact: true })).toBeVisible();
    await expect(page.locator('.activity-feed > li').first()).toBeVisible();
    await expect(page.locator('.sidenav a.active')).toHaveText('Sign-in history');
    await expect(page.locator('.sidenav a.active')).toHaveAttribute('aria-current', 'page');
    await expect(page.locator('.sidenav summary.has-active')).toHaveText('Security');
    await expect(page.locator('.sidenav details:has(a.active)')).toHaveAttribute('open', '');
    expect(documents).toEqual([]);
  });

  await test.step('the palette opens Sign-in history in place', async () => {
    await page.getByText('Access', { exact: true }).click();
    await page.getByRole('link', { name: 'My access', exact: true }).click();
    await expect(page).toHaveURL(/\/Access$/);
    await openPalette(page);
    await page.locator('.palette-input').fill('sign-in history');
    await expect(page.locator('.palette-item')).toHaveText([/Sign-in history/]);
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/SignIns$/);
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page.locator('.activity-feed > li').first()).toBeVisible();
    expect(documents).toEqual([]);
  });

  // The callout needs an unfamiliar-location attempt, which no real sign-in in this stack can make.
  const now = new Date().toISOString();
  const unfamiliar: SignIns = {
    summary: { successCount: 0, failedCount: 1, suspiciousCount: 1, distinctLocations: 1, lastSuccessUtc: null, lastFailedUtc: now, needsAttention: true },
    events: [{ timestampUtc: now, outcome: 'Failed', method: 'Password', title: 'Sign-in attempt failed', sourceIp: '203.0.113.50', locationFlag: 'Unfamiliar' }],
  };
  for (const [name, path, heading] of [['Review signed-in devices', '/Sessions', 'Signed-in devices'], ['Change password', '/Profile', 'Profile']] as const) {
    await test.step(`${name} is a full load into the Legacy UI on the same session`, async () => {
      await page.route('**/api/ui/sign-ins', async (route) => {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(unfamiliar) });
      }, { times: 1 });
      await page.goto('/SignIns');
      await expect(page.locator('.callout-warn')).toBeVisible();
      documents.length = 0;
      const legacyDocument = page.waitForResponse((response) => response.request().isNavigationRequest());
      await page.locator('.callout-warn').getByRole('link', { name, exact: true }).click();
      expect((await legacyDocument).headers()['content-security-policy']).not.toContain('trusted-types');
      await expect(page).toHaveURL(new RegExp(`${path}$`));
      await expect(page.getByRole('heading', { name: heading, exact: true })).toBeVisible();
      expect(documents).toEqual([path]);
      expect(signInRequests).toEqual([]);
      expect((await page.context().cookies()).find((cookie) => cookie.name === 'EclipsVault.Session')?.value).toBe(session?.value);
    });
  }
});

test('a sign-in history failure shows a retry and the next request restores the timeline', async ({ page, browserHealth }) => {
  await page.context().addCookies(administratorCookies);
  browserHealth.expectConsoleError({ text: /status of 503/, url: /\/api\/ui\/sign-ins$/ });
  await page.route('**/api/ui/sign-ins', async (route) => {
    await route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ code: 'audit_unavailable' }) });
  }, { times: 1 });
  await page.goto('/SignIns');
  await expect(page.getByRole('alert')).toHaveText('Could not load your sign-in history. Reload the page to try again.');
  await expect(page.locator('.activity-feed, .stat-grid')).toHaveCount(0);
  await page.getByRole('button', { name: 'Try again', exact: true }).click();
  await expect(page.locator('.activity-feed > li').first()).toBeVisible();
  await expect(page.locator('.stat-grid')).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
  await expect(page.locator('style, [style]')).toHaveCount(0);
});

test('signed out, Sign-in history sends the browser to sign-in with its return URL', async ({ page }) => {
  for (const [path, returnUrl] of [['/SignIns', '%2FSignIns'], ['/SignIns/Index', '%2FSignIns%2FIndex']] as const) {
    await page.goto(path);
    await expect(page).toHaveURL(new RegExp(`/Account/Login\\?ReturnUrl=${returnUrl}$`));
    await expect(page.getByLabel('Username')).toBeVisible();
  }
});

test('a session revoked from another browser signs in again and returns to My access', async ({ page, browserHealth }) => {
  // A fresh throttle window, then three sign-ins, each of which may wait for a new authenticator step.
  test.setTimeout(330_000);
  if (!authenticatorSecret) throw new Error('Runs after the sign-in test, which enrolls the authenticator');
  // Its six credential POSTs, with the earlier tests' seven, would exceed one window's budget.
  await freshThrottleWindow(page);

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
    await second.locator(`form:has(input[name="id"][value="${first[0]}"])`).getByRole('button', { name: 'Sign out' }).click();
    await second.getByRole('alertdialog').getByRole('button', { name: 'Continue' }).click();
    await expect(second.getByText('That session was signed out')).toBeVisible();
  });

  await test.step("the first browser's next UI API action sends it to sign-in, returning to My access", async () => {
    // The browser logs the refused request as a console error; that one error is the point of this step.
    browserHealth.expectConsoleError({ text: /status of 401/, url: /\/api\/ui\/access$/ });
    const refused = page.waitForResponse((response) => new URL(response.url()).pathname === '/api/ui/access');
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
    await page.getByLabel('Authenticator code').fill(await freshTotp(authenticatorSecret, lastSignInStep));
    lastSignInStep = Math.floor(Date.now() / 30_000);
    await page.getByRole('button', { name: 'Verify and sign in' }).click();
    await expect(page).toHaveURL(/\/Access$/);
    await expect(page.locator('.access-matrix tbody tr')).toHaveCount(4);
  });

  await test.step('the production image serves none of the integration tests\' test-only endpoints', async () => {
    // Signed in, so a missing endpoint answers 404 rather than asking for a session.
    expect((await second.request.get('/api/ui/test-only/throw/unhandled')).status()).toBe(404);
    expect((await second.request.get('/test-only/throw/unhandled')).status()).toBe(404);
  });
});

test('a stale-session problem ends the real session through protected sign-out before sign-in', async ({ page, context, browserHealth }) => {
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
  let credentialStatus = 0;
  for (let attempt = 0; attempt < 12; attempt++) {
    const response = await page.request.post('/Account/Login', {
      headers: { RequestVerificationToken: xsrf!.value },
    });
    credentialStatus = response.status();
    if (credentialStatus === 429) break;
    expect(credentialStatus).toBe(200);
  }
  expect(credentialStatus).toBe(429);
  // Integration tests exercise real protected cookies with missing claims. Here the transport
  // supplies that contract so a production browser must perform the real CSRF-protected sign-out.
  browserHealth.expectConsoleError({ text: /status of 401/, url: /\/api\/ui\/access$/ });
  await page.route('**/api/ui/access', async (route) => {
    await route.fulfill({ status: 401, contentType: 'application/problem+json', body: JSON.stringify({ code: 'stale_session' }) });
  }, { times: 1 });
  const logout = page.waitForRequest((request) => request.method() === 'POST' && new URL(request.url()).pathname === '/Account/Logout');
  const logoutResponse = page.waitForResponse((response) =>
    response.request().method() === 'POST' && new URL(response.url()).pathname === '/Account/Logout');
  await page.getByRole('link', { name: 'My access', exact: true }).click();
  expect((await logout).headers()['requestverificationtoken']).toBe(xsrf?.value);
  expect((await logoutResponse).status()).toBe(302);
  await expect(page).toHaveURL(/\/Account\/Login$/);
  expect((await context.cookies()).some((cookie) => cookie.name === 'EclipsVault.Session')).toBe(false);
  const replay = await browserHealth.newContext();
  await replay.addCookies([session!]);
  expect((await replay.request.get('/api/ui/access')).status()).toBe(401);
});

test('a real passkey follows the server redirect for local and external return targets', async ({ page, context }) => {
  test.setTimeout(180_000);
  await context.addCookies(administratorCookies);
  const cdp = await context.newCDPSession(page);
  await cdp.send('WebAuthn.enable');
  const { authenticatorId } = await cdp.send('WebAuthn.addVirtualAuthenticator', {
    options: {
      protocol: 'ctap2', transport: 'internal', hasResidentKey: true,
      hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true,
    },
  });
  try {
    await page.goto('/Profile');
    await page.getByLabel('New passkey name').fill('issue53-browser');
    await page.getByRole('button', { name: 'Add a passkey', exact: true }).click();
    await expect(page.getByText('Passkey added. You can now sign in with it.')).toBeVisible();

    // Each passkey attempt below consumes its ordinary two credential POSTs.
    await freshThrottleWindow(page);
    for (const [returnUrl, expected] of [
      ['/Access?view=mine#matrix', '/Access?view=mine#matrix'],
      ['https://outside.example/', '/Secrets'],
      ['//outside.example/', '/Secrets'],
      ['/\\outside.example/', '/Secrets'],
    ]) {
      await context.clearCookies();
      await page.goto(`/Account/Login?ReturnUrl=${encodeURIComponent(returnUrl!)}`);
      await page.getByRole('button', { name: 'Sign in with a passkey', exact: true }).click();
      await expect(page).toHaveURL(new URL(expected!, page.url()).href);
      expect((await context.cookies()).some((cookie) => cookie.name === 'EclipsVault.Session')).toBe(true);
    }
  } finally {
    await cdp.send('WebAuthn.removeVirtualAuthenticator', { authenticatorId });
    await cdp.detach();
  }
});
