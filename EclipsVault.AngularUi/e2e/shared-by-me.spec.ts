import { refreshAdministratorProof } from './administrator-proof';
import { AccountCreation } from './account-creation';
import type { BrowserContext, Page } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { test, expect } from './fixtures';
import { expectLegacyShell, legacyShell } from './legacy-shell';
import { clearSignInBudget } from './profile-state';
import { createSecret, openPalette } from './palette';
import { SecretsList } from './secrets-list';
import { SharedByMe } from './shared-by-me';
import { SharedWithMe } from './shared-with-me';
import { freshTotp } from './totp';

test.describe.configure({ mode: 'serial' });
// Long and unbroken, so the width checks cover a name with no break opportunity of its own.
const recipientUsername = 'e2esharerecipientfromthefinancedirectoryofacme01';
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
  const creation = new AccountCreation(page);
  await refreshAdministratorProof(page);
  await creation.open();
  await creation.create({
    firstName: 'Share',
    lastName: 'Recipient',
    username: recipientUsername,
    password,
    clearance: 'Standard',
    project: 'E2E-RECIPIENT',
  });
  await expect(page).toHaveURL(/\/Admin\/Users$/);

  // Earlier sign-in scenarios use the same proxy source; keep the production limiter enabled.
  clearSignInBudget();
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
  // The page's own request for the secret is refused before the recipient lands on Denied.
  browserHealth.expectConsoleError({
    text: /status of 403/,
    url: /\/api\/ui\/secrets\/[0-9a-f-]{36}$/,
  });
  await recipient.goto(secretDetails);
  await expect(recipient).toHaveURL(/\/Account\/Denied\?/);
  await page.getByLabel('User to share with').fill(recipientUsername);
  await page.getByRole('button', { name: 'Share access', exact: true }).click();
  await expect(
    page.getByText(`Access to 'E2e_Shared_No_Expiry' was shared with ${recipientUsername}.`, {
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
    page.getByText(`Access to 'E2e_Shared_Expiring' was shared with ${recipientUsername}.`, {
      exact: true,
    }),
  ).toBeVisible();
});

for (const theme of ['dark', 'light']) {
  test(`Shared by me lists both shares and their revoke actions in ${theme}`, async ({
    page,
  }, info) => {
    await page.setViewportSize({ width: 1440, height: 1200 });
    await page
      .context()
      .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: process.env['E2E_BASE_URL']! }]);
    const shares = await openAsIssuer(page);
    await expect(shares.rows).toHaveCount(2);
    await expect(shares.heading).toBeVisible();
    await expect(shares.rows.getByRole('button', { name: 'Revoke', exact: true })).toHaveCount(2);
    const path = info.outputPath(`${theme}-shared-by-me.png`);
    await page.screenshot({ path, fullPage: true });
    await info.attach(`${theme}-shared-by-me`, { path, contentType: 'image/png' });
  });

  test(`Shared by me explains the empty list and offers the Secrets link in ${theme}`, async ({
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
  });
}

test('with nothing shared, Go to your secrets opens the Secrets page', async ({ page }) => {
  await page.context().addCookies(recipientCookies);
  await page.goto('/Secrets/SharedByMe');
  const shares = new SharedByMe(page);
  await shares.goToSecrets.click();
  await expect(new SecretsList(page).heading).toBeVisible();
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

test('Shared by me lists shares soonest-ending first, the close one as a yellow pill', async ({
  page,
}) => {
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
  // Shared for 7 days a moment ago, so it ends within the week: the pill counts the days left.
  await expect(shares.accessUntilOf(shares.rows.first())).toHaveText(
    /^In 6 days · \d{1,2} [A-Z][a-z]{2}( \d{4})?$/,
  );
  await expect(
    page.getByText(/^2 shares you gave · soonest ends \d{1,2} [A-Z][a-z]{2}/),
  ).toBeVisible();
});

test('Shared by me shows "No expiry" for a share without an expiry', async ({ page }) => {
  const shares = await openAsIssuer(page);
  await expect(shares.accessUntilOf(shares.share('E2e_Shared_No_Expiry'))).toHaveText('No expiry');
});

test('the Secrets card tab opens Shared by me in-app while keeping Secrets active in the sidebar', async ({
  page,
}) => {
  await openAsIssuer(page, '/Access');
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(request.url());
  });
  const secretsLink = page
    .getByRole('navigation', { name: 'Main navigation' })
    .getByRole('link', { name: 'Secrets', exact: true });
  await secretsLink.click();
  const tab = page
    .getByRole('navigation', { name: 'Secrets views' })
    .getByRole('link', { name: 'Shared by me', exact: true });
  await tab.click();
  await expect(new SharedByMe(page).heading).toBeVisible();
  await expect(tab).toHaveAttribute('aria-current', 'page');
  await expect(secretsLink).toHaveAttribute('aria-current', 'true');
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

test('the Legacy sidebar loads Shared by me into Angular', async ({ page, browserHealth }) => {
  expectLegacyShell(browserHealth);
  await openAsIssuer(page, legacyShell);
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(new URL(request.url()).pathname);
  });
  await page.getByText('Sharing', { exact: true }).click();
  await page.getByRole('link', { name: 'Shared by me', exact: true }).click();
  await expect(new SharedByMe(page).heading).toBeVisible();
  await expect(page).toHaveTitle('Shared by me — EclipsVault');
  expect(documents).toEqual(['/Secrets/SharedByMe']);
});

