import type { Page, TestInfo } from '@playwright/test';
import type { paths } from '../src/app/ui-api/schema.generated';
import { expect } from './fixtures';

export type Security =
  paths['/api/ui/security']['get']['responses'][200]['content']['application/json'];

/** The title's verdict pill; the checks below carry the same words, so it is read from its row. */
export function securityVerdict(page: Page) {
  // eslint-disable-next-line playwright/no-raw-locators -- "Action needed" and "Recommended" also label checks; only the title row holds the verdict.
  return page.locator('.title-row > .badge');
}

export function securityStatus(page: Page) {
  return page.getByRole('main').getByRole('status');
}

export function checksNeedingYou(page: Page) {
  return page.getByRole('list', { name: 'Needs you', exact: true });
}

export function passingChecks(page: Page) {
  return page.getByRole('list', { name: 'Passing', exact: true });
}

/** Each check that needs you as displayed: title, pill, detail, recommendation and any fix link. */
export async function failingRows(page: Page) {
  return checksNeedingYou(page)
    .getByRole('listitem')
    .evaluateAll((items) =>
      items.map((item) => {
        const [title, detail, recommendation] = [...item.querySelectorAll('p')].map((line) =>
          line.textContent!.trim(),
        );
        const fix = item.querySelector('a');
        return {
          title,
          status: item.querySelector('.badge')!.textContent!.trim(),
          detail,
          recommendation,
          fix: fix ? [fix.textContent!.trim(), fix.getAttribute('href')] : null,
        };
      }),
    );
}

/** Each passing check as displayed: its title and detail, with no pill and no link. */
export async function passingRows(page: Page) {
  return passingChecks(page)
    .getByRole('listitem')
    .evaluateAll((items) =>
      items.map((item) => {
        const [title, detail] = [...item.querySelectorAll('p')].map((line) =>
          line.textContent!.trim(),
        );
        return { title, detail };
      }),
    );
}

export const allClear: Security = {
  score: 100,
  grade: 'Strong',
  actionNeededCount: 0,
  recommendedCount: 0,
  passCount: 4,
  nextStep: null,
  checks: [
    {
      kind: 'TwoStepSignIn',
      status: 'Pass',
      title: 'Two-step sign-in',
      detail: 'An authenticator app is protecting your sign-in.',
      recommendation: "You're all set. Keep your authenticator app backed up.",
      fix: 'None',
    },
    {
      kind: 'BackupCodes',
      status: 'Pass',
      title: 'Backup codes',
      detail: 'You have 10 one-time backup codes.',
      recommendation: 'Keep them somewhere safe and offline.',
      fix: 'None',
    },
    {
      kind: 'Passkey',
      status: 'Pass',
      title: 'Passkey',
      detail: 'You have 1 passkey for phishing-resistant sign-in.',
      recommendation: "Great — passkeys can't be phished or replayed.",
      fix: 'None',
    },
    {
      kind: 'SignedInDevices',
      status: 'Pass',
      title: 'Signed-in devices',
      detail: 'Only this device is signed in.',
      recommendation: 'Check this list whenever you sign in somewhere new.',
      fix: 'SignedInDevices',
    },
  ],
};

export const atRisk: Security = {
  score: 30,
  grade: 'AtRisk',
  actionNeededCount: 1,
  recommendedCount: 3,
  passCount: 0,
  nextStep: "Turn on an authenticator app so a stolen password isn't enough to get in.",
  checks: [
    {
      kind: 'TwoStepSignIn',
      status: 'ActionNeeded',
      title: 'Two-step sign-in',
      detail: 'Your account relies on a password alone.',
      recommendation: "Turn on an authenticator app so a stolen password isn't enough to get in.",
      fix: 'Profile',
    },
    {
      kind: 'BackupCodes',
      status: 'Recommended',
      title: 'Backup codes',
      detail: 'Backup codes become available once two-step sign-in is on.',
      recommendation: 'Turn on two-step sign-in, then generate a set of one-time backup codes.',
      fix: 'Profile',
    },
    {
      kind: 'Passkey',
      status: 'Recommended',
      title: 'Passkey',
      detail: "You haven't added a passkey yet.",
      recommendation:
        "Add a passkey for sign-in that can't be phished, even if your password leaks.",
      fix: 'Profile',
    },
    {
      kind: 'SignedInDevices',
      status: 'Recommended',
      title: 'Signed-in devices',
      detail: '4 devices are signed in to your account.',
      recommendation: "Review the list and sign out anything you don't recognise.",
      fix: 'SignedInDevices',
    },
  ],
};

/** The administrator's real posture before any test adds a passkey or another session. */
export async function verifySecurityCheckup(page: Page, info: TestInfo, theme: string) {
  await page
    .context()
    .addCookies([
      { name: 'EclipsVault.Theme', value: theme, url: new URL('/', page.url()).href, secure: true },
    ]);
  const served = await page.goto('/Security');
  expect(served?.headers()['content-security-policy']).toContain(
    "require-trusted-types-for 'script'",
  );
  await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
  await expect(page).toHaveTitle('Security checkup — EclipsVault');
  await expect(page.getByRole('heading', { name: 'Security checkup', exact: true })).toBeVisible();
  await expect(securityVerdict(page)).toHaveText('Action needed');
  await expect(
    page.getByText(
      '2 checks need you · next: Generate a fresh set so you can get in if you ever lose your authenticator.',
      { exact: true },
    ),
  ).toBeVisible();
  await expect(checksNeedingYou(page).getByRole('listitem')).toHaveCount(2);
  const response = await page.request.get('/api/ui/security');
  expect(response.status()).toBe(200);
  const posture: Security = await response.json();
  expect([
    posture.score,
    posture.grade,
    posture.actionNeededCount,
    posture.recommendedCount,
    posture.passCount,
  ]).toEqual([65, 'Fair', 1, 1, 2]);
  expect(posture.checks.map((check) => check.kind)).toEqual([
    'BackupCodes',
    'Passkey',
    'TwoStepSignIn',
    'SignedInDevices',
  ]);

  const tile = page.getByRole('img', { name: 'Security score 65 out of 100 — Fair', exact: true });
  await expect(tile).toContainText('65 / 100');
  await expect(tile).toContainText('Target 100');
  await expect(tile).toContainText('Fair');
  expect(await failingRows(page)).toEqual([
    {
      title: 'Backup codes',
      status: 'Action needed',
      detail: 'You have no backup codes left.',
      recommendation: 'Generate a fresh set so you can get in if you ever lose your authenticator.',
      fix: ['Generate codes', '/Profile'],
    },
    {
      title: 'Passkey',
      status: 'Recommended',
      detail: "You haven't added a passkey yet.",
      recommendation:
        "Add a passkey for sign-in that can't be phished, even if your password leaks.",
      fix: ['Add a passkey', '/Profile'],
    },
  ]);
  expect(await passingRows(page)).toEqual([
    { title: 'Two-step sign-in', detail: 'An authenticator app is protecting your sign-in.' },
    { title: 'Signed-in devices', detail: 'Only this device is signed in.' },
  ]);
  await expect(page.locator('style, [style]')).toHaveCount(0);
  await page.evaluate(() => document.fonts.ready);
  const path = info.outputPath(`administrator-${theme}-security.png`);
  await page.screenshot({ path, fullPage: true });
  await info.attach(`administrator-${theme}-security`, { path, contentType: 'image/png' });
}
