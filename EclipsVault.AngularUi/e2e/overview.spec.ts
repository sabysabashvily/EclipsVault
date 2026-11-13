import type { BrowserContext, Page } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { AccountCreation } from './account-creation';
import { AccountSignIn } from './account-sign-in';
import { savedAdministratorCookies } from './administrator-cookies';
import { refreshAdministratorProof } from './administrator-proof';
import { AuthenticatorEnrollment } from './authenticator-enrollment';
import { test, expect } from './fixtures';
import { NewSecretForm } from './new-secret';
import { Overview } from './overview';
import { freshTotp } from './totp';

test.describe.configure({ mode: 'serial' });
const memberUsername = 'e2e-overview-member';
const project = 'E2E-OVERVIEW';
const expiring = 'E2e_Overview_Expiring';
let memberCookies: Awaited<ReturnType<BrowserContext['cookies']>> = [];

/** Records each document the browser loads, so a test can tell an in-app link from a page load. */
function recordDocuments(page: Page): string[] {
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(new URL(request.url()).pathname);
  });
  return documents;
}

test('an administrator gets the vault figures, its activity and the administration links', async ({
  page,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const overview = new Overview(page);
  const document = await page.goto('/');
  expect(document?.headers()['content-security-policy']).toContain(
    "require-trusted-types-for 'script'",
  );
  await expect(page).toHaveTitle('Overview — EclipsVault');
  await expect(overview.heading).toBeVisible();
  await expect(overview.welcome).toBeVisible();
  await expect(overview.sidebarOverview).toHaveAttribute('aria-current', 'page');
  await expect(overview.criticalAlertsLink).toHaveAttribute('href', '/Audit');
  await expect(overview.staffAccounts).toHaveAttribute('href', '/Admin/Users');
  await expect(overview.vaultActivity).toBeVisible();
  await expect(overview.fullAuditLog).toHaveAttribute('href', '/Audit');
  await expect(overview.provisionUser).toHaveAttribute('href', '/Admin/CreateUser');
  await expect(overview.manageNetworks).toHaveAttribute('href', '/Admin/Networks');
  await expect(overview.newSecret).toHaveAttribute('href', '/Secrets/Create');
  await expect(overview.browseSecrets).toHaveAttribute('href', '/Secrets');
});

const angularLinks = [
  ['the critical-alerts card', 'criticalAlertsLink', /\/Audit$/, 'Audit log'],
  ['the staff-accounts card', 'staffAccounts', /\/Admin\/Users$/, 'Users'],
  ['Full audit log', 'fullAuditLog', /\/Audit$/, 'Audit log'],
  ['Provision a user', 'provisionUser', /\/Admin\/CreateUser$/, 'New user'],
  ['Manage trusted networks', 'manageNetworks', /\/Admin\/Networks$/, 'Networks'],
] as const;
for (const [name, link, url, heading] of angularLinks) {
  test(`${name} opens ${heading} without loading another document`, async ({ page }) => {
    await page.context().addCookies(await savedAdministratorCookies());
    const overview = new Overview(page);
    await overview.open();
    await expect(overview.heading).toBeVisible();
    const documents = recordDocuments(page);
    await overview[link].click();
    await expect(page.getByRole('heading', { name: heading, exact: true })).toBeVisible();
    await expect(page).toHaveURL(url);
    expect(documents).toEqual([]);
  });
}

test('Browse secrets opens the Legacy Secrets page', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const overview = new Overview(page);
  await overview.open();
  await expect(overview.heading).toBeVisible();
  const documents = recordDocuments(page);
  await overview.browseSecrets.click();
  await expect(page.getByRole('heading', { name: 'Secrets', exact: true })).toBeVisible();
  expect(documents).toEqual(['/Secrets']);
});