test('the Legacy palette loads Shared by me into Angular', async ({ page, browserHealth }) => {
  expectLegacyShell(browserHealth);
  await openAsIssuer(page, legacyShell);
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(new URL(request.url()).pathname);
  });
  await openPalette(page);
  await page.getByRole('combobox').fill('Shared by me');
  await page.getByRole('option', { name: /Shared by me/ }).click();
  await expect(new SharedByMe(page).heading).toBeVisible();
  expect(documents).toEqual(['/Secrets/SharedByMe']);
});

test('Shared with me opens in-app on the same session from Shared by me', async ({ page }) => {
  await openAsIssuer(page);
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(new URL(request.url()).pathname);
  });
  await page.getByRole('link', { name: 'Shared with me', exact: true }).click();
  await expect(new SharedWithMe(page).heading).toBeVisible();
  await expect(page).toHaveURL(/\/Secrets\/SharedWithMe$/);
  expect(documents).toEqual([]);
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
  await expect(shares.confirmation).toHaveAccessibleName(`Revoke ${recipientUsername}'s access?`);
  await expect(shares.confirmation).toHaveAccessibleDescription(
    `Revoke ${recipientUsername}'s access to 'E2e_Shared_No_Expiry'? They lose access on their next request.`,
  );
  await expect(shares.cancel).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(shares.confirmation).not.toBeVisible();
  await expect(revoke).toBeFocused();
  await revoke.click();
  await expect(shares.cancel).toBeFocused();
  await page.keyboard.press('Shift+Tab');
  await expect(shares.revokeAccess).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(shares.cancel).toBeFocused();
  await page.keyboard.press('Escape');
  await expect(shares.confirmation).not.toBeVisible();
  await expect(revoke).toBeFocused();
  expect(writes).toEqual([]);
});

test('on a phone each share is a card and Revoke opens a bottom sheet', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 800 });
  const shares = await openAsIssuer(page);
  const revoke = page.getByRole('button', { name: 'Revoke', exact: true });
  await expect(revoke).toHaveCount(2);
  await expect(revoke.first()).toBeVisible();
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(375);
  await revoke.first().click();
  await expect(shares.revokeAccess).toBeVisible();
  // The sheet slides up; once it settles it spans the width and sits on the bottom edge.
  await expect
    .poll(async () => {
      const box = await shares.confirmation.boundingBox();
      return box && [Math.round(box.x), Math.round(box.width), Math.round(box.y + box.height)];
    })
    .toEqual([0, 375, 800]);
  await page.keyboard.press('Escape');
  await expect(shares.confirmation).not.toBeVisible();
});

for (const width of [300, 2000]) {
  test(`Shared by me has no sideways scroll at ${width}px with shares listed`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await openAsIssuer(page);
    await expect(page.getByRole('button', { name: 'Revoke', exact: true })).toHaveCount(2);
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(width);
  });
}

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
  await shares.revokeAccess.click();
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
  await shares.revokeAccess.click();
  expect((await deleted).headers()['requestverificationtoken']).toBeTruthy();
  await expect(shares.status).toHaveText('Access revoked.');
  await expect(shares.rows).toHaveCount(1);
  await expect(shares.share('E2e_Shared_No_Expiry')).toHaveCount(0);
  // The recipient's next request for the secret is refused, so its page lands on Denied.
  browserHealth.expectConsoleError({
    text: /status of 403/,
    url: /\/api\/ui\/secrets\/[0-9a-f-]{36}$/,
  });
  await recipient.goto(secretDetails);
  await expect(recipient).toHaveURL(/\/Account\/Denied\?/);
  await expect(
    recipient.getByRole('heading', { name: 'E2e_Shared_No_Expiry', exact: true }),
  ).toHaveCount(0);
  // The served page fetches its rows, so the remaining share is seen before the revoked one's absence.
  await recipient.goto('/Secrets/SharedWithMe');
  await expect(recipient.getByRole('row').filter({ hasText: 'E2e_Shared_Expiring' })).toBeVisible();
  await expect(recipient.getByRole('row').filter({ hasText: 'E2e_Shared_No_Expiry' })).toHaveCount(
    0,
  );
});

test('signed out, Shared by me bookmarks return through sign-in', async ({ page }) => {
  await page.goto('/Secrets/SharedByMe');
  await expect(page).toHaveURL(/\/Account\/Login\?ReturnUrl=%2FSecrets%2FSharedByMe$/);
});
