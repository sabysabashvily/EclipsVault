import type { BrowserContext, Page } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { AccessRequests } from './access-requests';
import { Denied } from './denied';
import { test, expect } from './fixtures';
import { lookOf } from './look';
import { createSecret, openPalette } from './palette';
import { SecretsList } from './secrets-list';
import { SharedWithMe } from './shared-with-me';
import { freshTotp } from './totp';

test.describe.configure({ mode: 'serial' });
const requesterUsername = 'e2e-ar-requester';
const reason = 'Needed for the e2e release';
const approved = 'E2e_Request_To_Approve';
const rejected = 'E2e_Request_To_Reject';
const withdrawn = 'E2e_Request_To_Withdraw';
const byKeyboard = 'E2e_Request_By_Keyboard';
const aboveClearance = 'E2e_Request_Above_Clearance';
const filedMessage = 'Access request submitted. A reviewer for this secret will decide.';
const detailsPaths = new Map<string, string>();
let requesterCookies: Awaited<ReturnType<BrowserContext['cookies']>> = [];

async function signInAsAdministrator(page: Page): Promise<void> {
  const state = JSON.parse(
    await readFile(
      `${process.env['E2E_TEST_RESULTS'] || 'test-results'}/sessions-administrator.json`,
      'utf8',
    ),
  ) as { cookies: Awaited<ReturnType<BrowserContext['cookies']>> };
  await page.context().addCookies(state.cookies);
}

async function openAsAdministrator(page: Page): Promise<AccessRequests> {
  await signInAsAdministrator(page);
  const requests = new AccessRequests(page);
  await requests.open();
  await expect(requests.heading).toBeVisible();
  return requests;
}

async function openAsRequester(page: Page, path = '/AccessRequests'): Promise<AccessRequests> {
  await page.context().addCookies(requesterCookies);
  await page.goto(path);
  return new AccessRequests(page);
}

/** Opens the secret as the requester, is denied, and files from the Legacy Denied page. */
async function fileRequest(page: Page, secret: string, why = reason): Promise<AccessRequests> {
  await page.context().addCookies(requesterCookies);
  await page.goto(detailsPaths.get(secret)!);
  await expect(page).toHaveURL(/\/Account\/Denied\?secretId=[0-9a-f-]{36}$/);
  const denied = new Denied(page);
  await expect(denied.heading).toBeVisible();
  await denied.reason.fill(why);
  await denied.submit.click();
  // The page shows the outcome once, then drops it from the address.
  await expect(page).toHaveURL(/\/AccessRequests$/);
  return new AccessRequests(page);
}

function recordDocuments(page: Page): string[] {
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(new URL(request.url()).pathname);
  });
  return documents;
}

function useTheme(page: Page, theme: string) {
  return page
    .context()
    .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: process.env['E2E_BASE_URL']! }]);
}

