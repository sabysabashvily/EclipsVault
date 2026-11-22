import { type Activity, MyActivity, fulfilActivity } from './activity';
import { savedAdministratorCookies } from './administrator-cookies';
import { AppShell } from './app-shell';
import { test, expect } from './fixtures';
import { openPalette } from './palette';
import { ProfilePage } from './profile';

const emptyActivity: Activity = {
  page: 1,
  pageSize: 25,
  hasMore: false,
  hasPrevious: false,
  notableCount: 0,
  items: [],
};
const longName = 'Payments_Production_Database_Primary_Connection_String_Rotated_Quarterly';
const exactTime = /^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d UTC$/;

/** Two mocked My activity pages, the first timed from now. */
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
  await page.context().addCookies(await savedAdministratorCookies());
  const shell = new AppShell(page);
  const myActivity = new MyActivity(page);
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
    await expect(myActivity.empty).toBeVisible();
    await expect(myActivity.emptyMessage).toBeVisible();
    await expect(myActivity.table).toHaveCount(0);
    await expect(myActivity.pager).toHaveCount(0);
    await expect(myActivity.nothingFurther).toHaveCount(0);
    await expect(myActivity.inlineStyles).toHaveCount(0);
    expect(requested).toEqual(['']);
    await page.unrouteAll({ behavior: 'wait' });
  });

  const { first, second } = activityPages();
  const requested = await fulfilActivity(page, { '': first, '1': first, '2': second });

  await test.step('the first page names each row, its severity and kind, and links only to older', async () => {
    await shell.myAccess.click();
    await page.getByRole('link', { name: 'My activity', exact: true }).click();
    await expect(page).toHaveURL(/\/Activity$/);
    await expect(myActivity.rows).toHaveCount(4);
    expect(await myActivity.entries()).toEqual([
      {
        title: 'Revealed a secret value',
        kind: 'Secrets',
        severity: 'Notable',
        resource: longName,
        from: '203.0.113.7',
        time: expect.stringMatching(exactTime),
      },
      {
        title: 'A session was force-revoked',
        kind: 'Security',
        severity: 'Critical',
        resource: null,
        from: '203.0.113.8',
        time: expect.stringMatching(exactTime),
      },
      {
        title: 'Signed in',
        kind: 'Sign-in',
        severity: '',
        resource: null,
        from: '203.0.113.7',
        time: expect.stringMatching(exactTime),
      },
      {
        title: 'Shared a secret',
        kind: 'Sharing',
        severity: 'Notable',
        resource: 'Shared_Key',
        from: '2001:db8::5',
        time: expect.stringMatching(exactTime),
      },
    ]);
    await expect(myActivity.summary).toContainText('2 notable on this page.');
    await expect(myActivity.pageNumber).toHaveText('Page 1');
    await expect(myActivity.pagerLinks).toHaveCount(1);
    await expect(myActivity.older).toHaveAttribute('href', '/Activity?page=2');
    await expect(myActivity.inlineStyles).toHaveCount(0);
  });

  await test.step('Older → changes ?page= in-app and shows the older page', async () => {
    await myActivity.older.click();
    await expect(page).toHaveURL(/\/Activity\?page=2$/);
    await expect(myActivity.pageNumber).toHaveText('Page 2');
    await expect(myActivity.pagerLinks).toHaveCount(1);
    await expect(myActivity.newer).toHaveAttribute('href', '/Activity?page=1');
    await expect(myActivity.summary).not.toContainText('notable');
    expect(await myActivity.entries()).toEqual([
      {
        title: 'Created a user',
        kind: 'Admin',
        severity: '',
        resource: null,
        from: '198.51.100.20',
        time: '2026-01-02 03:04:05 UTC',
      },
      {
        title: 'Changed your password',
        kind: 'Account',
        severity: '',
        resource: null,
        from: '198.51.100.20',
        time: '2025-12-31 23:59:59 UTC',
      },
      {
        title: 'Issued an API key',
        kind: 'Automation',
        severity: '',
        resource: null,
        from: 'internal',
        time: '2025-12-30 08:00:00 UTC',
      },
      {
        title: 'Some new action',
        kind: 'Other',
        severity: '',
        resource: null,
        from: 'internal',
        time: '2025-12-29 08:00:00 UTC',
      },
    ]);
  });

  await test.step('back and forward move between the visited pages and refetch each', async () => {
    await page.goBack();
    await expect(page).toHaveURL(/\/Activity$/);
    await expect(myActivity.pageNumber).toHaveText('Page 1');
    await expect(myActivity.rows.first()).toContainText('Revealed a secret value');
    await page.goForward();
    await expect(page).toHaveURL(/\/Activity\?page=2$/);
    await expect(myActivity.pageNumber).toHaveText('Page 2');
    await myActivity.newer.click();
    await expect(page).toHaveURL(/\/Activity\?page=1$/);
    await expect(myActivity.pageNumber).toHaveText('Page 1');
    expect(requested).toEqual(['', '2', '', '2', '1']);
    expect(documents).toEqual([]);
  });
  await page.unrouteAll({ behavior: 'wait' });
});

