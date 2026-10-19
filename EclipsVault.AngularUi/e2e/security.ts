import type { Page, TestInfo } from '@playwright/test';
import type { paths } from '../src/app/ui-api/schema.generated';
import { feedIcons } from './activity';
import { expect } from './fixtures';

export type Security = paths['/api/ui/security']['get']['responses'][200]['content']['application/json'];

export const twoStepIcon = ['path:d=M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6z', 'path:d=M9 12l2 2 4-4'];
export const backupIcon = ['rect:x=4,y=4,width=16,height=16,rx=2', 'path:d=M8 9h8M8 13h5M8 17h3'];
export const passkeyIcon = ['circle:cx=8,cy=15,r=4', 'path:d=M10.85 12.15 19 4M18 5l2 2M15 8l2 2'];
export const devicesIcon = ['rect:x=2,y=4,width=14,height=10,rx=1.6', 'path:d=M6 18h6M9 14v4', 'rect:x=17,y=9,width=5,height=11,rx=1.4'];

/** Each check as displayed, including the badge's tone and any recommendation and fix link. */
export async function checkRows(page: Page) {
  return page.locator('.activity-feed > li').evaluateAll((rows) => rows.map((row) => {
    const badge = row.querySelector('.badge')!;
    const recommendation = row.querySelector('.check-rec');
    const fix = row.querySelector('.feed-action a');
    return {
      title: row.querySelector('.feed-title')!.textContent!.trim(),
      status: badge.textContent!.trim(),
      tone: [...badge.classList].filter((name) => name !== 'badge').join(' '),
      accent: [...row.classList].filter((name) => name !== 'feed-item').join(' '),
      detail: row.querySelector('.feed-meta')!.textContent!.trim(),
      recommendation: recommendation?.textContent!.trim() ?? null,
      fix: fix ? [fix.textContent!.trim(), fix.getAttribute('href')] : null,
    };
  }));
}

export const allClear: Security = {
  score: 100, grade: 'Strong', actionNeededCount: 0, recommendedCount: 0, passCount: 4, nextStep: null,
  checks: [
    { kind: 'TwoStepSignIn', status: 'Pass', title: 'Two-step sign-in', detail: 'An authenticator app is protecting your sign-in.', recommendation: "You're all set. Keep your authenticator app backed up.", fix: 'None' },
    { kind: 'BackupCodes', status: 'Pass', title: 'Backup codes', detail: 'You have 10 one-time backup codes.', recommendation: 'Keep them somewhere safe and offline.', fix: 'None' },
    { kind: 'Passkey', status: 'Pass', title: 'Passkey', detail: 'You have 1 passkey for phishing-resistant sign-in.', recommendation: "Great — passkeys can't be phished or replayed.", fix: 'None' },
    { kind: 'SignedInDevices', status: 'Pass', title: 'Signed-in devices', detail: 'Only this device is signed in.', recommendation: 'Check this list whenever you sign in somewhere new.', fix: 'SignedInDevices' },
  ],
};

export const atRisk: Security = {
  score: 30, grade: 'AtRisk', actionNeededCount: 1, recommendedCount: 3, passCount: 0,
  nextStep: "Turn on an authenticator app so a stolen password isn't enough to get in.",
  checks: [
    { kind: 'TwoStepSignIn', status: 'ActionNeeded', title: 'Two-step sign-in', detail: 'Your account relies on a password alone.', recommendation: "Turn on an authenticator app so a stolen password isn't enough to get in.", fix: 'Profile' },
    { kind: 'BackupCodes', status: 'Recommended', title: 'Backup codes', detail: 'Backup codes become available once two-step sign-in is on.', recommendation: 'Turn on two-step sign-in, then generate a set of one-time backup codes.', fix: 'Profile' },
    { kind: 'Passkey', status: 'Recommended', title: 'Passkey', detail: "You haven't added a passkey yet.", recommendation: "Add a passkey for sign-in that can't be phished, even if your password leaks.", fix: 'Profile' },
    { kind: 'SignedInDevices', status: 'Recommended', title: 'Signed-in devices', detail: '4 devices are signed in to your account.', recommendation: "Review the list and sign out anything you don't recognise.", fix: 'SignedInDevices' },
  ],
};