test('a requester in another project is denied, files from the Denied page and sees it filed', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(180_000);
  const password = `E2e-${randomBytes(24).toString('hex')}`;
  await signInAsAdministrator(page);
  await page.goto('/Admin/CreateUser');
  await page.getByLabel('First name').fill('Access');
  await page.getByLabel('Last name').fill('Requester');
  await page.getByLabel('Username').fill(requesterUsername);
  await page.getByLabel('Initial password', { exact: true }).fill(password);
  await page.getByLabel('Clearance').selectOption({ label: 'Elevated' });
  await page.getByLabel('Project').fill('E2E-AR');
  await page.getByRole('button', { name: 'Create user', exact: true }).click();
  await expect(page).toHaveURL(/\/Admin\/Users$/);

  for (const [secret, sensitivity] of [
    [approved, 'Internal'],
    [rejected, 'Internal'],
    [withdrawn, 'Internal'],
    [byKeyboard, 'Internal'],
    [aboveClearance, 'Secret'],
  ]) {
    await createSecret(page, secret, sensitivity);
    detailsPaths.set(secret, new URL(page.url()).pathname);
  }

  // Two credential posts: the earlier specs leave this window's sign-in budget far from spent.
  const requester = await (await browserHealth.newContext()).newPage();
  await requester.goto('/Account/Login');
  await requester.getByLabel('Username').fill(requesterUsername);
  await requester.getByLabel('Password', { exact: true }).fill(password);
  await requester.getByRole('button', { name: 'Continue', exact: true }).click();
  await expect(requester).toHaveURL(/\/Account\/EnrollTotp$/);
  // eslint-disable-next-line playwright/no-raw-locators -- Enrollment displays the throwaway authenticator seed as unlabelled text.
  const seed = await requester.locator('.secret-code').innerText();
  await requester.getByLabel('Confirmation code').fill(await freshTotp(seed));
  await requester.getByRole('button', { name: 'Confirm enrollment', exact: true }).click();
  await expect(requester).toHaveURL(/\/Secrets$/);
  requesterCookies = await requester.context().cookies();
  const before = await openAsRequester(requester);
  await expect(before.noRequests).toBeVisible();

  const requests = await fileRequest(requester, approved);
  await expect(requests.announcement(filedMessage)).toBeVisible();
  await expect(requests.reviewScope).toHaveText('Your project');
  await expect(requests.nothingToReview).toBeVisible();
  const filed = requests.myRequest(approved);
  await expect(requests.cellsOf(filed)).toHaveText([
    `${approved} GLOBAL`,
    'Pending',
    /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/,
    '—',
    'Withdraw',
  ]);

  await requester.reload();
  await expect(requests.heading).toBeVisible();
  await expect(requests.myRequest(approved)).toBeVisible();
  await expect(requests.announcement(filedMessage)).toHaveCount(0);
});

test('an under-cleared requester is told a share would not help and offered no form', async ({
  page,
}) => {
  await page.context().addCookies(requesterCookies);
  await page.goto(detailsPaths.get(aboveClearance)!);
  const denied = new Denied(page);
  await expect(denied.requestAccess).toBeVisible();
  await expect(
    denied.refusal("A share would not help: your clearance is below this secret's sensitivity."),
  ).toBeVisible();
  await expect(denied.reason).toHaveCount(0);
  await expect(denied.submit).toHaveCount(0);
});

test('an empty reason is refused with the Legacy words and files nothing', async ({ page }) => {
  const refused = await fileRequest(page, rejected, '');

  await expect(refused.error).toHaveText('Add a short reason so a reviewer can decide.');
  await expect(refused.myRequest(approved)).toBeVisible();
  await expect(refused.myRequest(rejected)).toHaveCount(0);
});

test('a reviewer approves with an expiry and the requester finds it in Shared with me', async ({
  page,
  browserHealth,
}) => {
  const requests = await openAsAdministrator(page);
  await expect(requests.reviewScope).toHaveText('All projects');
  await expect(requests.toReview.getByRole('columnheader')).toHaveText([
    'Secret',
    'Requester',
    'Reason',
    'Denied because',
    'Requested',
    '',
  ]);
  const request = requests.requestToReview(approved);
  await expect(requests.cellsOf(request)).toHaveText([
    `${approved} GLOBAL`,
    requesterUsername,
    reason,
    /Subject project 'E2E-AR' does not match resource project 'GLOBAL'\./,
    /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/,
    'Approve Reject',
  ]);
  const expiry = requests.expiryOf(request);
  await expect(expiry).toHaveValue('0');
  await expect(expiry).toHaveAttribute('title', 'Grant expires in days (0 = no expiry)');
  await expiry.fill('7');
  await requests.approveOf(request).click();
  await expect(
    requests.announcement('Request approved — a grant was created for the requester.'),
  ).toBeVisible();
  await expect(request).toHaveCount(0);

  const requester = await (await browserHealth.newContext()).newPage();
  await requester.context().addCookies(requesterCookies);
  await requester.goto('/Secrets/SharedWithMe');
  const shared = new SharedWithMe(requester);
  await expect(shared.accessUntilOf(shared.secret(approved))).toHaveText(
    /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/,
  );
  const mine = await openAsRequester(requester);
  await expect(mine.cellsOf(mine.myRequest(approved))).toHaveText([
    `${approved} GLOBAL`,
    'Approved',
    /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/,
    /^vault-admin · \d{4}-\d{2}-\d{2}$/,
    '',
  ]);
});