test('the sidebar and palette open My activity in-app, and its Profile link opens Angular on the same session', async ({
  page,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const shell = new AppShell(page);
  const myActivity = new MyActivity(page);
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

  await test.step('the sidebar link keeps the document and marks My activity current in an open You group', async () => {
    const sidebarLink = shell.navigation.getByRole('link', { name: 'My activity', exact: true });
    await sidebarLink.click();
    await expect(page).toHaveURL(/\/Activity$/);
    await expect(page).toHaveTitle('My activity — EclipsVault');
    await expect(myActivity.heading).toBeVisible();
    await expect(sidebarLink).toHaveAttribute('aria-current', 'page');
    await expect(shell.youGroup).toHaveAttribute('open', '');
    expect(documents).toEqual([]);
  });

  await test.step('the palette opens My activity in place', async () => {
    await shell.myAccess.click();
    await expect(page).toHaveURL(/\/Access$/);
    await openPalette(page);
    await shell.paletteQuery.fill('my activity');
    await expect(page.getByRole('option')).toHaveText([/My activity/]);
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/Activity$/);
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(myActivity.rows.first()).toBeVisible();
    expect(documents).toEqual([]);
  });

  await test.step('the Profile link stays inside Angular on the same session', async () => {
    await myActivity.profileLink.click();
    await expect(page).toHaveURL(/\/Profile$/);
    await expect(new ProfilePage(page).heading).toBeVisible();
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
  await page.context().addCookies(await savedAdministratorCookies());
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
  const myActivity = new MyActivity(page);
  await myActivity.open();
  await expect(myActivity.failure).toHaveText(
    'Could not load your activity. Reload the page to try again.',
  );
  await expect(myActivity.table).toHaveCount(0);
  await myActivity.tryAgain.click();
  await expect(myActivity.rows.first()).toBeVisible();
  await expect(myActivity.failure).toHaveCount(0);
  await expect(myActivity.inlineStyles).toHaveCount(0);
});

test('signed out, My activity sends the browser to sign-in with its return URL', async ({
  page,
}) => {
  const myActivity = new MyActivity(page);
  await test.step('My activity retains its return URL', async () => {
    await myActivity.open();
    await expect(page).toHaveURL(new RegExp(`/Account/Login\\?ReturnUrl=%2FActivity$`));
    await expect(page.getByLabel('Username')).toBeVisible();
  });
  await test.step('the My activity alias retains its return URL', async () => {
    await myActivity.open('/Index');
    await expect(page).toHaveURL(new RegExp(`/Account/Login\\?ReturnUrl=%2FActivity%2FIndex$`));
    await expect(page.getByLabel('Username')).toBeVisible();
  });
});
