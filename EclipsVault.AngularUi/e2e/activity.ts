import type { Locator, Page, Route, TestInfo } from '@playwright/test';
import type { paths } from '../src/app/ui-api/schema.generated';
import { expect } from './fixtures';

export type Activity =
  paths['/api/ui/activity']['get']['responses'][200]['content']['application/json'];
export type ActivityItem = Activity['items'][number];

/** One activity row as a person reads it. A routine row's severity is empty. */
export interface FeedRow {
  title: string;
  kind: string;
  severity: string;
  resource: string | null;
  from: string;
  time: string;
}

/** A real row expected on My activity: its title, kind and severity. */
export interface ExpectedRow {
  title: string;
  category: string;
  severity: string;
}

/** My activity: the signed-in account's own actions, newest first. */
export class MyActivity {
  readonly heading;
  readonly summary;
  readonly profileLink;
  readonly table;
  readonly rows;
  readonly pager;
  readonly pagerLinks;
  readonly pageNumber;
  readonly older;
  readonly newer;
  readonly empty;
  readonly emptyMessage;
  readonly nothingFurther;
  readonly backToLatest;
  readonly failure;
  readonly tryAgain;
  readonly inlineStyles;

  constructor(readonly page: Page) {
    const main = page.getByRole('main');
    this.heading = page.getByRole('heading', { name: 'My activity', exact: true });
    this.summary = main
      .getByRole('paragraph')
      .filter({ hasText: 'Only your own actions are listed.' });
    this.profileLink = page.getByRole('link', {
      name: 'Change your password or sign out everywhere',
      exact: true,
    });
    this.table = main.getByRole('table');
    this.rows = main.getByRole('row').filter({ has: page.getByRole('cell') });
    this.pager = page.getByRole('navigation', { name: 'Activity pages', exact: true });
    this.pagerLinks = this.pager.getByRole('link');
    this.pageNumber = this.pager.getByText(/^Page \d+$/);
    this.older = this.pager.getByRole('link', { name: 'Older →', exact: true });
    this.newer = this.pager.getByRole('link', { name: '← Newer', exact: true });
    this.empty = page.getByRole('heading', { name: 'No activity yet', exact: true });
    this.emptyMessage = page.getByText(
      'As you sign in, view and reveal secrets, and update your account, each action is recorded here so you can review it later.',
      { exact: true },
    );
    this.nothingFurther = page.getByText('Nothing further back to show.', { exact: true });
    this.backToLatest = page.getByRole('link', { name: 'Back to latest', exact: true });
    this.failure = main.getByRole('alert');
    this.tryAgain = page.getByRole('button', { name: 'Try again', exact: true });
    // eslint-disable-next-line playwright/no-raw-locators -- Inline styles have no role, and the CSP must never need to allow them.
    this.inlineStyles = page.locator('style, [style]');
  }

  async open(query = ''): Promise<void> {
    await this.page.goto(`/Activity${query}`);
  }

  /** Each row, column by column, as a person reads it. */
  async entries(): Promise<FeedRow[]> {
    return this.rows.evaluateAll((rows) =>
      rows.map((row) => {
        const [when, action, severity, kind, from] = [...row.children];
        // The action cell holds the title, then any resource name, as two spans with no role.
        const [title, resource] = [...(action?.children ?? [])];
        return {
          title: title?.textContent?.trim() ?? '',
          kind: kind?.textContent?.trim() ?? '',
          severity: severity?.textContent?.trim() ?? '',
          resource: resource?.textContent?.trim() ?? null,
          from: from?.textContent?.trim() ?? '',
          time: when?.textContent?.trim() ?? '',
        };
      }),
    );
  }
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
  const myActivity = new MyActivity(page);
  await page
    .context()
    .addCookies([
      { name: 'EclipsVault.Theme', value: theme, url: new URL('/', page.url()).href, secure: true },
    ]);
  await page.goto('/Activity');
  // eslint-disable-next-line playwright/no-raw-locators -- The theme is an attribute of the document element.
  await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
  await expect(page).toHaveTitle('My activity — EclipsVault');
  await expect(myActivity.heading).toBeVisible();
  const notable = rows.filter(({ severity }) => severity !== '').length;
  const notableSentence = notable === 0 ? '' : `${notable} notable on this page. `;
  await expect(myActivity.summary).toHaveText(
    `Newest first. ${notableSentence}Only your own actions are listed. This wasn't you? ` +
      'Change your password or sign out everywhere.',
  );
  await expect(myActivity.profileLink).toHaveAttribute('href', '/Profile');
  await expect(page.getByText('Newest first', { exact: true }).last()).toBeVisible();

  // Docker allocates the proxy's forwarded source address; every row of this session comes from it.
  const access = await page.request.get('/api/ui/access');
  const sourceIp: string = (await access.json()).sourceIp;
  await expect(myActivity.rows).toHaveCount(rows.length);
  // Read the rows' timestamps from the API itself: the body of the page's own fetch is not always
  // still held by the browser once the page has consumed it.
  const response = await page.request.get('/api/ui/activity');
  expect(response.status()).toBe(200);
  const activity: Activity = await response.json();
  expect(activity.items).toHaveLength(rows.length);
  const shown = await myActivity.entries();
  expect(shown.map(({ title, kind, severity }) => ({ title, category: kind, severity }))).toEqual(
    rows,
  );
  for (const [i, row] of shown.entries()) {
    expect(row.resource).toBeNull();
    expect(row.from).toBe(sourceIp);
    expect(row.time).toBe(utc(activity.items[i]!.timestampUtc));
  }
  await expect(myActivity.pager).toHaveCount(0);
  await expect(myActivity.inlineStyles).toHaveCount(0);

  await page.evaluate(() => document.fonts.ready);
  const path = info.outputPath(`administrator-${theme}-activity.png`);
  await page.screenshot({ path, fullPage: true });
  await info.attach(`administrator-${theme}-activity`, { path, contentType: 'image/png' });
}