test('a reviewer rejects through the confirmation dialog, after cancelling once', async ({
  page,
  browserHealth,
}) => {
  const requester = await (await browserHealth.newContext()).newPage();
  await expect((await fileRequest(requester, rejected)).announcement(filedMessage)).toBeVisible();

  const requests = await openAsAdministrator(page);
  const request = requests.requestToReview(rejected);
  const question = `Reject ${requesterUsername}'s request for ${rejected}?`;
  await requests.rejectOf(request).click();
  await requests.confirmation(question).getByRole('button', { name: 'Cancel' }).click();
  await expect(requests.confirmation(question)).toBeHidden();
  await expect(requests.rejectOf(request)).toBeFocused();
  await expect(request).toBeVisible();

  await requests.rejectOf(request).click();
  await requests.confirmation(question).getByRole('button', { name: 'Continue' }).click();
  await expect(requests.announcement('Request rejected.')).toBeVisible();
  await expect(request).toHaveCount(0);

  await requester.reload();
  const mine = new AccessRequests(requester);
  await expect(mine.statusOf(mine.myRequest(rejected))).toHaveText('Rejected');
  await expect(mine.withdrawOf(mine.myRequest(rejected))).toHaveCount(0);
});

test('the requester withdraws a pending request through the confirmation dialog', async ({
  page,
}) => {
  const requests = await fileRequest(page, withdrawn);
  const request = requests.myRequest(withdrawn);
  const question = `Withdraw your request for ${withdrawn}?`;
  await requests.withdrawOf(request).click();
  await requests.confirmation(question).getByRole('button', { name: 'Continue' }).click();
  await expect(requests.announcement('Request withdrawn.')).toBeVisible();
  await expect(requests.cellsOf(request)).toHaveText([
    `${withdrawn} GLOBAL`,
    'Withdrawn',
    /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/,
    // Withdrawing records the requester as the one who decided, as Legacy showed.
    new RegExp(`^${requesterUsername} · \\d{4}-\\d{2}-\\d{2}$`),
    '',
  ]);
});

test('the keyboard reaches the expiry box and approves without a pointer', async ({
  page,
  browserHealth,
}) => {
  const requester = await (await browserHealth.newContext()).newPage();
  await expect((await fileRequest(requester, byKeyboard)).announcement(filedMessage)).toBeVisible();

  const requests = await openAsAdministrator(page);
  const request = requests.requestToReview(byKeyboard);
  await requests.introduction.click();
  await page.keyboard.press('Tab');
  await expect(requests.expiryOf(request)).toBeFocused();
  await page.keyboard.press('ControlOrMeta+a');
  await page.keyboard.type('3');
  await page.keyboard.press('Tab');
  await expect(requests.approveOf(request)).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(
    requests.announcement('Request approved — a grant was created for the requester.'),
  ).toBeVisible();
  await expect(request).toHaveCount(0);
});

for (const [theme, accent, accentBackground] of [
  ['dark', 'rgb(255, 159, 10)', 'rgba(255, 159, 10, 0.14)'],
  ['light', 'rgb(180, 95, 4)', 'rgba(180, 95, 4, 0.12)'],
]) {
  test(`visual parity: Access requests keeps the Legacy page and table look in ${theme}`, async ({
    page,
  }, info) => {
    await page.setViewportSize({ width: 1440, height: 1200 });
    await useTheme(page, theme);
    const legacy = new SecretsList(page);
    await page.context().addCookies(requesterCookies);
    await legacy.open();
    await expect(legacy.heading).toBeVisible();
    // Legacy's Access requests page used the Secrets list's own heading, introduction and table classes.
    const expected = {
      heading: await lookOf(legacy.heading),
      introduction: await lookOf(legacy.introduction),
      table: await lookOf(page.getByRole('table')),
      column: await lookOf(legacy.columns.first()),
    };

    const requests = await openAsRequester(page);
    await expect(requests.myRequest(approved)).toBeVisible();
    expect({
      heading: await lookOf(requests.heading),
      introduction: await lookOf(requests.introduction),
      table: await lookOf(requests.mine),
      column: await lookOf(requests.mine.getByRole('columnheader').first()),
    }).toEqual(expected);
    await expect(requests.nothingToReview).toBeVisible();
    await expect(requests.emptyIcon).toHaveCSS('color', accent);
    await expect(requests.emptyIcon).toHaveCSS('background-color', accentBackground);
    const path = info.outputPath(`${theme}-access-requests.png`);
    await page.screenshot({ path, fullPage: true });
    await info.attach(`${theme}-access-requests`, { path, contentType: 'image/png' });
  });
}

