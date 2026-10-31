import type { BrowserContext, Page } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { test, expect } from './fixtures';
import { lookOf } from './look';
import { createSecret, openPalette } from './palette';
import { SecretsList } from './secrets-list';
import { SharedWithMe } from './shared-with-me';
import { freshTotp } from './totp';

test.describe.configure({ mode: 'serial' });
const recipientUsername = 'e2e-swm-recipient';
const underCleared = 'E2e_Under_Cleared_Share';
let recipientCookies: Awaited<ReturnType<BrowserContext['cookies']>> = [];

async function signInAsAdministrator(page: Page): Promise<void> {
  const state = JSON.parse(
    await readFile(
      `${process.env['E2E_TEST_RESULTS'] || 'test-results'}/sessions-administrator.json`,
      'utf8',
    ),
  ) as { cookies: Awaited<ReturnType<BrowserContext['cookies']>> };
  await page.context().addCookies(state.cookies);
}

async function openAsRecipient(page: Page, path = '/Secrets/SharedWithMe'): Promise<SharedWithMe> {
  await page.context().addCookies(recipientCookies);
  await page.goto(path);
  return new SharedWithMe(page);
}

async function shareWithRecipient(page: Page, secret: string, days?: string): Promise<void> {
  await page.getByLabel('User to share with').fill(recipientUsername);
  if (days) await page.getByLabel('Access expires in days').fill(days);
  await page.getByRole('button', { name: 'Share access', exact: true }).click();
  await expect(
    page.getByText(`Access to '${secret}' was shared with ${recipientUsername}.`, { exact: true }),
  ).toBeVisible();
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

test('a share whose recipient was later under-cleared is not listed for them', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(150_000);
  const password = `E2e-${randomBytes(24).toString('hex')}`;
  await signInAsAdministrator(page);
  await page.goto('/Admin/CreateUser');
  await page.getByLabel('First name').fill('Shared');
  await page.getByLabel('Last name').fill('Recipient');
  await page.getByLabel('Username').fill(recipientUsername);
  await page.getByLabel('Initial password', { exact: true }).fill(password);
  await page.getByLabel('Clearance').selectOption({ label: 'Elevated' });
  await page.getByLabel('Project').fill('E2E-SWM');
  await page.getByRole('button', { name: 'Create user', exact: true }).click();
  await expect(page).toHaveURL(/\/Admin\/Users$/);

  // Sharing refuses an under-cleared recipient, so the share is made first and the clearance
  // lowered after: the state a later change of role leaves behind.
  await createSecret(page, underCleared, 'Confidential');
  await shareWithRecipient(page, underCleared);
  await page.goto('/Admin/Users');
  await page
    .getByRole('row')
    .filter({ hasText: recipientUsername })
    .getByRole('link', { name: 'Edit', exact: true })
    .click();
  await page.getByLabel('Clearance').selectOption({ label: 'Standard' });
  await page.getByRole('button', { name: 'Save role', exact: true }).click();
  await expect(page).toHaveURL(/\/Admin\/Users$/);

  // Two credential posts: the earlier specs leave this window's sign-in budget far from spent.
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

  await recipient.goto('/Secrets/SharedWithMe');
  const shared = new SharedWithMe(recipient);
  await expect(shared.emptyHeading).toBeVisible();
  await expect(recipient.getByText(underCleared, { exact: true })).toHaveCount(0);
});

for (const [theme, accent, accentBackground] of [
  ['dark', 'rgb(255, 159, 10)', 'rgba(255, 159, 10, 0.14)'],
  ['light', 'rgb(180, 95, 4)', 'rgba(180, 95, 4, 0.12)'],
]) {
  test(`visual parity: Shared with me keeps the Legacy empty state and share icon in ${theme}`, async ({
    page,
  }, info) => {
    await useTheme(page, theme);
    const shared = await openAsRecipient(page);
    await expect(shared.heading).toBeVisible();
    await expect(shared.introduction).toBeVisible();
    await expect(shared.emptyHeading).toBeVisible();
    await expect(shared.emptyMessage).toHaveText(
      "This is where secrets from other projects show up when a colleague grants you access to them. There's nothing for you to do here until someone shares one.",
    );
    await expect(shared.goToSecrets).toHaveAttribute('href', '/Secrets');
    await expect(shared.emptyState).toHaveCSS('text-align', 'center');
    await expect(shared.emptyState).toHaveCSS('padding', '48px 24px');
    await expect(shared.emptyIcon).toHaveCSS('width', '54px');
    await expect(shared.emptyIcon).toHaveCSS('height', '54px');
    await expect(shared.emptyIcon).toHaveCSS('color', accent);
    await expect(shared.emptyIcon).toHaveCSS('background-color', accentBackground);
    const path = info.outputPath(`${theme}-shared-with-me-empty.png`);
    await page.screenshot({ path, fullPage: true });
    await info.attach(`${theme}-shared-with-me-empty`, { path, contentType: 'image/png' });
  });
}

