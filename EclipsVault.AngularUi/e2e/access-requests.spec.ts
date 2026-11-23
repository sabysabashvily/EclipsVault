import { refreshAdministratorProof } from './administrator-proof';
import { AccountCreation } from './account-creation';
import type { BrowserContext, Page } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { AccessRequests } from './access-requests';
import { feedEntry } from './activity';
import { Denied } from './denied';
import { test, expect, type BrowserHealth } from './fixtures';
import { expectLegacyShell, legacyShell } from './legacy-shell';
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
const filedByKeyboard = 'E2e_Request_Filed_By_Keyboard';
const projectReason = "Subject project 'E2E-AR' does not match resource project 'GLOBAL'.";
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

/** The details page's own request for a refused secret, which the browser logs as an error. */
function expectRefusal(browserHealth: BrowserHealth): void {
  browserHealth.expectConsoleError({
    text: /status of 403/,
    url: /\/api\/ui\/secrets\/[0-9a-f-]{36}$/,
  });
}

/** Opens the secret as the requester, is denied with the policy's reasons, and files from Denied. */
async function fileRequest(
  page: Page,
  browserHealth: BrowserHealth,
  secret: string,
  why = reason,
): Promise<AccessRequests> {
  expectRefusal(browserHealth);
  await page.context().addCookies(requesterCookies);
  await page.goto(detailsPaths.get(secret)!);
  await expect(page).toHaveURL(/\/Account\/Denied\?secretId=[0-9a-f-]{36}$/);
  const denied = new Denied(page);
  await expect(denied.heading).toBeVisible();
  await expect(denied.denial(projectReason)).toBeVisible();
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

test('a requester denied in another project sees the request they filed from the Denied page', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(180_000);
  const password = `E2e-${randomBytes(24).toString('hex')}`;
  await signInAsAdministrator(page);
  const creation = new AccountCreation(page);
  await refreshAdministratorProof(page);
  await creation.open();
  await creation.create({
    firstName: 'Access',
    lastName: 'Requester',
    username: requesterUsername,
    password,
    clearance: 'Elevated',
    project: 'E2E-AR',
  });
  await expect(page).toHaveURL(/\/Admin\/Users$/);

  for (const [secret, sensitivity] of [
    [approved, 'Internal'],
    [rejected, 'Internal'],
    [withdrawn, 'Internal'],
    [byKeyboard, 'Internal'],
    [aboveClearance, 'Secret'],
    [filedByKeyboard, 'Internal'],
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

  const requests = await fileRequest(requester, browserHealth, approved);
  await expect(requests.announcement(filedMessage)).toBeVisible();
  await expect(requests.verdict).toHaveText('Nothing to review');
  await expect(requests.reviewScope).toHaveText('Your project · Newest first');
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
  browserHealth,
}) => {
  expectRefusal(browserHealth);
  await page.context().addCookies(requesterCookies);
  await page.goto(detailsPaths.get(aboveClearance)!);
  const denied = new Denied(page);
  await expect(denied.requestAccess).toBeVisible();
  await expect(
    denied.refusal("A share would not help: your clearance is below this secret's sensitivity."),
  ).toBeVisible();
  await expect(denied.reason).toHaveCount(0);
  await expect(denied.submit).toHaveCount(0);
  await expect(denied.refusedBecause).toBeVisible();

  // The reasons live only in the navigation that brought the requester here.
  await page.reload();
  await expect(denied.generic).toBeVisible();
  await expect(denied.refusedBecause).toHaveCount(0);
  await expect(
    denied.refusal("A share would not help: your clearance is below this secret's sensitivity."),
  ).toBeVisible();
});

test('the keyboard alone files a request from Denied', async ({ page, browserHealth }) => {
  expectRefusal(browserHealth);
  await page.context().addCookies(requesterCookies);
  await page.goto(detailsPaths.get(filedByKeyboard)!);
  const denied = new Denied(page);
  await expect(denied.requestAccess).toBeVisible();
  await denied.backToSecrets.focus();
  await page.keyboard.press('Tab');
  await expect(denied.reason).toBeFocused();
  await page.keyboard.type(reason);
  await page.keyboard.press('Tab');
  await expect(denied.submit).toBeFocused();
  await page.keyboard.press('Enter');
  const requests = new AccessRequests(page);
  await expect(requests.announcement(filedMessage)).toBeVisible();
  await expect(requests.myRequest(filedByKeyboard)).toBeVisible();
});

for (const theme of ['dark', 'light']) {
  test(`Denied shows its reasons and refusal in the ${theme} theme`, async ({
    page,
    browserHealth,
  }, info) => {
    expectRefusal(browserHealth);
    await useTheme(page, theme);
    await page.context().addCookies(requesterCookies);
    await page.goto(detailsPaths.get(aboveClearance)!);
    const denied = new Denied(page);
    await expect(denied.refusedBecause).toBeVisible();
    await expect(denied.requestAccess).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.dataset['theme'])).toBe(theme);

    const path = info.outputPath(`${theme}-denied.png`);
    await page.screenshot({ path, fullPage: true });
    await info.attach(`${theme}-denied`, { path, contentType: 'image/png' });
  });
}

