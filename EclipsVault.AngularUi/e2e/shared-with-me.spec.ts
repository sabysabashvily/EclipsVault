import { refreshAdministratorProof } from './administrator-proof';
import { signInNewAccount } from './account-activation';
import { AccountCreation } from './account-creation';
import { AppShell } from './app-shell';
import type { BrowserContext, Page } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { test, expect } from './fixtures';
import { createSecret, openPalette } from './palette';
import { SecretsList } from './secrets-list';
import { SharedByMe } from './shared-by-me';
import { SharedWithMe } from './shared-with-me';
import { Users } from './users';

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
  const creation = new AccountCreation(page);
  await refreshAdministratorProof(page);
  await creation.open();
  await creation.create({
    firstName: 'Shared',
    lastName: 'Recipient',
    username: recipientUsername,
    clearance: 'Elevated',
    project: 'E2E-SWM',
  });
  await expect(creation.status).toContainText('Invitation sent to');

  // Sharing refuses an under-cleared recipient, so the share is made first and the clearance
  // lowered after: the state a later change of role leaves behind.
  await createSecret(page, underCleared, 'Confidential');
  await shareWithRecipient(page, underCleared);
  const users = new Users(page);
  await users.open();
  await users.edit(recipientUsername);
  await page.getByLabel('Clearance').selectOption({ label: 'Standard' });
  await page.getByRole('button', { name: 'Save role', exact: true }).click();
  await expect(page).toHaveURL(/\/Admin\/Users$/);

  const recipient = await (await browserHealth.newContext()).newPage();
  await signInNewAccount(recipient, recipientUsername, password);
  await expect(recipient).toHaveURL(/\/Secrets$/);
  recipientCookies = await recipient.context().cookies();

  await recipient.goto('/Secrets/SharedWithMe');
  const shared = new SharedWithMe(recipient);
  await expect(shared.emptyHeading).toBeVisible();
  await expect(recipient.getByText(underCleared, { exact: true })).toHaveCount(0);
});

for (const theme of ['dark', 'light']) {
  test(`Shared with me explains the empty list and offers the Secrets link in ${theme}`, async ({
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

test('after an issuer shares, the recipient lists it soonest-ending first and opens Details', async ({
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
  await expect(shared.context).toHaveText(
    /^2 secrets shared with you · soonest ends \d{1,2} [A-Z][a-z]{2}( \d{4})?\.$/,
  );
  await expect(shared.columns).toHaveText([
    'Secret',
    'Environment',
    'Sensitivity',
    'Shared by',
    'Access until',
  ]);
  await expect(shared.rows).toHaveCount(2);
  await expect(shared.openOf(shared.rows.first())).toHaveText('E2e_Shared_With_Me_Expiring');
  await expect(shared.openOf(shared.rows.last())).toHaveText('E2e_Shared_With_Me');
  const expiring = shared.secret('E2e_Shared_With_Me_Expiring');
  await expect(shared.secretCellOf(expiring)).toContainText('GLOBAL');
  await expect(shared.environmentBadgeOf(expiring, 'Development')).toBeVisible();
  await expect(shared.sensitivityBadgeOf(expiring, 'Internal')).toBeVisible();
  await expect(shared.sharedByOf(expiring)).toHaveText('vault-admin');
  // Shared for 7 days a moment ago, so it ends within the week: the pill counts the days left.
  await expect(shared.accessUntilOf(expiring)).toHaveText(
    /^In 6 days · \d{1,2} [A-Z][a-z]{2}( \d{4})?$/,
  );
  const withoutExpiry = shared.secret('E2e_Shared_With_Me');
  await expect(shared.secretCellOf(withoutExpiry)).toContainText('GLOBAL');
  await expect(shared.environmentBadgeOf(withoutExpiry, 'Development')).toBeVisible();
  await expect(shared.sensitivityBadgeOf(withoutExpiry, 'Internal')).toBeVisible();
  await expect(shared.sharedByOf(withoutExpiry)).toHaveText('vault-admin');
  await expect(shared.accessUntilOf(withoutExpiry)).toHaveText('No expiry');
  await expect(recipient.getByText(underCleared, { exact: true })).toHaveCount(0);

  await shared.openOf(withoutExpiry).click();
  await expect(recipient).toHaveURL(/\/Secrets\/Details\/[0-9a-f-]{36}$/);
  await expect(
    recipient.getByRole('heading', { name: 'E2e_Shared_With_Me', exact: true }),
  ).toBeVisible();
});

for (const theme of ['dark', 'light']) {
  test(`Shared with me shows the secret's scope and issuer in ${theme}`, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1200 });
    await useTheme(page, theme);
    const shared = await openAsRecipient(page);
    const row = shared.secret('E2e_Shared_With_Me');
    await expect(row).toBeVisible();
    await expect(shared.secretCellOf(row)).toContainText('GLOBAL');
    await expect(shared.sharedByOf(row)).toHaveText('vault-admin');
    await expect(shared.environmentBadgeOf(row, 'Development')).toBeVisible();
    await expect(shared.sensitivityBadgeOf(row, 'Internal')).toBeVisible();
    await expect(shared.openOf(row)).toBeVisible();
    const path = info.outputPath(`${theme}-shared-with-me.png`);
    await page.screenshot({ path, fullPage: true });
    await info.attach(`${theme}-shared-with-me`, { path, contentType: 'image/png' });
  });
}

test('the keyboard reaches the first row and opens its Details page', async ({ page }) => {
  const shared = await openAsRecipient(page);
  await shared.heading.click();
  await page.keyboard.press('Tab');
  await expect(shared.tab('All secrets')).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(shared.tab('Shared with me')).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(shared.tab('Shared by me')).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(shared.openOf(shared.rows.first())).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(
    page.getByRole('heading', { name: 'E2e_Shared_With_Me_Expiring', exact: true }),
  ).toBeVisible();
});

test('a click anywhere on a row, not only its name, opens the secret', async ({ page }) => {
  const shared = await openAsRecipient(page);

  // The name's link stretches over the row, so the cell is covered: click where a person would,
  // and let the topmost element, the link, take it.
  await shared.sharedByOf(shared.secret('E2e_Shared_With_Me')).click({ force: true });

  await expect(page).toHaveURL(/\/Secrets\/Details\/[0-9a-f-]{36}$/);
  await expect(
    page.getByRole('heading', { name: 'E2e_Shared_With_Me', exact: true }),
  ).toBeVisible();
});

test('on a phone each share is a card, its pills under the name', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 800 });
  const shared = await openAsRecipient(page);
  const row = shared.secret('E2e_Shared_With_Me');
  await expect(shared.rows).toHaveCount(2);
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(375);
  const name = (await shared.openOf(row).boundingBox())!;
  const environment = (await shared.environmentBadgeOf(row, 'Development').boundingBox())!;
  expect(environment.y).toBeGreaterThan(name.y + name.height);
});