test('with nothing shared, the keyboard reaches Go to your secrets and opens the Secrets page', async ({
  page,
}) => {
  const shared = await openAsRecipient(page);
  await shared.emptyHeading.click();
  await page.keyboard.press('Tab');
  await expect(shared.goToSecrets).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(new SecretsList(page).heading).toBeVisible();
  await expect(page).toHaveURL(/\/Secrets$/);
});

test('after an issuer shares, the recipient lists it under the Legacy columns and opens Details', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(120_000);
  await signInAsAdministrator(page);
  await createSecret(page, 'E2e_Shared_With_Me', 'Internal');
  await shareWithRecipient(page, 'E2e_Shared_With_Me');
  await createSecret(page, 'E2e_Shared_With_Me_Expiring', 'Internal');
  await shareWithRecipient(page, 'E2e_Shared_With_Me_Expiring', '7');

  const recipient = await (await browserHealth.newContext()).newPage();
  const shared = await openAsRecipient(recipient);
  await expect(shared.columns).toHaveText([
    'Name',
    'Project',
    'Environment',
    'Sensitivity',
    'Shared by',
    'Access until',
    '',
  ]);
  await expect(shared.rows).toHaveCount(2);
  await expect(shared.nameOf(shared.rows.first())).toHaveText('E2e_Shared_With_Me');
  await expect(shared.nameOf(shared.rows.last())).toHaveText('E2e_Shared_With_Me_Expiring');
  for (const name of ['E2e_Shared_With_Me', 'E2e_Shared_With_Me_Expiring']) {
    const row = shared.secret(name);
    await expect(shared.cellsOf(row).nth(1)).toHaveText('GLOBAL');
    await expect(shared.environmentBadgeOf(row, 'Development')).toBeVisible();
    await expect(shared.sensitivityBadgeOf(row, 'Internal')).toBeVisible();
    await expect(shared.cellsOf(row).nth(4)).toHaveText('vault-admin');
  }
  await expect(shared.accessUntilOf(shared.secret('E2e_Shared_With_Me'))).toHaveText('no expiry');
  await expect(shared.accessUntilOf(shared.secret('E2e_Shared_With_Me_Expiring'))).toHaveText(
    /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/,
  );
  await expect(recipient.getByText(underCleared, { exact: true })).toHaveCount(0);

  await shared.openOf(shared.secret('E2e_Shared_With_Me')).click();
  await expect(recipient).toHaveURL(/\/Secrets\/Details\/[0-9a-f-]{36}$/);
  await expect(
    recipient.getByRole('heading', { name: 'E2e_Shared_With_Me', exact: true }),
  ).toBeVisible();
});

for (const theme of ['dark', 'light']) {
  test(`visual parity: Shared with me rows look like the same secret's Legacy Secrets row in ${theme}`, async ({
    page,
  }, info) => {
    await page.setViewportSize({ width: 1440, height: 1200 });
    await useTheme(page, theme);
    const legacy = new SecretsList(page);
    await page.context().addCookies(recipientCookies);
    await legacy.open();
    const legacyRow = legacy.secret('E2e_Shared_With_Me');
    const expected = {
      heading: await lookOf(legacy.heading),
      introduction: await lookOf(legacy.introduction),
      column: await lookOf(legacy.columns.first()),
      name: await lookOf(legacy.nameOf(legacyRow)),
      environment: await lookOf(legacy.environmentBadgeOf(legacyRow, 'Development')),
      sensitivity: await lookOf(legacy.sensitivityBadgeOf(legacyRow, 'Internal')),
      open: await lookOf(legacy.openOf(legacyRow)),
    };

    const shared = await openAsRecipient(page);
    const row = shared.secret('E2e_Shared_With_Me');
    await expect(row).toBeVisible();
    expect({
      heading: await lookOf(shared.heading),
      introduction: await lookOf(shared.introduction),
      column: await lookOf(shared.columns.first()),
      name: await lookOf(shared.nameOf(row)),
      environment: await lookOf(shared.environmentBadgeOf(row, 'Development')),
      sensitivity: await lookOf(shared.sensitivityBadgeOf(row, 'Internal')),
      open: await lookOf(shared.openOf(row)),
    }).toEqual(expected);
    const path = info.outputPath(`${theme}-shared-with-me.png`);
    await page.screenshot({ path, fullPage: true });
    await info.attach(`${theme}-shared-with-me`, { path, contentType: 'image/png' });
  });
}

