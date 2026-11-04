import type { Page } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { freshTotp, currentTotpStep } from './totp';

/** Long suites reuse a real session; refresh its proof explicitly before granting access. */
export async function refreshAdministratorProof(page: Page): Promise<void> {
  const secret = await readFile(
    `${process.env['E2E_TEST_RESULTS'] || 'test-results'}/users-administrator-authenticator.txt`,
    'utf8',
  );
  await page.request.get('/api/ui/me');
  const token = (await page.context().cookies()).find((cookie) => cookie.name === 'XSRF-TOKEN');
  if (!token) throw new Error('The signed-in browser has no antiforgery token');
  const response = await page.request.post('/api/ui/step-up', {
    headers: { RequestVerificationToken: decodeURIComponent(token.value) },
    data: { code: await freshTotp(secret, currentTotpStep()) },
  });
  if (response.status() !== 204)
    throw new Error(`Administrator proof answered ${response.status()}`);
}
