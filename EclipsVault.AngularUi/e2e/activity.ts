import type { Page, Route, TestInfo } from '@playwright/test';
import type { paths } from '../src/app/ui-api/schema.generated';
import { expect } from './fixtures';

export type Activity = paths['/api/ui/activity']['get']['responses'][200]['content']['application/json'];
export type ActivityItem = Activity['items'][number];

/** One feed row as a person reads it, with the accent and icon chip it carries. */
export interface FeedRow {
  title: string;
  badges: string[];
  resource: { text: string; tooltip: string | null } | null;
  meta: string;
  time: string;
  tooltip: string | null;
  severity: string;
}

export async function feedRows(page: Page): Promise<FeedRow[]> {
  return page.locator('.activity-feed > li').evaluateAll((items) => items.map((item) => {
    const resource = item.querySelector('.feed-resource');
    const time = item.querySelector('time.feed-time')!;
    return {
      title: item.querySelector('.feed-title')!.textContent!.trim(),
      badges: [...item.querySelectorAll('.feed-title-row > .badge')].map((badge) => badge.textContent!.trim()),
      resource: resource ? { text: resource.textContent!.trim(), tooltip: resource.getAttribute('title') } : null,
      meta: item.querySelector('.feed-meta')!.textContent!.trim(),
      time: time.textContent!.trim(),
      tooltip: time.getAttribute('title'),
      severity: [...item.classList].filter((name) => name !== 'feed-item').join(' '),
    };
  }));
}

/** The geometry inside each row's icon chip, so a category's icon can be compared literally. */
export async function feedIcons(page: Page): Promise<string[][]> {
  return page.locator('.activity-feed .feed-icon svg').evaluateAll((icons) => icons.map((icon) =>
    [...icon.children].map((shape) => `${shape.tagName}:${[...shape.attributes].map((a) => `${a.name}=${a.value}`).join(',')}`)));
}

/** The UTC tooltip the Legacy page wrote for a timestamp, restated literally. */
export function utc(timestampUtc: string): string {
  const date = new Date(timestampUtc);
  const two = (value: number) => String(value).padStart(2, '0');
  return `${date.getUTCFullYear()}-${two(date.getUTCMonth() + 1)}-${two(date.getUTCDate())} ` +
    `${two(date.getUTCHours())}:${two(date.getUTCMinutes())}:${two(date.getUTCSeconds())} UTC`;
}

/** Answers `/api/ui/activity` requests from `bodies`, keyed by the request's `page` parameter. */
export async function fulfilActivity(page: Page, bodies: Record<string, Activity>): Promise<string[]> {
  const requested: string[] = [];
  await page.route((url) => url.pathname === '/api/ui/activity', async (route: Route) => {
    const key = new URL(route.request().url()).searchParams.get('page') ?? '';
    requested.push(key);
    const body = bodies[key];
    if (!body) throw new Error(`No fulfilled activity for page "${key}"`);
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  });
  return requested;
}

/**
 * The administrator's real rows in one theme, against literal expectations, with a screenshot.
 * `rows` lists each expected row's title and category badge, newest first.
 */
export async function verifyMyActivity(page: Page, info: TestInfo, theme: string, rows: [string, string, string][]) {
  await page.context().addCookies([{ name: 'EclipsVault.Theme', value: theme, url: new URL('/', page.url()).href, secure: true }]);
  const response = page.waitForResponse((r) => new URL(r.url()).pathname === '/api/ui/activity');
  const served = await page.goto('/Activity');
  // The Angular UI serves the page, not the Legacy Razor view at the same URL.
  expect(served?.headers()['content-security-policy']).toContain("require-trusted-types-for 'script'");
  const activity: Activity = await (await response).json();
  await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
  await expect(page).toHaveTitle('My activity — EclipsVault');
  await expect(page.getByRole('heading', { name: 'My activity', exact: true })).toBeVisible();
  await expect(page.locator('.page-sub')).toHaveText(
    "Everything you have done in EclipsVault, newest first — sign-ins, secret reveals, and account changes. " +
    "Only your own actions appear here. Spot something you don't recognise? " +
    'Change your password or sign out everywhere.');
  await expect(page.locator('.page-sub a')).toHaveAttribute('href', '/Profile');

  // Docker allocates the proxy's forwarded source address; every row of this session comes from it.
  const access = await page.request.get('/api/ui/access');
  const sourceIp: string = (await access.json()).sourceIp;
  await expect(page.locator('.activity-feed > li')).toHaveCount(rows.length);
  const shown = await feedRows(page);
  expect(shown.map(({ title, badges, severity }) => [title, badges[0], severity]))
    .toEqual(rows.map(([title, category, severity]) => [title, category, severity]));
  for (const [i, row] of shown.entries()) {
    expect(row.badges).toHaveLength(1);
    expect(row.resource).toBeNull();
    expect(row.meta).toBe(`from ${sourceIp}`);
    expect(row.time).toMatch(/^(just now|\d{1,2}m ago)$/);
    expect(row.tooltip).toBe(utc(activity.items[i]!.timestampUtc));
  }
  const notable = rows.filter(([, , severity]) => severity !== 'sev-muted').length;
  const badge = page.locator('.page-header > .badge');
  if (notable) {
    await expect(badge).toHaveText(`${notable} notable`);
    await expect(badge).toHaveAttribute('title', 'Notable or higher-severity entries on this page');
  } else {
    await expect(badge).toHaveCount(0);
  }
  await expect(page.locator('.pager')).toHaveCount(0);
  await expect(page.locator('style, [style]')).toHaveCount(0);

  // The accents resolve through the shared theme tokens.
  const accents = await page.locator('.activity-feed').evaluate((feed) =>
    [...feed.querySelectorAll('.feed-icon')].map((icon) => [getComputedStyle(icon).color, getComputedStyle(icon).backgroundColor]));
  const routine = theme === 'dark' ? ['rgb(182, 188, 202)', 'rgb(13, 14, 19)'] : ['rgb(74, 78, 88)', 'rgb(236, 238, 242)'];
  const warn = theme === 'dark' ? ['rgb(255, 214, 10)', 'rgba(255, 214, 10, 0.12)'] : ['rgb(138, 106, 0)', 'rgba(180, 140, 0, 0.15)'];
  expect(accents).toEqual(rows.map(([, , severity]) => (severity === 'sev-warn' ? warn : routine)));

  await page.evaluate(() => document.fonts.ready);
  const path = info.outputPath(`administrator-${theme}-activity.png`);
  await page.screenshot({ path, fullPage: true });
  await info.attach(`administrator-${theme}-activity`, { path, contentType: 'image/png' });
}
