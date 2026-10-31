import { defineConfig, devices } from '@playwright/test';

const evidence = process.env['E2E_EVIDENCE_DIR'] || '.';
const probes = process.env['E2E_VERIFY_GUARDS'] === '1';

export default defineConfig({
  testDir: './e2e',
  testMatch: probes ? '**/*.probe.ts' : '**/*.spec.ts',
  forbidOnly: !!process.env['CI'],
  workers: 1,
  retries: 0,
  timeout: 90_000,
  expect: { timeout: 10_000 },
  outputDir: `${evidence}/${probes ? 'guard-results' : 'test-results'}/artifacts`,
  reporter: probes
    ? [['list'], ['json', { outputFile: `${evidence}/guard-results/results.json` }]]
    : [['list'], ['html', { outputFolder: `${evidence}/playwright-report`, open: 'never' }]],
  use: {
    baseURL: process.env['E2E_BASE_URL'],
    // Only the disposable proxy's locally issued certificate is untrusted. HTTPS and the vault's
    // Secure cookies, CSP and production forwarded-header handling are exercised unchanged.
    ignoreHTTPSErrors: true,
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: probes
    ? [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }]
    : [
        {
          name: 'chromium',
          testIgnore: [
            '**/sessions.spec.ts',
            '**/data.spec.ts',
            '**/shared-by-me.spec.ts',
            '**/shared-with-me.spec.ts',
            '**/mailbox.spec.ts',
            '**/profile.spec.ts',
          ],
          use: { ...devices['Desktop Chrome'] },
        },
        {
          name: 'profile',
          testMatch: '**/profile.spec.ts',
          dependencies: ['mailbox'],
          use: { ...devices['Desktop Chrome'] },
        },
        {
          name: 'mailbox',
          testMatch: '**/mailbox.spec.ts',
          dependencies: ['shared-with-me'],
          use: { ...devices['Desktop Chrome'] },
        },
        {
          name: 'sessions',
          testMatch: '**/sessions.spec.ts',
          dependencies: ['chromium'],
          use: { ...devices['Desktop Chrome'] },
        },
        {
          name: 'data',
          testMatch: '**/data.spec.ts',
          dependencies: ['chromium'],
          use: { ...devices['Desktop Chrome'] },
        },
        {
          name: 'shared-by-me',
          testMatch: '**/shared-by-me.spec.ts',
          dependencies: ['sessions', 'data'],
          use: { ...devices['Desktop Chrome'] },
        },
        {
          name: 'shared-with-me',
          testMatch: '**/shared-with-me.spec.ts',
          dependencies: ['shared-by-me'],
          use: { ...devices['Desktop Chrome'] },
        },
      ],
});
