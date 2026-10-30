import type { BrowserContext, Page } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { test, expect } from './fixtures';
import { createSecret, openPalette } from './palette';
import { SharedByMe } from './shared-by-me';
import { freshTotp } from './totp';

test.describe.configure({ mode: 'serial' });
const recipientUsername = 'e2e-share-recipient';
let recipientCookies: Awaited<ReturnType<BrowserContext['cookies']>> = [];
let secretDetails = '';

async function openAsIssuer(page: Page, path = '/Secrets/SharedByMe'): Promise<SharedByMe> {
  const state = JSON.parse(
    await readFile(
      `${process.env['E2E_TEST_RESULTS'] || 'test-results'}/sessions-administrator.json`,
      'utf8',
    ),
  ) as {
    cookies: Awaited<ReturnType<BrowserContext['cookies']>>;
  };
  await page.context().addCookies(state.cookies);
  await page.goto(path);
  return new SharedByMe(page);
}

test('an issuer shares two real secrets with a colleague outside their project', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(210_000);
  const password = `E2e-${randomBytes(24).toString('hex')}`;
  await openAsIssuer(page, '/Admin/CreateUser');
  await page.getByLabel('First name').fill('Share');
  await page.getByLabel('Last name').fill('Recipient');
  await page.getByLabel('Username').fill(recipientUsername);
  await page.getByLabel('Initial password', { exact: true }).fill(password);
  await page.getByLabel('Clearance').selectOption({ label: 'Standard' });
  await page.getByLabel('Project').fill('E2E-RECIPIENT');
  await page.getByRole('button', { name: 'Create user', exact: true }).click();
  await expect(page).toHaveURL(/\/Admin\/Users$/);

  // Earlier sign-in scenarios use the same proxy source; keep the production limiter enabled.
  const windowMilliseconds = 120_000;
  await page.waitForTimeout(windowMilliseconds - (Date.now() % windowMilliseconds) + 1_000);
  const recipient = await (await browserHealth.newContext()).newPage();
  await recipient.goto('/Account/Login');
  await recipient.getByLabel('Username').fill(recipientUsername);
  await recipient.getByLabel('Password', { exact: true }).fill(password);
  await recipient.getByRole('button', { name: 'Continue', exact: true }).click();
  await expect(recipient).toHaveURL(/\/Account\/EnrollTotp$/);
  // eslint-disable-next-line playwright/no-raw-locators -- Enrollment displays the throwaway authenticator seed as unlabelled text.
  const seed = await recipient.locator('.secret-code').innerText();
  await recipient.getByLabel('Confirmation code').fill(await freshTotp(seed));
  await recipient.getByRole('button', { name: 'Confirm enrollment', exact: true }).click();
  await expect(recipient).toHaveURL(/\/Secrets$/);
  recipientCookies = await recipient.context().cookies();

  await createSecret(page, 'E2e_Shared_No_Expiry', 'Internal');
  secretDetails = new URL(page.url()).pathname;
  await recipient.goto(secretDetails);
  await expect(recipient).toHaveURL(/\/Account\/Denied\?/);
  await page.getByLabel('User to share with').fill(recipientUsername);
  await page.getByRole('button', { name: 'Share access', exact: true }).click();
  await expect(
    page.getByText("Access to 'E2e_Shared_No_Expiry' was shared with e2e-share-recipient.", {
      exact: true,
    }),
  ).toBeVisible();
  await recipient.goto(secretDetails);
  await expect(
    recipient.getByRole('heading', { name: 'E2e_Shared_No_Expiry', exact: true }),
  ).toBeVisible();
  await createSecret(page, 'E2e_Shared_Expiring', 'Internal');
  await page.getByLabel('User to share with').fill(recipientUsername);
  await page.getByLabel('Access expires in days').fill('7');
  await page.getByRole('button', { name: 'Share access', exact: true }).click();
  await expect(
    page.getByText("Access to 'E2e_Shared_Expiring' was shared with e2e-share-recipient.", {
      exact: true,
    }),
  ).toBeVisible();
});

