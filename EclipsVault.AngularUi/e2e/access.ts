import type { Page, TestInfo } from '@playwright/test';
import type { paths } from '../src/app/ui-api/schema.generated';
import { expect } from './fixtures';

/** The actual grid and explanation in each theme, with literal expectations for both roles. */
export async function verifyMyAccess(
  page: Page,
  info: TestInfo,
  account: 'administrator' | 'standard',
  theme: string,
) {
  await expect(page).toHaveTitle('My access — EclipsVault');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  const you = page
    .locator('section')
    .filter({ has: page.getByRole('heading', { name: 'You', exact: true }) });
  const clearance = account === 'administrator' ? 'Top Secret' : 'Standard';
  await expect(you.locator('dd').nth(0)).toHaveText(
    `${clearance} — you can open secrets classified up to ${clearance}.`,
  );
  await expect(you.locator('dd').nth(1)).toHaveText('GLOBAL');
  await expect(you.locator('dd').nth(2)).toHaveText(
    account === 'administrator'
      ? 'Your TopSecret clearance lets you reach secrets in any project.'
      : 'You can only reach secrets in GLOBAL, unless a secret is explicitly shared with you.',
  );
  const now = page
    .locator('section')
    .filter({ has: page.getByRole('heading', { name: 'Right now', exact: true }) });
  const response = await page.request.get('/api/ui/access');
  expect(response.status()).toBe(200);
  const access: paths['/api/ui/access']['get']['responses'][200]['content']['application/json'] =
    await response.json();
  // Docker allocates the proxy's forwarded source address; it is not necessarily loopback.
  expect(access.isTrustedNetwork).toBe(true);
  await expect(now.locator('dd').nth(0)).toHaveText(`${access.sourceIp} Trusted`);
  await expect(now.locator('dd').nth(1)).toHaveText(/^\s*(Open now|Closed)\s+06:00–22:00 UTC\s*$/);
  const production = (await now.locator('dd').nth(1).innerText()).includes('Open now')
    ? 'Can open'
    : 'Outside access hours';
  const rows = page.locator('.access-matrix tbody tr');
  await expect(page.locator('.access-matrix thead th')).toHaveText([
    'Sensitivity',
    'Development',
    'Staging',
    'Production',
  ]);
  await expect(rows.locator('th')).toHaveText(['Top Secret', 'Secret', 'Confidential', 'Internal']);
  for (let row = 0; row < 4; row++) {
    const values =
      account === 'standard' && row < 3
        ? ['Clearance too low', 'Clearance too low', 'Clearance too low']
        : ['Can open', 'Can open', production];
    await expect(rows.nth(row).locator('td')).toHaveText(values);
  }
  await expect(page.locator('.callout-warn')).toContainText(
    'opening a decoy always trips the trap',
  );
  await expect(page.locator('style, [style]')).toHaveCount(0);
  // The allow/deny colors must resolve through the shared theme tokens, including the green fill.
  const colors = await page.locator('.access-matrix').evaluate((grid) => {
    const cell = grid.querySelector('.cell-allow')!;
    const tokens = getComputedStyle(document.documentElement);
    return [
      getComputedStyle(cell).color,
      tokens.getPropertyValue('--ok').trim(),
      getComputedStyle(cell).backgroundColor,
    ];
  });
  expect(colors).toEqual(
    theme === 'dark'
      ? ['rgb(48, 209, 88)', '#30d158', 'rgba(48, 209, 88, 0.14)']
      : ['rgb(26, 127, 55)', '#1a7f37', 'rgba(26, 127, 55, 0.13)'],
  );
  await page.evaluate(() => document.fonts.ready);
  const path = info.outputPath(`${account}-${theme}-access.png`);
  await page.screenshot({ path, fullPage: true });
  await info.attach(`${account}-${theme}-access`, { path, contentType: 'image/png' });
}
