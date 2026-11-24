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
    .filter({ has: page.getByRole('heading', { name: 'You · right now', exact: true }) });
  const response = await page.request.get('/api/ui/access');
  expect(response.status()).toBe(200);
  const access: paths['/api/ui/access']['get']['responses'][200]['content']['application/json'] =
    await response.json();
  // Docker allocates the proxy's forwarded source address; it is not necessarily loopback.
  expect(access.isTrustedNetwork).toBe(true);
  await expect(you.getByRole('definition')).toHaveText([
    account === 'administrator' ? 'Top Secret' : 'Standard',
    'GLOBAL',
    account === 'administrator' ? 'Any project' : 'Only GLOBAL, unless shared with you',
    `${access.sourceIp} Trusted`,
    '00:00–24:00 UTC Open now',
  ]);
  const rows = page.locator('.access-matrix tbody tr');
  await expect(page.locator('.access-matrix thead th')).toHaveText([
    'Sensitivity',
    'Development',
    'Staging',
    'Production',
  ]);
  await expect(rows.locator('th')).toHaveText(['Top Secret', 'Secret', 'Confidential', 'Internal']);
  // One row per sensitivity, Top Secret first; each row lists Development, Staging, Production.
  const grids = {
    administrator: [
      ['Can open', 'Can open', 'Can open'],
      ['Can open', 'Can open', 'Can open'],
      ['Can open', 'Can open', 'Can open'],
      ['Can open', 'Can open', 'Can open'],
    ],
    standard: [
      ['Clearance too low', 'Clearance too low', 'Clearance too low'],
      ['Clearance too low', 'Clearance too low', 'Clearance too low'],
      ['Clearance too low', 'Clearance too low', 'Clearance too low'],
      ['Can open', 'Can open', 'Can open'],
    ],
  };
  await expect(rows.locator('td')).toHaveText(grids[account].flat());
  await expect(
    page.getByRole('region', { name: 'Decoys always trip the trap', exact: true }),
  ).toContainText('opening a decoy always trips the trap');
  // Only a denied cell has something to explain.
  await expect(page.getByRole('button', { name: /^Can open/ })).toHaveCount(0);
  await expect(page.locator('style, [style]')).toHaveCount(0);
  await page.evaluate(() => document.fonts.ready);
  const path = info.outputPath(`${account}-${theme}-access.png`);
  await page.screenshot({ path, fullPage: true });
  await info.attach(`${account}-${theme}-access`, { path, contentType: 'image/png' });
}
