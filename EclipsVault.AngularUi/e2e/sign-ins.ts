import type { Page, TestInfo } from '@playwright/test';
import type { paths } from '../src/app/ui-api/schema.generated';
import { utc } from './activity';
import { expect } from './fixtures';

export type SignIns =
  paths['/api/ui/sign-ins']['get']['responses'][200]['content']['application/json'];

/** One row of the events table as a person reads it, with the time's UTC tooltip. */
export interface SignInRow {
  when: string;
  tooltip: string | null;
  title: string;
  method: string;
  outcome: string;
  address: string;
  location: string;
}

/** One summary card: its modifier classes, value, label, and the detail line with its tooltip. */
export interface StatCard {
  classes: string;
  value: string;
  label: string;
  detail: string | null;
  tooltip: string | null;
}

/** The events table's body rows: the header row has column headers, not cells. */
export function signInEvents(page: Page) {
  return page.getByRole('row').filter({ has: page.getByRole('cell') });
}

/** The page's verdict pill. */
export function signInVerdict(page: Page) {
  return page.getByText(/^(Needs a look|Nothing unusual)$/);
}

/** The notice that leads the page when an attempt came from an unfamiliar place. */
export function signInNotice(page: Page) {
  return page.getByRole('region', { name: "Don't recognise these attempts?", exact: true });
}

// Cells follow the header order: When, Event (title, then method), Outcome, Address, Location.
export async function signInRows(page: Page): Promise<SignInRow[]> {
  return signInEvents(page).evaluateAll((rows) =>
    rows.map((row) => {
      const [when, event, outcome, address, location] = [...row.children];
      return {
        when: when!.textContent!.trim(),
        tooltip: when!.querySelector('time')!.getAttribute('title'),
        title: event!.children[0]!.textContent!.trim(),
        method: event!.children[1]!.textContent!.trim(),
        outcome: outcome!.textContent!.trim(),
        address: address!.textContent!.trim(),
        location: location!.textContent!.trim(),
      };
    }),
  );
}

export async function statCards(page: Page): Promise<StatCard[]> {
  return page.locator('.stat-grid > .stat-card').evaluateAll((cards) =>
    cards.map((card) => {
      const detail = card.querySelector('.stat-detail');
      return {
        classes: [...card.classList].filter((name) => name !== 'stat-card').join(' '),
        value: card.querySelector('.stat-value')!.textContent!.trim(),
        label: card.querySelector('.stat-label')!.textContent!.trim(),
        detail: detail ? detail.textContent!.trim() : null,
        tooltip: detail ? detail.getAttribute('title') : null,
      };
    }),
  );
}

/**
 * The administrator's real sign-ins in one theme, against literal expectations, with a screenshot.
 * The first test signed in twice with a password and authenticator code, from the proxy's one address.
 */
export async function verifySignInHistory(page: Page, info: TestInfo, theme: string) {
  await page
    .context()
    .addCookies([
      { name: 'EclipsVault.Theme', value: theme, url: new URL('/', page.url()).href, secure: true },
    ]);
  const served = await page.goto('/SignIns');
  // The Angular UI serves the page, not the Legacy Razor view at the same URL.
  expect(served?.headers()['content-security-policy']).toContain(
    "require-trusted-types-for 'script'",
  );
  await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
  await expect(page).toHaveTitle('Sign-in history — EclipsVault');
  await expect(page.getByRole('heading', { name: 'Sign-in history', exact: true })).toBeVisible();
  await expect(
    page.getByText('2 sign-ins and 0 rejected attempts · only your own sign-ins appear here.', {
      exact: true,
    }),
  ).toBeVisible();
  await expect(signInVerdict(page)).toHaveText('Nothing unusual');

  // Docker allocates the proxy's forwarded source address; every sign-in of this run comes from it.
  const access = await page.request.get('/api/ui/access');
  const sourceIp: string = (await access.json()).sourceIp;
  await expect(signInEvents(page)).toHaveCount(2);
  // Read the timestamps from the API itself: the body of the page's own fetch is not always still
  // held by the browser once the page has consumed it.
  const response = await page.request.get('/api/ui/sign-ins');
  expect(response.status()).toBe(200);
  const history: SignIns = await response.json();
  expect(history.events).toHaveLength(2);

  const recent = /^(just now|\d{1,2}m ago)$/;
  expect(await signInRows(page)).toEqual([
    {
      when: expect.stringMatching(recent),
      tooltip: utc(history.events[0]!.timestampUtc),
      title: 'Signed in',
      method: 'Password + 2FA',
      outcome: 'Success',
      address: sourceIp,
      location: '',
    },
    {
      when: expect.stringMatching(recent),
      tooltip: utc(history.events[1]!.timestampUtc),
      title: 'Signed in',
      method: 'Password + 2FA',
      outcome: 'Success',
      address: sourceIp,
      location: 'New location',
    },
  ]);
  expect(await statCards(page)).toEqual([
    {
      classes: '',
      value: '2',
      label: 'Successful sign-ins',
      detail: expect.stringMatching(/^last (just now|\d{1,2}m ago)$/),
      tooltip: utc(history.summary.lastSuccessUtc!),
    },
    { classes: '', value: '0', label: 'Rejected attempts', detail: null, tooltip: null },
    {
      classes: '',
      value: '0',
      label: 'From unfamiliar places',
      detail: 'failed, never signed in from there',
      tooltip: null,
    },
    {
      classes: '',
      value: '1',
      label: 'Distinct locations',
      detail: 'by source address',
      tooltip: null,
    },
  ]);
  // Only an attempt from an unfamiliar place raises the notice.
  await expect(signInNotice(page)).toHaveCount(0);
  await expect(page.locator('.empty-state')).toHaveCount(0);
  await expect(page.locator('style, [style]')).toHaveCount(0);

  await page.evaluate(() => document.fonts.ready);
  const path = info.outputPath(`administrator-${theme}-sign-ins.png`);
  await page.screenshot({ path, fullPage: true });
  await info.attach(`administrator-${theme}-sign-ins`, { path, contentType: 'image/png' });
}