/** The administrator's real posture before any test adds a passkey or another session. */
export async function verifySecurityCheckup(page: Page, info: TestInfo, theme: string) {
  await page.context().addCookies([{ name: 'EclipsVault.Theme', value: theme, url: new URL('/', page.url()).href, secure: true }]);
  const served = await page.goto('/Security');
  expect(served?.headers()['content-security-policy']).toContain("require-trusted-types-for 'script'");
  await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
  await expect(page).toHaveTitle('Security checkup — EclipsVault');
  await expect(page.getByRole('heading', { name: 'Security checkup', exact: true })).toBeVisible();
  await expect(page.locator('.page-sub')).toHaveText(
    'A quick read on how well your account is protected. Everything here is about ' +
    'your account only — work through anything flagged to make it stronger.');
  await expect(page.locator('.activity-feed > li')).toHaveCount(4);
  const response = await page.request.get('/api/ui/security');
  expect(response.status()).toBe(200);
  const posture: Security = await response.json();
  expect([posture.score, posture.grade, posture.actionNeededCount, posture.recommendedCount, posture.passCount]).toEqual([65, 'Fair', 1, 1, 2]);
  expect(posture.checks.map((check) => check.kind)).toEqual(['BackupCodes', 'Passkey', 'TwoStepSignIn', 'SignedInDevices']);

  await expect(page.getByRole('img', { name: 'Security score 65 out of 100 — Fair', exact: true })).toContainClass('score-ring grade-fair');
  await expect(page.locator('.score-number')).toHaveText('65');
  await expect(page.locator('.score-outof')).toHaveText('/ 100');
  await expect(page.locator('.grade-label')).toHaveText('Fair');
  await expect(page.locator('.grade-label')).toContainClass('grade-label grade-fair');
  await expect(page.locator('.ring-track')).toHaveAttribute('r', '52');
  await expect(page.locator('.ring-value')).toHaveAttribute('r', '52');
  await expect(page.locator('.ring-value')).toHaveAttribute('stroke-dasharray', '212.4 114.4');
  await expect(page.locator('.ring-value')).toHaveAttribute('transform', 'rotate(-90 60 60)');
  await expect(page.locator('.checkup-lead')).toHaveText('Next step: Generate a fresh set so you can get in if you ever lose your authenticator.');
  await expect(page.locator('.checkup-counts .badge')).toHaveText(['1 action needed', '1 recommended', '2 secured']);
  expect(await checkRows(page)).toEqual([
    { title: 'Backup codes', status: 'Action needed', tone: 'tone-danger', accent: 'check-fail', detail: 'You have no backup codes left.', recommendation: 'Generate a fresh set so you can get in if you ever lose your authenticator.', fix: ['Generate codes', '/Profile'] },
    { title: 'Passkey', status: 'Recommended', tone: 'tone-warn', accent: 'check-warn', detail: "You haven't added a passkey yet.", recommendation: "Add a passkey for sign-in that can't be phished, even if your password leaks.", fix: ['Add a passkey', '/Profile'] },
    { title: 'Two-step sign-in', status: 'Secured', tone: 'tone-ok', accent: 'check-pass', detail: 'An authenticator app is protecting your sign-in.', recommendation: null, fix: null },
    { title: 'Signed-in devices', status: 'Secured', tone: 'tone-ok', accent: 'check-pass', detail: 'Only this device is signed in.', recommendation: null, fix: null },
  ]);
  expect(await feedIcons(page)).toEqual([backupIcon, passkeyIcon, twoStepIcon, devicesIcon]);
  await expect(page.locator('style, [style]')).toHaveCount(0);
  const look = await page.locator('.checkup-hero').evaluate((hero) => {
    const ring = hero.querySelector('.ring-value')!;
    return [getComputedStyle(hero).display, getComputedStyle(ring).stroke,
      getComputedStyle(hero.querySelector('.grade-label')!).color,
      getComputedStyle(document.querySelector('.feed-action')!).alignSelf];
  });
  expect(look).toEqual(['flex', theme === 'dark' ? 'rgb(255, 214, 10)' : 'rgb(138, 106, 0)',
    theme === 'dark' ? 'rgb(255, 214, 10)' : 'rgb(138, 106, 0)', 'center']);
  await page.evaluate(() => document.fonts.ready);
  const path = info.outputPath(`administrator-${theme}-security.png`);
  await page.screenshot({ path, fullPage: true });
  await info.attach(`administrator-${theme}-security`, { path, contentType: 'image/png' });
}