test('a non-administrator sees their own activity, the critical count and their expiring secret, with no administration links', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  const password = `E2e-${randomBytes(24).toString('hex')}`;
  await page.context().addCookies(await savedAdministratorCookies());
  // Sign-in's passkey scenarios spend the shared source's credential budget; start a fresh window.
  // Its proof, sign-in and enrollment are three posts, which leave the next project's budget intact.
  const windowMilliseconds = 120_000;
  await page.waitForTimeout(windowMilliseconds - (Date.now() % windowMilliseconds) + 1_000);
  await refreshAdministratorProof(page);
  const creation = new AccountCreation(page);
  await creation.open();
  await creation.create({
    firstName: 'Overview',
    lastName: 'Member',
    username: memberUsername,
    password,
    clearance: 'Standard',
    project,
  });
  await expect(page).toHaveURL(/\/Admin\/Users$/);
  const newSecret = new NewSecretForm(page);
  await newSecret.open();
  await newSecret.create({ name: expiring, project, sensitivity: 'Internal', ttlDays: '1' });
  await expect(page.getByRole('heading', { name: expiring, exact: true })).toBeVisible();

  const member = await (await browserHealth.newContext()).newPage();
  const signIn = new AccountSignIn(member);
  await signIn.open();
  await signIn.submitPassword(memberUsername, password);
  await expect(member).toHaveURL(/\/Account\/EnrollTotp$/);
  const enrollment = new AuthenticatorEnrollment(member);
  await enrollment.confirm(await freshTotp(await enrollment.secret.innerText()));
  await expect(member).toHaveURL(/\/Secrets$/);
  memberCookies = await member.context().cookies();

  const overview = new Overview(member);
  await overview.open();
  await expect(overview.ownActivity).toBeVisible();
  await expect(overview.activity.filter({ hasText: 'TOTP enrolled' })).toContainText(
    memberUsername,
  );
  await expect(overview.notice).toHaveText(
    '1 secret will expire within 7 days and be shredded automatically. Rotate them to extend their life.',
  );
  await expect(overview.activeSecrets).toContainText('1 dev · 0 staging · 0 prod');
  await expect(overview.expiringRow(expiring)).toContainText('Development');
  await expect(overview.criticalAlerts).toBeVisible();
  await expect(overview.criticalAlertsLink).toHaveCount(0);
  await expect(overview.staffAccounts).toHaveCount(0);
  await expect(overview.vaultActivity).toHaveCount(0);
  await expect(overview.fullAuditLog).toHaveCount(0);
  await expect(overview.provisionUser).toHaveCount(0);
  await expect(overview.manageNetworks).toHaveCount(0);
});

test("a non-administrator's Rotate link opens the expiring secret's details", async ({ page }) => {
  expect(memberCookies).not.toHaveLength(0);
  await page.context().addCookies(memberCookies);
  const overview = new Overview(page);
  await overview.open();
  await overview.expiringRow(expiring).getByRole('link', { name: 'Rotate', exact: true }).click();
  await expect(page.getByRole('heading', { name: expiring, exact: true })).toBeVisible();
  await expect(page).toHaveURL(/\/Secrets\/Details\/[0-9a-f-]{36}$/);
});

test('the keyboard reaches the figures in order and Enter opens the Audit log', async ({
  page,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const overview = new Overview(page);
  await overview.open();
  await overview.heading.click();
  await page.keyboard.press('Tab');
  await expect(overview.newSecret).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(overview.activeSecrets).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(overview.expiringCard).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(overview.criticalAlertsLink).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: 'Audit log', exact: true })).toBeVisible();
});

for (const theme of ['dark', 'light'] as const) {
  test(`visual parity: the Overview keeps its activity list and quick actions in ${theme}`, async ({
    page,
  }, info) => {
    await page.setViewportSize({ width: 1440, height: 1200 });
    expect(memberCookies).not.toHaveLength(0);
    await page.context().addCookies(memberCookies);
    const overview = new Overview(page);
    await overview.open();
    await overview.useTheme(theme);
    // eslint-disable-next-line playwright/no-raw-locators -- The theme is an attribute of the document element.
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
    await expect(overview.expiringSoon).toBeVisible();
    await expect(overview.activityList).toHaveCSS('list-style-type', 'none');
    await expect(overview.activity.first()).toHaveCSS('display', 'flex');
    await expect(overview.browseSecrets).toHaveCSS('display', 'flex');
    await expect(overview.browseSecrets).toHaveCSS('flex-direction', 'column');
    const path = info.outputPath(`${theme}-overview.png`);
    await page.screenshot({ path, fullPage: true });
    await info.attach(`${theme}-overview`, { path, contentType: 'image/png' });
  });
}