for (const width of [300, 2000] as const) {
  test(`Denied has no sideways scroll at ${width}px with reasons and the request form`, async ({
    page,
    browserHealth,
  }) => {
    expectRefusal(browserHealth);
    await page.setViewportSize({ width, height: 900 });
    await page.context().addCookies(requesterCookies);
    await page.goto(detailsPaths.get(rejected)!);
    const denied = new Denied(page);
    await expect(denied.refusedBecause).toBeVisible();
    await expect(denied.reason).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(width);
  });
}

test('Denied is served as the titled Angular document', async ({ page }) => {
  await page.context().addCookies(requesterCookies);
  const response = await page.goto('/Account/Denied');
  await expect(new Denied(page).generic).toBeVisible();
  await expect(page).toHaveTitle('Access denied — EclipsVault');
  expect(response?.headers()['content-security-policy']).toContain(
    "require-trusted-types-for 'script'",
  );
});

test('an empty reason is refused in the Legacy words without filing', async ({
  page,
  browserHealth,
}) => {
  // The vault refuses the filing itself, which the browser logs as an error.
  browserHealth.expectConsoleError({ text: /status of 409/, url: /\/api\/ui\/access-requests$/ });
  const refused = await fileRequest(page, browserHealth, rejected, '');

  await expect(refused.error).toHaveText('Add a short reason so a reviewer can decide.');
  await expect(refused.myRequest(approved)).toBeVisible();
  await expect(refused.myRequest(rejected)).toHaveCount(0);
});

