import { readFile } from 'node:fs/promises';
import { test, expect } from './fixtures';
import { savedAdministratorCookies } from './administrator-cookies';
import { AuditLog } from './audit';
import { freshTotp, currentTotpStep } from './totp';

test.describe.configure({ mode: 'serial' });
const refusedExport = { text: /403/, url: /\/api\/ui\/audit\/export$/ };

// The saved administrator session signed in long before this project runs, so Export needs proof.
test('the Audit log lists the newest entries, starting with this opening of it', async ({
  page,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const audit = new AuditLog(page);
  await audit.open();
  await expect(audit.heading).toBeVisible();
  await expect(audit.summary).toBeVisible();
  await expect(audit.rows().first()).toContainText('Audit log viewed');
  await expect(audit.rows().first()).toContainText('vault-admin');
  await expect(audit.latestCheckpoint).toBeVisible();
});

test('Verify is anchored by the checkpoint just created', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const audit = new AuditLog(page);
  await audit.open();
  const created = page.waitForResponse('**/api/ui/audit/checkpoints');
  await audit.checkpoint.click();
  const { sequence } = (await (await created).json()) as { sequence: number };
  await expect(audit.status).toHaveText(
    new RegExp(`^Signed checkpoint created at sequence ${sequence} with key \\S+\\.$`),
  );
  await expect(audit.latestCheckpoint).toContainText(`sequence ${sequence} ·`);
  await audit.verify.click();
  await expect(audit.notice).toHaveClass(/notice-ok/);
  await expect(audit.notice).toContainText('✓ Hash chain intact.');
  await expect(audit.notice).toContainText(`The signed checkpoint at sequence ${sequence} matches`);
});

test('the filter keeps only matching rows', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const audit = new AuditLog(page);
  await audit.open();
  await audit.filter.fill('audit log viewed');
  await expect(audit.rows().first()).toBeVisible();
  await expect(audit.rows().filter({ hasNotText: 'Audit log viewed' })).toHaveCount(0);
  await audit.filter.fill('no entry says this');
  await expect(audit.rows()).toHaveCount(0);
});

test('cancelling the proof for Export restores focus and downloads nothing', async ({
  page,
  browserHealth,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const audit = new AuditLog(page);
  let downloads = 0;
  page.on('download', () => downloads++);
  await audit.open();
  browserHealth.expectConsoleError(refusedExport);
  await audit.export.click();
  await expect(audit.stepUp).toBeVisible();
  await audit.cancelProof();
  await expect(audit.export).toBeFocused();
  expect(downloads).toBe(0);
});

test('Export behind a real authenticator code downloads the v2 bundle under its dated name', async ({
  page,
  browserHealth,
}) => {
  const directory = process.env['E2E_TEST_RESULTS'] || 'test-results';
  const secret = await readFile(`${directory}/users-administrator-authenticator.txt`, 'utf8');
  await page.context().addCookies(await savedAdministratorCookies());
  const audit = new AuditLog(page);
  await audit.open();
  browserHealth.expectConsoleError(refusedExport);
  await audit.export.click();
  const downloaded = page.waitForEvent('download');
  await audit.prove(await freshTotp(secret, currentTotpStep()));

  const file = await downloaded;
  expect(file.suggestedFilename()).toMatch(/^eclipsvault-audit-bundle-\d{8}-\d{6}\.json$/);
  const bundle = JSON.parse(await readFile((await file.path())!, 'utf8')) as {
    SchemaVersion: string;
    PublicKeySpki: string;
    Checkpoint: { Sequence: number; ChainHeadHash: string };
    Rows: { Sequence: number; EntryHash: string }[];
  };
  expect(bundle.SchemaVersion).toBe('eclipsvault.audit-bundle/2');
  expect(bundle.PublicKeySpki).toMatch(/^[A-Za-z0-9+/]+=*$/);
  const last = bundle.Rows.at(-1);
  expect([last?.Sequence, last?.EntryHash]).toEqual([
    bundle.Checkpoint.Sequence,
    bundle.Checkpoint.ChainHeadHash,
  ]);
  await expect(audit.stepUp).toBeHidden();
});

for (const [name, link] of [
  ['critical-alerts card', /Critical alerts \(24 h\)/],
  ['Full audit log button', /^Full audit log$/],
] as const) {
  test(`the Dashboard ${name} opens the Audit log`, async ({ page }) => {
    await page.context().addCookies(await savedAdministratorCookies());
    const audit = new AuditLog(page);
    await audit.fromDashboard(link);
    await expect(audit.heading).toBeVisible();
    await expect(page).toHaveURL(/\/Audit$/);
  });
}

for (const shell of ['/Secrets', '/Access'] as const) {
  test(`the sidebar on ${shell} opens the Audit log`, async ({ page }) => {
    await page.context().addCookies(await savedAdministratorCookies());
    const audit = new AuditLog(page);
    await audit.fromSidebar(shell);
    await expect(audit.heading).toBeVisible();
    await expect(page).toHaveURL(/\/Audit$/);
  });

  test(`the palette on ${shell} opens the Audit log`, async ({ page }) => {
    await page.context().addCookies(await savedAdministratorCookies());
    const audit = new AuditLog(page);
    await audit.fromPalette(shell);
    await expect(audit.heading).toBeVisible();
  });
}

for (const theme of ['dark', 'light'] as const) {
  test(`visual parity: the Audit log in ${theme} appearance`, async ({ page }, info) => {
    await page.context().addCookies(await savedAdministratorCookies());
    const audit = new AuditLog(page);
    await audit.open();
    await audit.useTheme(theme);
    // eslint-disable-next-line playwright/no-raw-locators -- The theme is an attribute of the document element.
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
    await expect(audit.rows().first()).toBeVisible();
    await page.screenshot({ path: info.outputPath(`audit-${theme}.png`), fullPage: true });
  });

  test(`the Export proof opens, cycles and closes by keyboard in ${theme} appearance`, async ({
    page,
    browserHealth,
  }) => {
    await page.context().addCookies(await savedAdministratorCookies());
    const audit = new AuditLog(page);
    await audit.open();
    await audit.useTheme(theme);
    browserHealth.expectConsoleError(refusedExport);
    await audit.export.focus();
    await page.keyboard.press('Enter');
    const code = audit.stepUp.getByLabel('Authenticator code');
    await expect(code).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(audit.stepUp.getByRole('button', { name: 'Cancel', exact: true })).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(
      audit.stepUp.getByRole('button', { name: 'Verify and continue', exact: true }),
    ).toBeFocused();
    await page.keyboard.press('Tab');
    await expect(code).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(audit.stepUp).toBeHidden();
    await expect(audit.export).toBeFocused();
  });
}