for (const [theme, textColor, dangerColor, accent, accentBackground] of [
  [
    'dark',
    'rgb(237, 238, 242)',
    'rgb(255, 69, 58)',
    'rgb(255, 159, 10)',
    'rgba(255, 159, 10, 0.14)',
  ],
  ['light', 'rgb(28, 29, 34)', 'rgb(215, 0, 21)', 'rgb(180, 95, 4)', 'rgba(180, 95, 4, 0.12)'],
]) {
  test(`visual parity: Shared by me preserves its table and revoke styles in ${theme}`, async ({
    page,
  }, info) => {
    await page.setViewportSize({ width: 1440, height: 1200 });
    await page
      .context()
      .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: process.env['E2E_BASE_URL']! }]);
    const shares = await openAsIssuer(page);
    await expect(shares.rows).toHaveCount(2);
    await expect(shares.heading).toHaveCSS('color', textColor);
    await expect(shares.heading).toHaveCSS('font-size', '24px');
    await expect(shares.heading).toHaveCSS('font-weight', '620');
    await expect(shares.introduction).toHaveCSS('display', 'flex');
    await expect(page.getByRole('table')).toHaveCSS('border-collapse', 'collapse');
    await expect(shares.columns.first()).toHaveCSS('text-transform', 'uppercase');
    await expect(shares.secretOf(shares.rows.first())).toHaveCSS('font-weight', '600');
    await expect(shares.secretOf(shares.rows.first())).toHaveCSS('padding', '10.4px 11.2px');
    await expect(shares.rows.first().getByRole('button')).toHaveCSS('color', dangerColor);
    await expect(shares.rows.first().getByRole('button')).toHaveCSS(
      'background-color',
      'rgba(0, 0, 0, 0)',
    );
    const path = info.outputPath(`${theme}-shared-by-me.png`);
    await page.screenshot({ path, fullPage: true });
    await info.attach(`${theme}-shared-by-me`, { path, contentType: 'image/png' });
  });

  test(`visual parity: Shared by me preserves its empty state and share icon in ${theme}`, async ({
    page,
  }) => {
    await page.context().addCookies(recipientCookies);
    await page
      .context()
      .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: process.env['E2E_BASE_URL']! }]);
    await page.goto('/Secrets/SharedByMe');
    const shares = new SharedByMe(page);
    await expect(shares.emptyHeading).toBeVisible();
    await expect(shares.emptyMessage).toHaveText(
      "When you grant a colleague access to one of your secrets, it shows up here so you can keep track of who can see what — and revoke access when it's no longer needed.",
    );
    await expect(shares.goToSecrets).toBeVisible();
    await expect(shares.emptyState).toHaveCSS('text-align', 'center');
    await expect(shares.emptyState).toHaveCSS('padding', '48px 24px');
    await expect(shares.emptyIcon).toHaveCSS('width', '54px');
    await expect(shares.emptyIcon).toHaveCSS('height', '54px');
    await expect(shares.emptyIcon).toHaveCSS('color', accent);
    await expect(shares.emptyIcon).toHaveCSS('background-color', accentBackground);
  });
}

test('with nothing shared, Go to your secrets opens the Secrets page', async ({ page }) => {
  await page.context().addCookies(recipientCookies);
  await page.goto('/Secrets/SharedByMe');
  const shares = new SharedByMe(page);
  await shares.goToSecrets.click();
  await expect(page.getByRole('heading', { name: 'Secrets', exact: true })).toBeVisible();
  await expect(page).toHaveURL(/\/Secrets$/);
});

test('Shared by me is served as the titled Angular document', async ({ page }) => {
  await openAsIssuer(page, '/Access');
  const response = await page.goto('/Secrets/SharedByMe');
  await expect(new SharedByMe(page).heading).toBeVisible();
  await expect(page).toHaveTitle('Shared by me — EclipsVault');
  expect(response?.headers()['content-security-policy']).toContain(
    "require-trusted-types-for 'script'",
  );
});

test('Shared by me lists shares newest first under the Legacy columns', async ({ page }) => {
  const shares = await openAsIssuer(page);
  await expect(shares.columns).toHaveText([
    'Secret',
    'Shared with',
    'Shared on',
    'Access until',
    '',
  ]);
  await expect(shares.secretOf(shares.rows.first())).toHaveText('E2e_Shared_Expiring');
  await expect(shares.secretOf(shares.rows.last())).toHaveText('E2e_Shared_No_Expiry');
  await expect(shares.recipientOf(shares.rows.first())).toHaveText(recipientUsername);
  await expect(shares.recipientOf(shares.rows.last())).toHaveText(recipientUsername);
  await expect(shares.sharedOnOf(shares.rows.first())).toHaveText(
    /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/,
  );
  await expect(shares.accessUntilOf(shares.rows.first())).toHaveText(
    /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/,
  );
});

test('Shared by me shows "no expiry" for a share without an expiry', async ({ page }) => {
  const shares = await openAsIssuer(page);
  await expect(shares.accessUntilOf(shares.share('E2e_Shared_No_Expiry'))).toHaveText('no expiry');
});

test('the sidebar opens Shared by me in-app with its Sharing group active', async ({ page }) => {
  await openAsIssuer(page, '/Access');
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(request.url());
  });
  await page.getByText('Sharing', { exact: true }).click();
  const link = page.getByRole('link', { name: 'Shared by me', exact: true });
  await link.click();
  await expect(new SharedByMe(page).heading).toBeVisible();
  await expect(link).toHaveAttribute('aria-current', 'page');
  await expect(page.getByRole('group').filter({ has: link })).toHaveJSProperty('open', true);
  expect(documents).toEqual([]);
});

test('the palette opens Shared by me without a document load', async ({ page }) => {
  await openAsIssuer(page, '/Access');
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(request.url());
  });
  await openPalette(page);
  await page.getByRole('combobox').fill('Shared by me');
  await page.keyboard.press('Enter');
  await expect(new SharedByMe(page).heading).toBeVisible();
  expect(documents).toEqual([]);
});

