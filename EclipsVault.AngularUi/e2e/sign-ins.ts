import type { Page, TestInfo } from '@playwright/test';
import type { paths } from '../src/app/ui-api/schema.generated';
import { feedIcons, feedRows, utc } from './activity';
import { expect } from './fixtures';

export type SignIns =
  paths['/api/ui/sign-ins']['get']['responses'][200]['content']['application/json'];

/** One timeline row as a person reads it: each badge with its tone, and the accent class it carries. */
export interface SignInRow {
  title: string;
  badges: [text: string, tone: string][];
  meta: string;
  time: string;
  tooltip: string | null;
  accent: string;
}

/** One summary card: its modifier classes, value, label, and the detail line with its tooltip. */
export interface StatCard {
  classes: string;
  value: string;
  label: string;
  detail: string | null;
  tooltip: string | null;
}

/** The method icon the Legacy page drew for a password sign-in, as `feedIcons` reports it. */
export const passwordIcon = [
  'path:d=M15 3h3a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-3',
  'path:d=M8 7l5 5-5 5M13 12H3',
];

/** The feed rows, each badge paired with its tone. A sign-in names no resource. */
export async function signInRows(page: Page): Promise<SignInRow[]> {
  const rows = await feedRows(page);
  const tones = await page
    .locator('.activity-feed > li')
    .evaluateAll((items) =>
      items.map((item) =>
        [...item.querySelectorAll('.feed-title-row > .badge')].map((badge) =>
          [...badge.classList].filter((name) => name !== 'badge').join(' '),
        ),
      ),
    );
  return rows.map(({ badges, resource, ...row }, i) => {
    expect(resource).toBeNull();
    return { ...row, badges: badges.map((text, j): [string, string] => [text, tones[i]![j]!]) };
  });
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
  await expect(page.locator('.page-sub')).toHaveText(
    'Every recent attempt to sign in to your account — successful and rejected — newest first. ' +
      "Only your own sign-ins appear here. An attempt from a location you've never signed in from " +
      "is flagged so you can spot anything you didn't do.",
  );

  // Docker allocates the proxy's forwarded source address; every sign-in of this run comes from it.
  const access = await page.request.get('/api/ui/access');
  const sourceIp: string = (await access.json()).sourceIp;
  await expect(page.locator('.activity-feed > li')).toHaveCount(2);
  // Read the timestamps from the API itself: the body of the page's own fetch is not always still
  // held by the browser once the page has consumed it.
  const response = await page.request.get('/api/ui/sign-ins');
  expect(response.status()).toBe(200);
  const history: SignIns = await response.json();
  expect(history.events).toHaveLength(2);

  const recent = /^(just now|\d{1,2}m ago)$/;
  expect(await signInRows(page)).toEqual([
    {
      title: 'Signed in',
      badges: [
        ['Success', 'tone-ok'],
        ['Password + 2FA', 'tone-muted'],
      ],
      meta: `from ${sourceIp}`,
      time: expect.stringMatching(recent),
      tooltip: utc(history.events[0]!.timestampUtc),
      accent: '',
    },
    {
      title: 'Signed in',
      badges: [
        ['Success', 'tone-ok'],
        ['Password + 2FA', 'tone-muted'],
        ['New location', 'tone-muted'],
      ],
      meta: `from ${sourceIp}`,
      time: expect.stringMatching(recent),
      tooltip: utc(history.events[1]!.timestampUtc),
      accent: '',
    },
  ]);
  expect(await feedIcons(page)).toEqual([passwordIcon, passwordIcon]);
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
  // Only an attempt from an unfamiliar place raises the header badge and the callout.
  await expect(page.locator('.page-header > .badge')).toHaveCount(0);
  await expect(page.locator('.callout-warn')).toHaveCount(0);
  await expect(page.locator('.empty-state')).toHaveCount(0);
  await expect(page.locator('style, [style]')).toHaveCount(0);

  // The cards and the routine accents resolve through the ported rules and the shared theme tokens.
  expect(
    await page
      .locator('.stat-grid')
      .evaluate((grid) => [
        getComputedStyle(grid).display,
        getComputedStyle(grid.querySelector('.stat-card')!).flexDirection,
      ]),
  ).toEqual(['grid', 'column']);
  const accents = await page
    .locator('.activity-feed')
    .evaluate((feed) =>
      [...feed.querySelectorAll('.feed-icon')].map((icon) => [
        getComputedStyle(icon).color,
        getComputedStyle(icon).backgroundColor,
      ]),
    );
  const routine =
    theme === 'dark'
      ? ['rgb(182, 188, 202)', 'rgb(13, 14, 19)']
      : ['rgb(74, 78, 88)', 'rgb(236, 238, 242)'];
  expect(accents).toEqual([routine, routine]);

  await page.evaluate(() => document.fonts.ready);
  const path = info.outputPath(`administrator-${theme}-sign-ins.png`);
  await page.screenshot({ path, fullPage: true });
  await info.attach(`administrator-${theme}-sign-ins`, { path, contentType: 'image/png' });
}
