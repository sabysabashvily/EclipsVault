import type { BrowserContext } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { test, expect } from './fixtures';
import { Networks, blockInRunStore } from './networks';
import { freshTotp, currentTotpStep } from './totp';
import { savedAdministratorCookies } from './administrator-cookies';
import { refreshAdministratorProof } from './administrator-proof';

test.describe.configure({ mode: 'serial' });
let administratorCookies: Awaited<ReturnType<BrowserContext['cookies']>> = [];
const range = '203.0.113.0/24';

// The saved administrator session signed in long before this project runs, so its proof is stale.
test('cancelling the proof for a new range restores focus and trusts nothing', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const networks = new Networks(page);
  await networks.open();
  await expect(networks.address).toHaveText(/^\S+$/);
  await expect(networks.trustBadge).toBeVisible();
  await networks.range.fill(range);
  await networks.label.fill('E2E office');
  browserHealth.expectConsoleError({ text: /403/, url: /\/api\/ui\/networks$/ });
  await networks.trust.click();
  await expect(networks.stepUp).toBeVisible();
  await networks.cancelProof();
  await expect(networks.trust).toBeFocused();
  await expect(networks.row(range)).toHaveCount(0);
});

test('a real authenticator code trusts the range once and clears the form', async ({
  page,
  browserHealth,
}) => {
  const directory = process.env['E2E_TEST_RESULTS'] || 'test-results';
  const secret = await readFile(`${directory}/users-administrator-authenticator.txt`, 'utf8');
  await page.context().addCookies(await savedAdministratorCookies());
  const networks = new Networks(page);
  await networks.open();
  await networks.range.fill(range);
  await networks.label.fill('E2E office');
  browserHealth.expectConsoleError({ text: /403/, url: /\/api\/ui\/networks$/ });
  await networks.trust.click();
  await networks.prove(await freshTotp(secret, currentTotpStep()));
  await expect(networks.status).toHaveText(`Trusted network ${range} added.`);
  await expect(networks.row(range)).toContainText('E2E office');
  await expect(networks.range).toHaveValue('');
  administratorCookies = await page.context().cookies();
});

test('cancelling Remove keeps the range trusted', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  const networks = new Networks(page);
  await networks.open();
  await networks.remove(range).click();
  await expect(networks.confirmation).toContainText(
    `Stop trusting ${range}? Requests from it will fail the network rule for Confidential+ secrets.`,
  );
  await networks.cancel();
  await expect(networks.row(range)).toBeVisible();
});

test('Remove after the Legacy confirmation stops trusting the range without proof', async ({
  page,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const networks = new Networks(page);
  await networks.open();
  await networks.remove(range).click();
  await networks.confirm();
  await expect(networks.status).toHaveText('Trusted network removed.');
  await expect(networks.row(range)).toHaveCount(0);
});

test('a block written into the run store shows the tripped decoy by name and its UTC time', async ({
  page,
}) => {
  await page.context().addCookies(administratorCookies);
  blockInRunStore('192.0.2.14/32', "Honey-token 'E2E_NETWORKS_DECOY' tripped");
  const networks = new Networks(page);
  await networks.open();
  await expect(networks.row('192.0.2.14/32')).toContainText(
    "Honey-token 'E2E_NETWORKS_DECOY' tripped",
  );
  await expect(networks.blockedSince('192.0.2.14/32')).toHaveText(/^\d{2}-\d{2} \d{2}:\d{2}$/);
});

test('Unblock after the Legacy confirmation lifts the block', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  await refreshAdministratorProof(page);
  const networks = new Networks(page);
  await networks.open();
  await networks.unblock('192.0.2.14/32').click();
  await expect(networks.confirmation).toContainText(
    'Lift the intrusion-defence block on 192.0.2.14/32?',
  );
  await networks.confirm();
  await expect(networks.status).toHaveText('Block on 192.0.2.14/32 lifted.');
  await expect(networks.row('192.0.2.14/32')).toHaveCount(0);
});

test('the Dashboard action opens Networks', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  const networks = new Networks(page);
  await networks.fromDashboard();
  await expect(networks.heading).toBeVisible();
  await expect(page).toHaveURL(/\/Admin\/Networks$/);
});

test('the Denied page link opens Networks', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  const networks = new Networks(page);
  await networks.fromDenied();
  await expect(networks.heading).toBeVisible();
});

test('the Legacy sidebar opens Networks', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  const networks = new Networks(page);
  await networks.fromSidebar('/Secrets');
  await expect(networks.heading).toBeVisible();
});

test('the Angular sidebar opens Networks', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  const networks = new Networks(page);
  await networks.fromSidebar('/Access');
  await expect(networks.heading).toBeVisible();
});

test('the palette in the Legacy shell opens Networks', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  const networks = new Networks(page);
  await networks.fromPalette('/Secrets');
  await expect(networks.heading).toBeVisible();
});

test('the palette in the Angular shell opens Networks', async ({ page }) => {
  await page.context().addCookies(administratorCookies);
  const networks = new Networks(page);
  await networks.fromPalette('/Access');
  await expect(networks.heading).toBeVisible();
});

const appearances = [
  { theme: 'dark', blocked: '192.0.2.15/32' },
  { theme: 'light', blocked: '192.0.2.16/32' },
] as const;
for (const { theme, blocked } of appearances) {
  test(`visual parity: Networks in ${theme} appearance`, async ({ page }, info) => {
    await page.context().addCookies(administratorCookies);
    blockInRunStore(blocked, 'Honey-token visual parity');
    const networks = new Networks(page);
    await networks.open();
    await networks.useTheme(theme);
    await expect(networks.row(blocked)).toBeVisible();
    await expect(networks.currentAddress).toHaveCSS('display', 'flex');
    await expect(networks.range).toHaveCSS('flex-grow', '1');
    await page.screenshot({ path: info.outputPath(`networks-${theme}.png`), fullPage: true });
  });

  test(`the Trust form is reachable in order by keyboard in ${theme} appearance`, async ({
    page,
  }) => {
    await page.context().addCookies(administratorCookies);
    const networks = new Networks(page);
    await networks.open();
    await networks.useTheme(theme);
    await networks.range.focus();
    await page.keyboard.press('Tab');
    await expect(networks.label).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(networks.trust).toBeFocused();
  });

  test(`Unblock confirms and lifts by keyboard in ${theme} appearance`, async ({ page }) => {
    await page.context().addCookies(administratorCookies);
    await refreshAdministratorProof(page);
    const networks = new Networks(page);
    await networks.open();
    await networks.useTheme(theme);
    await networks.unblock(blocked).focus();
    await page.keyboard.press('Enter');
    await expect(networks.confirmation).toContainText(
      `Lift the intrusion-defence block on ${blocked}?`,
    );
    await page.keyboard.press('Escape');
    await expect(networks.unblock(blocked)).toBeFocused();
    await page.keyboard.press('Enter');
    await expect(networks.keepBlock).toBeFocused();
    await page.keyboard.press('Tab');
    await page.keyboard.press('Enter');
    await expect(networks.status).toHaveText(`Block on ${blocked} lifted.`);
    await expect(networks.row(blocked)).toHaveCount(0);
  });
}
