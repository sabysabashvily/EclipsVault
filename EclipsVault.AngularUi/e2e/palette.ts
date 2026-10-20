import { execFileSync } from 'node:child_process';
import type { Page } from '@playwright/test';
import { expect } from './fixtures';

/** Opens the palette with the platform's chord, as a person would, and waits for its field. */
export async function openPalette(page: Page): Promise<void> {
  await expect(
    page.getByRole('button', { name: 'Search secrets or jump to a page', exact: true }),
  ).toBeVisible();
  await page.keyboard.press('ControlOrMeta+k');
  await expect(page.getByRole('dialog', { name: 'Command palette' })).toBeVisible();
  await expect(page.locator('.palette-input')).toBeFocused();
}

/** The palette's rows as a person reads them: what kind of thing, and its name. */
export async function paletteEntries(page: Page): Promise<string[][]> {
  return page
    .locator('.palette-item')
    .evaluateAll((items) =>
      items.map((item) => [
        item.querySelector('.palette-kind')?.textContent?.trim() ?? '',
        item.querySelector('.palette-label')?.textContent?.trim() ?? '',
      ]),
    );
}

/** Every page link the sidebar renders, nav then account menu, in document order. */
export async function sidebarPages(page: Page): Promise<string[]> {
  return (await page.locator('.sidenav a, .account-menu-panel a').allTextContents()).map((text) =>
    text.trim(),
  );
}

/** Types a query and returns the secret search it causes, once the palette has rendered its answer. */
export async function searchSecrets(
  page: Page,
  query: string,
): Promise<{ status: number; names: string[] }> {
  const answered = page.waitForResponse((response) => {
    const url = new URL(response.url());
    return url.pathname === '/Secrets/Search' && url.searchParams.get('q') === query;
  });
  await page.locator('.palette-input').fill(query);
  const response = await answered;
  const names = ((await response.json()) as { name: string }[]).map((secret) => secret.name);
  await expect
    .poll(async () =>
      (await paletteEntries(page)).filter(([kind]) => kind === 'Secret').map(([, label]) => label),
    )
    .toEqual(names);
  return { status: response.status(), names };
}

/** Creates a secret through the Legacy UI's own form, as the signed-in administrator. */
export async function createSecret(page: Page, name: string, sensitivity: string): Promise<void> {
  await page.goto('/Secrets/Create');
  await page.getByLabel('Name', { exact: true }).fill(name);
  await page.getByLabel('Secret value').fill(`e2e-${name}`);
  await page.getByLabel('Project key').fill('GLOBAL');
  await page.getByLabel('Environment').selectOption({ label: 'Development' });
  await page.getByLabel('Sensitivity').selectOption({ label: sensitivity });
  await page.getByRole('button', { name: 'Encrypt and store' }).click();
  await expect(page.getByRole('heading', { name, exact: true })).toBeVisible();
}

/**
 * Turns a secret into a honey-token decoy. A production vault has no screen that makes one (only the
 * Development seed does), so the test flips the flag in this run's disposable database. The
 * password stays inside the SQL container: the shell there reads it from its own environment.
 */
export function markDecoy(name: string): void {
  if (!/^\w+$/.test(name)) throw new Error('Decoy names are plain identifiers');
  const project = process.env['E2E_COMPOSE_PROJECT'];
  if (!project) throw new Error('Run through npm run e2e, which names the compose project');
  const sql = `SET NOCOUNT ON; UPDATE Secrets SET IsHoneyToken = 1 WHERE Name = N'${name}'; SELECT @@ROWCOUNT;`;
  const updated = execFileSync(
    'docker',
    [
      'compose',
      '--project-name',
      project,
      '--file',
      'e2e/compose.yml',
      'exec',
      '-T',
      'sql',
      'sh',
      '-c',
      `/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d EclipsVault -h -1 -Q "${sql}"`,
    ],
    { encoding: 'utf8' },
  );
  expect(updated.trim()).toBe('1');
}