test('the keyboard reaches a row and opens its Legacy Details page', async ({ page }) => {
  const shared = await openAsRecipient(page);
  await shared.introduction.click();
  await page.keyboard.press('Tab');
  await expect(shared.openOf(shared.rows.first())).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(
    page.getByRole('heading', { name: 'E2e_Shared_With_Me', exact: true }),
  ).toBeVisible();
});

test('Shared with me is served as the titled Angular document, bookmarks in any case', async ({
  page,
}) => {
  await page.context().addCookies(recipientCookies);
  const response = await page.goto('/Secrets/SharedWithMe');
  await expect(new SharedWithMe(page).heading).toBeVisible();
  await expect(page).toHaveTitle('Shared with me — EclipsVault');
  expect(response?.headers()['content-security-policy']).toContain(
    "require-trusted-types-for 'script'",
  );
  await page.goto('/sEcReTs/sHaReDwItHmE');
  await expect(new SharedWithMe(page).rows).toHaveCount(2);
  await expect(page).toHaveTitle('Shared with me — EclipsVault');
});

test('the sidebar opens Shared with me in-app with its Sharing group active', async ({ page }) => {
  await openAsRecipient(page, '/Access');
  const documents = recordDocuments(page);
  await page.getByText('Sharing', { exact: true }).click();
  const link = page.getByRole('link', { name: 'Shared with me', exact: true });
  await link.click();
  await expect(new SharedWithMe(page).rows).toHaveCount(2);
  await expect(link).toHaveAttribute('aria-current', 'page');
  await expect(page.getByRole('group').filter({ has: link })).toHaveJSProperty('open', true);
  expect(documents).toEqual([]);
});

test('the palette opens Shared with me without a document load', async ({ page }) => {
  await openAsRecipient(page, '/Access');
  const documents = recordDocuments(page);
  await openPalette(page);
  await page.getByRole('combobox').fill('Shared with me');
  await page.getByRole('option', { name: /Shared with me/ }).click();
  await expect(new SharedWithMe(page).rows).toHaveCount(2);
  expect(documents).toEqual([]);
});

test('the Legacy sidebar and palette load Shared with me into Angular', async ({ page }) => {
  await openAsRecipient(page, '/Secrets');
  const documents = recordDocuments(page);
  await page.getByText('Sharing', { exact: true }).click();
  await page.getByRole('link', { name: 'Shared with me', exact: true }).click();
  await expect(new SharedWithMe(page).rows).toHaveCount(2);
  await expect(page).toHaveTitle('Shared with me — EclipsVault');
  expect(documents).toEqual(['/Secrets/SharedWithMe']);
  await page.goto('/Secrets');
  documents.length = 0;
  await openPalette(page);
  await page.getByRole('combobox').fill('Shared with me');
  await page.getByRole('option', { name: /Shared with me/ }).click();
  await expect(new SharedWithMe(page).rows).toHaveCount(2);
  expect(documents).toEqual(['/Secrets/SharedWithMe']);
});

test('Shared with me reports a failed fetch and retries without a document load', async ({
  page,
  browserHealth,
}) => {
  await page.route(
    '**/api/ui/shared-with-me',
    (route) => route.fulfill({ status: 503, json: { code: 'internal_error' } }),
    { times: 1 },
  );
  browserHealth.expectConsoleError({ text: /status of 503/, url: /\/api\/ui\/shared-with-me$/ });
  const shared = await openAsRecipient(page);
  await expect(shared.error).toHaveText('Could not load the secrets shared with you. Try again.');
  const documents = recordDocuments(page);
  await shared.retry.click();
  await expect(shared.rows).toHaveCount(2);
  await expect(shared.error).toHaveCount(0);
  expect(documents).toEqual([]);
});

test('signed out, a Shared with me bookmark returns through sign-in', async ({ page }) => {
  await page.goto('/Secrets/SharedWithMe');
  await expect(page).toHaveURL(/\/Account\/Login\?ReturnUrl=%2FSecrets%2FSharedWithMe$/);
});
