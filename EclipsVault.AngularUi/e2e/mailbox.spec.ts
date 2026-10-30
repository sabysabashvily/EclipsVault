import type { BrowserContext } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { test, expect } from './fixtures';
import { AccountCreation } from './account-creation';
import { AccountSignIn } from './account-sign-in';
import { AuthenticatorEnrollment } from './authenticator-enrollment';
import { MailboxProfile } from './mailbox';
import { mailboxCode } from './smtp-capture';
import { currentTotpStep, freshTotp } from './totp';

test('a real SMTP code activates only the frozen mailbox and retires a copied session', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  const administrator = JSON.parse(
    await readFile(
      `${process.env['E2E_TEST_RESULTS'] || 'test-results'}/sessions-administrator.json`,
      'utf8',
    ),
  ) as { cookies: Awaited<ReturnType<BrowserContext['cookies']>> };
  await page.context().addCookies(administrator.cookies);
  const creation = new AccountCreation(page);
  await creation.open();
  const password = `E2e-${randomBytes(24).toString('hex')}`;
  await creation.createMailboxOwner('e2e-mailbox-owner', password);
  await expect(page).toHaveURL(/\/Admin\/Users$/);

  // Every project uses the proxy's one source address; isolate this flow's real credential budget.
  const windowMilliseconds = 120_000;
  await page.waitForTimeout(windowMilliseconds - (Date.now() % windowMilliseconds) + 1_000);
  const owner = await (await browserHealth.newContext()).newPage();
  const signIn = new AccountSignIn(owner);
  await signIn.open();
  await signIn.submitPassword('e2e-mailbox-owner', password);
  await expect(owner).toHaveURL(/\/Account\/EnrollTotp$/);
  const enrollment = new AuthenticatorEnrollment(owner);
  const secret = await enrollment.secret.innerText();
  const enrollmentCode = await freshTotp(secret);
  const enrollmentStep = currentTotpStep();
  await enrollment.confirm(enrollmentCode);
  await expect(owner).toHaveURL(/\/Secrets$/);
  const copied = await browserHealth.newContext();
  await copied.addCookies(await owner.context().cookies());
  const profile = new MailboxProfile(owner);
  await profile.open();
  const original = await (await profile.snapshot()).json();
  const prepared = await profile.prepareEmailChange('e2e-mailbox-confirmed@example.test');
  expect(prepared.status()).toBe(201);
  const { operationId } = await prepared.json();
  const verification = await profile.verifyWithAuthenticator(
    operationId,
    password,
    await freshTotp(secret, enrollmentStep),
  );
  expect(verification.status()).toBe(200);
  expect((await profile.requestCode(operationId)).status()).toBe(200);
  expect((await (await profile.snapshot()).json()).email).toBe(original.email);
  const code = await mailboxCode('e2e-mailbox-confirmed@example.test');

  expect((await profile.confirmEmail(operationId, code)).status()).toBe(204);

  expect((await (await profile.snapshot()).json()).email).toBe(
    'e2e-mailbox-confirmed@example.test',
  );
  expect((await copied.request.get('/api/ui/me')).status()).toBe(401);
  expect((await profile.confirmEmail(operationId, code)).status()).toBe(404);
});