test("an approval with an expiry reaches the requester's Shared with me", async ({
  page,
  browserHealth,
}) => {
  const requests = await openAsAdministrator(page);
  await expect(requests.verdict).toHaveText('Waiting on you');
  await expect(requests.reviewScope).toHaveText('All projects · Newest first');
  const request = requests.requestToReview(approved);
  await expect(request).toContainText(`${requesterUsername} · ${reason}`);
  await expect(request).toContainText(
    "Denied because: Subject project 'E2E-AR' does not match resource project 'GLOBAL'.",
  );
  await requests.approveOf(request).click();
  await expect(requests.expiry).toHaveValue('0');
  await requests.expiry.fill('7');
  await requests.approveAndGrant.click();
  await expect(
    requests.announcement('Request approved — a grant was created for the requester.'),
  ).toBeVisible();
  await expect(request).toHaveCount(0);

  const requester = await (await browserHealth.newContext()).newPage();
  await requester.context().addCookies(requesterCookies);
  await requester.goto('/Secrets/SharedWithMe');
  const shared = new SharedWithMe(requester);
  // Approved for 7 days a moment ago: the pill counts the whole days left.
  await expect(shared.accessUntilOf(shared.secret(approved))).toHaveText(
    /^In 6 days · \d{1,2} [A-Z][a-z]{2}( \d{4})?$/,
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

test("the requester opens the approved share's Legacy Details from Shared with me", async ({
  page,
}) => {
  await page.context().addCookies(requesterCookies);
  await page.goto('/Secrets/SharedWithMe');
  const shared = new SharedWithMe(page);
  await shared.openOf(shared.secret(approved)).click();
  await expect(page).toHaveURL(new RegExp(`${detailsPaths.get(approved)}$`));
  await expect(page.getByRole('heading', { name: approved, exact: true })).toBeVisible();
});

test('a reviewer rejects through the confirmation dialog, after cancelling once', async ({
  page,
  browserHealth,
}) => {
  const requester = await (await browserHealth.newContext()).newPage();
  await expect(
    (await fileRequest(requester, browserHealth, rejected)).announcement(filedMessage),
  ).toBeVisible();

  const requests = await openAsAdministrator(page);
  const request = requests.requestToReview(rejected);
  const question = 'Reject this request?';
  await requests.rejectOf(request).click();
  await requests.confirmation(question).getByRole('button', { name: 'Cancel' }).click();
  await expect(requests.confirmation(question)).toBeHidden();
  await expect(requests.rejectOf(request)).toBeFocused();
  await expect(request).toBeVisible();

  await requests.rejectOf(request).click();
  await requests.confirmation(question).getByRole('button', { name: 'Reject request' }).click();
  await expect(requests.announcement('Request rejected.')).toBeVisible();
  await expect(request).toHaveCount(0);

  await requester.reload();
  const mine = new AccessRequests(requester);
  await expect(mine.statusOf(mine.myRequest(rejected))).toHaveText('Rejected');
  await expect(mine.withdrawOf(mine.myRequest(rejected))).toHaveCount(0);
});

test('the requester withdraws a pending request through the confirmation dialog', async ({
  page,
  browserHealth,
}) => {
  const requests = await fileRequest(page, browserHealth, withdrawn);
  const request = requests.myRequest(withdrawn);
  const question = `Withdraw your request for ${withdrawn}?`;
  await requests.withdrawOf(request).click();
  await requests.confirmation(question).getByRole('button', { name: 'Withdraw request' }).click();
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

test('on a phone the approval opens as a bottom sheet', async ({ page, browserHealth }) => {
  const requester = await (await browserHealth.newContext()).newPage();
  await expect(
    (await fileRequest(requester, browserHealth, byKeyboard)).announcement(filedMessage),
  ).toBeVisible();

  await page.setViewportSize({ width: 375, height: 800 });
  const requests = await openAsAdministrator(page);
  await requests.approveOf(requests.requestToReview(byKeyboard)).click();
  await expect(requests.approval).toBeVisible();
  // The sheet slides up; once it settles it spans the width and sits on the bottom edge.
  await expect
    .poll(async () => {
      const box = await requests.approval.boundingBox();
      return box && [Math.round(box.x), Math.round(box.width), Math.round(box.y + box.height)];
    })
    .toEqual([0, 375, 800]);
  await page.keyboard.press('Escape');
  await expect(requests.approval).toBeHidden();
});

for (const width of [300, 2000]) {
  test(`Access requests has no sideways scroll at ${width}px with a request to review`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 900 });
    const requests = await openAsAdministrator(page);
    await expect(requests.requestToReview(byKeyboard)).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(width);
  });
}

test('the keyboard alone approves with an end date', async ({ page }) => {
  const requests = await openAsAdministrator(page);
  const request = requests.requestToReview(byKeyboard);
  await requests.heading.click();
  await page.keyboard.press('Tab');
  await expect(requests.approveOf(request)).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(requests.expiry).toBeFocused();
  await page.keyboard.press('ControlOrMeta+a');
  await page.keyboard.type('3');
  await page.keyboard.press('Tab');
  await page.keyboard.press('Tab');
  await expect(requests.approveAndGrant).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(
    requests.announcement('Request approved — a grant was created for the requester.'),
  ).toBeVisible();
  await expect(request).toHaveCount(0);
});

test("the requester's My activity lists their access requests by secret", async ({ page }) => {
  await openAsRequester(page, '/Activity');
  await expect(feedEntry(page, 'Requested access to a secret', approved)).toBeVisible();
  await expect(feedEntry(page, 'Requested access to a secret', rejected)).toBeVisible();
  await expect(feedEntry(page, 'Requested access to a secret', withdrawn)).toBeVisible();
  await expect(feedEntry(page, 'Requested access to a secret', byKeyboard)).toBeVisible();
  await expect(feedEntry(page, 'Requested access to a secret', filedByKeyboard)).toBeVisible();
  await expect(feedEntry(page, 'Cancelled an access request', withdrawn)).toBeVisible();
});

test("the reviewer's My activity lists their decisions by secret", async ({ page }) => {
  await signInAsAdministrator(page);
  await page.goto('/Activity');
  await expect(feedEntry(page, 'Approved an access request', approved)).toBeVisible();
  await expect(feedEntry(page, 'Shared a secret', approved)).toBeVisible();
  await expect(feedEntry(page, 'Rejected an access request', rejected)).toBeVisible();
  await expect(feedEntry(page, 'Approved an access request', byKeyboard)).toBeVisible();
});

test('with no requests of their own, Browse secrets opens the Secrets list', async ({ page }) => {
  const requests = await openAsAdministrator(page);
  await expect(requests.noRequests).toBeVisible();
  await requests.browseSecrets.click();
  await expect(new SecretsList(page).heading).toBeVisible();
  await expect(page).toHaveURL(/\/Secrets$/);
});

for (const theme of ['dark', 'light']) {
  test(`Access requests shows the approved request and empty review queue in ${theme}`, async ({
    page,
  }, info) => {
    await page.setViewportSize({ width: 1440, height: 1200 });
    await useTheme(page, theme);
    const requests = await openAsRequester(page);
    await expect(requests.heading).toBeVisible();
    await expect(requests.myRequest(approved)).toBeVisible();
    await expect(requests.nothingToReview).toBeVisible();
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

test('the sidebar opens Access requests in-app from its top-level item', async ({ page }) => {
  await openAsRequester(page, '/Access');
  const documents = recordDocuments(page);
  const link = page.getByRole('link', { name: 'Access requests', exact: true });
  await link.click();
  await expect(new AccessRequests(page).myRequest(approved)).toBeVisible();
  await expect(link).toHaveAttribute('aria-current', 'page');
  // Access requests sits at the top of the sidebar, in no folding group.
  await expect(page.getByRole('group').filter({ has: link })).toHaveCount(0);
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

test('the Legacy sidebar loads Access requests into Angular', async ({ page, browserHealth }) => {
  expectLegacyShell(browserHealth);
  await openAsRequester(page, legacyShell);
  const documents = recordDocuments(page);
  await page.getByText('Access', { exact: true }).click();
  await page.getByRole('link', { name: 'Access requests', exact: true }).click();
  await expect(new AccessRequests(page).myRequest(approved)).toBeVisible();
  await expect(page).toHaveTitle('Access requests — EclipsVault');
  expect(documents).toEqual(['/AccessRequests']);
});

test('the Legacy palette loads Access requests into Angular', async ({ page, browserHealth }) => {
  expectLegacyShell(browserHealth);
  await openAsRequester(page, legacyShell);
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
