import type { Locator, Page, Route, TestInfo } from '@playwright/test';
import type { paths } from '../src/app/ui-api/schema.generated';
import { expect } from './fixtures';

export type Activity =
  paths['/api/ui/activity']['get']['responses'][200]['content']['application/json'];
export type ActivityItem = Activity['items'][number];

/** One activity row as a person reads it. */
export interface FeedRow {
  title: string;
  kind: string;
  tone: string;
  resource: string | null;
  from: string;
  time: string;
}

/** A real row expected on My activity: its title, kind and status-pill tone. */
export interface ExpectedRow {
  title: string;
  category: string;
  tone: string;
}

export async function feedRows(page: Page): Promise<FeedRow[]> {
  return page.locator('.activity-table tbody tr').evaluateAll((rows) =>
    rows.map((row) => {
      const resource = row.querySelector('.activity-resource');
      const badge = row.querySelector('.badge');
      const time = row.querySelector('time');
      return {
        title: row.querySelector('.activity-title')?.textContent?.trim() ?? '',
        kind: badge?.textContent?.trim() ?? '',
        tone: [...(badge?.classList ?? [])].find((name) => name.startsWith('tone-')) ?? '',
        resource: resource?.textContent?.trim() ?? null,
        from: row.querySelector('td:last-child')?.textContent?.trim() ?? '',
        time: time?.textContent?.trim() ?? '',
      };
    }),
  );
}

/** The row that records `title` for the resource named `resource`. */
export function feedEntry(page: Page, title: string, resource: string): Locator {
  return page
    .getByRole('main')
    .getByRole('row')
    .filter({ hasText: title })
    .filter({ hasText: resource });
}

/** The geometry inside each matched SVG, one `tag:name=value,…` string per shape, for literal comparison. */
export async function svgShapes(icons: Locator): Promise<string[][]> {
  return icons.evaluateAll((svgs) =>
    svgs.map((icon) =>
      [...icon.children].map(
        (shape) =>
          `${shape.tagName}:${[...shape.attributes].map((a) => `${a.name}=${a.value}`).join(',')}`,
      ),
    ),
  );
}

/** The exact UTC time the activity table shows for a timestamp. */
export function utc(timestampUtc: string): string {
  const date = new Date(timestampUtc);
  const two = (value: number) => String(value).padStart(2, '0');
  return (
    `${date.getUTCFullYear()}-${two(date.getUTCMonth() + 1)}-${two(date.getUTCDate())} ` +
    `${two(date.getUTCHours())}:${two(date.getUTCMinutes())}:${two(date.getUTCSeconds())} UTC`
  );
}

/** Answers `/api/ui/activity` requests from `bodies`, keyed by the request's `page` parameter. */
export async function fulfilActivity(
  page: Page,
  bodies: Record<string, Activity>,
): Promise<string[]> {
  const requested: string[] = [];
  await page.route(
    (url) => url.pathname === '/api/ui/activity',
    async (route: Route) => {
      const key = new URL(route.request().url()).searchParams.get('page') ?? '';
      requested.push(key);
      const body = bodies[key];
      if (!body) throw new Error(`No fulfilled activity for page "${key}"`);
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(body),
      });
    },
  );
  return requested;
}

/**
 * The administrator's real rows in one theme, against literal expectations, with a screenshot.
 * `rows` lists the expected rows, newest first.
 */
export async function verifyMyActivity(
  page: Page,
  info: TestInfo,
  theme: string,
  rows: ExpectedRow[],
) {
  await page
    .context()
    .addCookies([
      { name: 'EclipsVault.Theme', value: theme, url: new URL('/', page.url()).href, secure: true },
    ]);
  const served = await page.goto('/Activity');
  // The Angular UI serves the page, not the Legacy Razor view at the same URL.
  expect(served?.headers()['content-security-policy']).toContain(
    "require-trusted-types-for 'script'",
  );
  await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
  await expect(page).toHaveTitle('My activity — EclipsVault');
  await expect(page.getByRole('heading', { name: 'My activity', exact: true })).toBeVisible();
  const notable = rows.filter(({ tone }) => tone !== 'muted').length;
  const notableSentence = notable === 0 ? '' : `${notable} notable on this page. `;
  await expect(page.locator('.page-sub')).toHaveText(
    `Newest first. ${notableSentence}Only your own actions are listed. This wasn't you? ` +
      'Change your password or sign out everywhere.',
  );
  await expect(page.locator('.page-sub a')).toHaveAttribute('href', '/Profile');
  await expect(page.getByText('Newest first', { exact: true }).last()).toBeVisible();

  // Docker allocates the proxy's forwarded source address; every row of this session comes from it.
  const access = await page.request.get('/api/ui/access');
  const sourceIp: string = (await access.json()).sourceIp;
  await expect(page.locator('.activity-table tbody tr')).toHaveCount(rows.length);
  // Read the rows' timestamps from the API itself: the body of the page's own fetch is not always
  // still held by the browser once the page has consumed it.
  const response = await page.request.get('/api/ui/activity');
  expect(response.status()).toBe(200);
  const activity: Activity = await response.json();
  expect(activity.items).toHaveLength(rows.length);
  const shown = await feedRows(page);
  expect(shown.map(({ title, kind, tone }) => ({ title, category: kind, tone }))).toEqual(
    rows.map((row) => ({ ...row, tone: `tone-${row.tone}` })),
  );
  for (const [i, row] of shown.entries()) {
    expect(row.resource).toBeNull();
    expect(row.from).toBe(sourceIp);
    expect(row.time).toBe(utc(activity.items[i]!.timestampUtc));
  }
  await expect(page.locator('.pager')).toHaveCount(0);
  await expect(page.locator('style, [style]')).toHaveCount(0);

  await page.evaluate(() => document.fonts.ready);
  const path = info.outputPath(`administrator-${theme}-activity.png`);
  await page.screenshot({ path, fullPage: true });
  await info.attach(`administrator-${theme}-activity`, { path, contentType: 'image/png' });
}
