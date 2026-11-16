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
  await expect(now.locator('dd').nth(1)).toHaveText('Open now 00:00–24:00 UTC');
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
  await expect(page.locator('.callout-warn')).toContainText(
    'opening a decoy always trips the trap',
  );
  await expect(page.locator('style, [style]')).toHaveCount(0);
  await page.evaluate(() => document.fonts.ready);
  const path = info.outputPath(`${account}-${theme}-access.png`);
  await page.screenshot({ path, fullPage: true });
  await info.attach(`${account}-${theme}-access`, { path, contentType: 'image/png' });
}