for (const width of [300, 2000]) {
  test(`Shared with me has no sideways scroll at ${width}px with shares listed`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 900 });
    const shared = await openAsRecipient(page);
    await expect(shared.rows).toHaveCount(2);
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(width);
  });
}

test('Shared with me is served as the titled Angular document', async ({ page }) => {
  await page.context().addCookies(recipientCookies);
  const response = await page.goto('/Secrets/SharedWithMe');
  await expect(new SharedWithMe(page).heading).toBeVisible();
  await expect(page).toHaveTitle('Shared with me — EclipsVault');
  expect(response?.headers()['content-security-policy']).toContain(
    "require-trusted-types-for 'script'",
  );
});

test('a mixed-case Shared with me bookmark serves the titled Angular page', async ({ page }) => {
  await openAsRecipient(page, '/sEcReTs/sHaReDwItHmE');
  await expect(new SharedWithMe(page).rows).toHaveCount(2);
  await expect(page).toHaveTitle('Shared with me — EclipsVault');
});

test('the Secrets card tab opens Shared with me in-app while keeping Secrets active in the sidebar', async ({
  page,
}) => {
  await openAsRecipient(page, '/Access');
  const documents = recordDocuments(page);
  const shell = new AppShell(page);
  await shell.secrets.click();
  const tab = new SecretsList(page).tab('Shared with me');
  await tab.click();
  await expect(new SharedWithMe(page).rows).toHaveCount(2);
  await expect(tab).toHaveAttribute('aria-current', 'page');
  await expect(shell.secrets).toHaveAttribute('aria-current', 'true');
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

test('Shared by me opens in-app on the same session from Shared with me', async ({ page }) => {
  await openAsRecipient(page);
  await expect(new SharedWithMe(page).rows).toHaveCount(2);
  const documents = recordDocuments(page);
  await page.getByRole('link', { name: 'Shared by me', exact: true }).click();
  await expect(new SharedByMe(page).heading).toBeVisible();
  await expect(page).toHaveURL(/\/Secrets\/SharedByMe$/);
  expect(documents).toEqual([]);
  await expect(page.getByRole('link', { name: 'Sign in', exact: true })).toHaveCount(0);
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