test('Access requests is served as the titled Angular document', async ({ page }) => {
  await page.context().addCookies(requesterCookies);
  const response = await page.goto('/AccessRequests');
  await expect(new AccessRequests(page).heading).toBeVisible();
  await expect(page).toHaveTitle('Access requests — EclipsVault');
  expect(response?.headers()['content-security-policy']).toContain(
    "require-trusted-types-for 'script'",
  );
});

test('a mixed-case Access requests bookmark serves the titled Angular page', async ({ page }) => {
  const requests = await openAsRequester(page, '/aCcEsSrEqUeStS/iNdEx');
  await expect(requests.myRequest(approved)).toBeVisible();
  await expect(page).toHaveTitle('Access requests — EclipsVault');
});

test('the sidebar opens Access requests in-app with its Access group active', async ({ page }) => {
  await openAsRequester(page, '/Access');
  const documents = recordDocuments(page);
  const link = page.getByRole('link', { name: 'Access requests', exact: true });
  await link.click();
  await expect(new AccessRequests(page).myRequest(approved)).toBeVisible();
  await expect(link).toHaveAttribute('aria-current', 'page');
  await expect(page.getByRole('group').filter({ has: link })).toHaveJSProperty('open', true);
  expect(documents).toEqual([]);
});

test('the palette opens Access requests without a document load', async ({ page }) => {
  await openAsRequester(page, '/Access');
  const documents = recordDocuments(page);
  await openPalette(page);
  await page.getByRole('combobox').fill('Access requests');
  await page.getByRole('option', { name: /Access requests/ }).click();
  await expect(new AccessRequests(page).myRequest(approved)).toBeVisible();
  expect(documents).toEqual([]);
});

test('the Legacy sidebar loads Access requests into Angular', async ({ page }) => {
  await openAsRequester(page, '/Secrets');
  const documents = recordDocuments(page);
  await page.getByText('Access', { exact: true }).click();
  await page.getByRole('link', { name: 'Access requests', exact: true }).click();
  await expect(new AccessRequests(page).myRequest(approved)).toBeVisible();
  await expect(page).toHaveTitle('Access requests — EclipsVault');
  expect(documents).toEqual(['/AccessRequests']);
});

test('the Legacy palette loads Access requests into Angular', async ({ page }) => {
  await openAsRequester(page, '/Secrets');
  const documents = recordDocuments(page);
  await openPalette(page);
  await page.getByRole('combobox').fill('Access requests');
  await page.getByRole('option', { name: /Access requests/ }).click();
  await expect(new AccessRequests(page).myRequest(approved)).toBeVisible();
  expect(documents).toEqual(['/AccessRequests']);
});

test('Access requests reports a failed fetch and retries without a document load', async ({
  page,
  browserHealth,
}) => {
  await page.route(
    '**/api/ui/access-requests',
    (route) => route.fulfill({ status: 503, json: { code: 'internal_error' } }),
    { times: 1 },
  );
  browserHealth.expectConsoleError({ text: /status of 503/, url: /\/api\/ui\/access-requests$/ });
  const requests = await openAsRequester(page);
  await expect(requests.error).toHaveText('Could not load your access requests. Try again.');
  const documents = recordDocuments(page);
  await requests.retry.click();
  await expect(requests.myRequest(approved)).toBeVisible();
  await expect(requests.error).toHaveCount(0);
  expect(documents).toEqual([]);
});

test('signed out, an Access requests bookmark returns through sign-in', async ({ page }) => {
  await page.goto('/AccessRequests');
  await expect(page).toHaveURL(/\/Account\/Login\?ReturnUrl=%2FAccessRequests$/);
});