test('Shared with me opens Legacy on the same session from Shared by me', async ({ page }) => {
  await openAsIssuer(page);
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(new URL(request.url()).pathname);
  });
  await page.getByRole('link', { name: 'Shared with me', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Shared with me', exact: true })).toBeVisible();
  expect(documents).toEqual(['/Secrets/SharedWithMe']);
  await expect(page.getByRole('link', { name: 'Sign in', exact: true })).toHaveCount(0);
});

test('a mixed-case Shared by me bookmark serves the titled Angular page', async ({ page }) => {
  await openAsIssuer(page, '/sEcReTs/sHaReDbYmE');
  await expect(new SharedByMe(page).heading).toBeVisible();
  await expect(page).toHaveTitle('Shared by me — EclipsVault');
});

test('Shared by me reports a failed fetch and retries without a document load', async ({
  page,
  browserHealth,
}) => {
  await page.route(
    '**/api/ui/shared-by-me',
    (route) => route.fulfill({ status: 503, json: { code: 'internal_error' } }),
    { times: 1 },
  );
  browserHealth.expectConsoleError({ text: /status of 503/, url: /\/api\/ui\/shared-by-me$/ });
  const shares = await openAsIssuer(page);
  await expect(
    shares.error.filter({ hasText: 'Could not load your shares. Try again.' }),
  ).toBeVisible();
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(request.url());
  });
  await shares.retry.click();
  await expect(shares.rows).toHaveCount(2);
  await expect(shares.error).toBeEmpty();
  expect(documents).toEqual([]);
});

test('Cancel and Escape keep access while confirmation names the recipient and secret', async ({
  page,
}) => {
  const shares = await openAsIssuer(page);
  const revoke = shares
    .share('E2e_Shared_No_Expiry')
    .getByRole('button', { name: 'Revoke', exact: true });
  const writes: string[] = [];
  page.on('request', (request) => {
    if (request.method() === 'DELETE') writes.push(request.url());
  });
  await revoke.click();
  await expect(shares.confirmation).toHaveAccessibleName(
    "Revoke e2e-share-recipient's access to 'E2e_Shared_No_Expiry'? They lose access on their next request.",
  );
  await expect(shares.cancel).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(shares.confirmation).not.toBeVisible();
  await expect(revoke).toBeFocused();
  await revoke.click();
  await expect(shares.cancel).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(shares.continue).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(shares.cancel).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(shares.confirmation).not.toBeVisible();
  await expect(revoke).toBeFocused();
  expect(writes).toEqual([]);
});

test('a refused revocation announces the audit failure and keeps the share', async ({
  page,
  browserHealth,
}) => {
  const title = 'The audit trail is unavailable, so the vault refused the request.';
  await page.route('**/api/ui/shared-by-me/*', (route) =>
    route.fulfill({
      status: 503,
      contentType: 'application/problem+json',
      json: { status: 503, code: 'audit_unavailable', title },
    }),
  );
  browserHealth.expectConsoleError({ text: /status of 503/, url: /\/api\/ui\/shared-by-me\// });
  const shares = await openAsIssuer(page);
  await shares
    .share('E2e_Shared_No_Expiry')
    .getByRole('button', { name: 'Revoke', exact: true })
    .click();
  await shares.continue.click();
  await expect(shares.error).toHaveText(title);
  await expect(shares.rows).toHaveCount(2);
});

test('revoking a real share ends the recipient browser access on its next request', async ({
  page,
  browserHealth,
}) => {
  const recipient = await (await browserHealth.newContext()).newPage();
  await recipient.context().addCookies(recipientCookies);
  await recipient.goto(secretDetails);
  await expect(
    recipient.getByRole('heading', { name: 'E2e_Shared_No_Expiry', exact: true }),
  ).toBeVisible();
  const shares = await openAsIssuer(page);
  await shares
    .share('E2e_Shared_No_Expiry')
    .getByRole('button', { name: 'Revoke', exact: true })
    .click();
  const deleted = page.waitForRequest((request) => request.method() === 'DELETE');
  await shares.continue.click();
  expect((await deleted).headers()['requestverificationtoken']).toBeTruthy();
  await expect(shares.status).toHaveText('Access revoked.');
  await expect(shares.rows).toHaveCount(1);
  await expect(shares.share('E2e_Shared_No_Expiry')).toHaveCount(0);
  await recipient.goto(secretDetails);
  await expect(recipient).toHaveURL(/\/Account\/Denied\?/);
  await expect(
    recipient.getByRole('heading', { name: 'E2e_Shared_No_Expiry', exact: true }),
  ).toHaveCount(0);
  await recipient.goto('/Secrets/SharedWithMe');
  await expect(recipient.getByRole('row').filter({ hasText: 'E2e_Shared_No_Expiry' })).toHaveCount(
    0,
  );
  await expect(recipient.getByRole('row').filter({ hasText: 'E2e_Shared_Expiring' })).toBeVisible();
});

test('signed out, Shared by me bookmarks return through sign-in', async ({ page }) => {
  await page.goto('/Secrets/SharedByMe');
  await expect(page).toHaveURL(/\/Account\/Login\?ReturnUrl=%2FSecrets%2FSharedByMe$/);
});
