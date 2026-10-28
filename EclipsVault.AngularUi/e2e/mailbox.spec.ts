import type { BrowserContext } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { test, expect } from './fixtures';
import { MailboxProfile } from './mailbox';
import { freshTotp } from './totp';

test('a real SMTP code activates only the frozen mailbox and retires a copied session', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  const administrator = JSON.parse(
    await readFile('test-results/sessions-administrator.json', 'utf8'),
  ) as { cookies: Awaited<ReturnType<BrowserContext['cookies']>> };
  await page.context().addCookies(administrator.cookies);
  await page.goto('/Admin/CreateUser');
  const password = `E2e-${randomBytes(24).toString('hex')}`;
  await page.getByLabel('First name').fill('Mailbox');
  await page.getByLabel('Last name').fill('Owner');
  await page.getByLabel('Username').fill('e2e-mailbox-owner');
  await page.getByLabel('Initial password', { exact: true }).fill(password);
  await page.getByLabel('Clearance').selectOption({ label: 'Standard' });
  await page.getByLabel('Project').fill('GLOBAL');
  await page.getByRole('button', { name: 'Create user', exact: true }).click();
  await expect(page).toHaveURL(/\/Admin\/Users$/);

  // Every project uses the proxy's one source address; isolate this flow's real credential budget.
  const windowMilliseconds = 120_000;
  await page.waitForTimeout(windowMilliseconds - (Date.now() % windowMilliseconds) + 1_000);
  const owner = await (await browserHealth.newContext()).newPage();
  await owner.goto('/Account/Login');
  await owner.getByLabel('Username').fill('e2e-mailbox-owner');
  await owner.getByLabel('Password', { exact: true }).fill(password);
  await owner.getByRole('button', { name: 'Continue', exact: true }).click();
  await expect(owner).toHaveURL(/\/Account\/EnrollTotp$/);
  // Enrollment shows the disposable seed as unlabelled text.
  // eslint-disable-next-line playwright/no-raw-locators -- The seed has no label or semantic role.
  const secret = await owner.locator('.secret-code').innerText();
  await owner.getByLabel('Confirmation code').fill(await freshTotp(secret));
  const enrollmentStep = Math.floor(Date.now() / 30_000);
  await owner.getByRole('button', { name: 'Confirm enrollment', exact: true }).click();
  await expect(owner).toHaveURL(/\/Secrets$/);
  const copied = await browserHealth.newContext();
  await copied.addCookies(await owner.context().cookies());
  const profile = new MailboxProfile(owner);
  const original = await (await owner.request.get('/api/ui/profile')).json();
  const prepared = await profile.post('operations', {
    kind: 'ChangeEmail',
    newEmail: 'e2e-mailbox-confirmed@example.test',
  });
  expect(prepared.status()).toBe(201);
  const { operationId } = await prepared.json();
  expect(
    (
      await profile.post(`operations/${operationId}/verify`, {
        method: 'PasswordAndAuthenticatorCode',
        currentPassword: password,
        code: await freshTotp(secret, enrollmentStep),
      })
    ).status(),
  ).toBe(200);
  expect((await profile.post('email/request', { operationId })).status()).toBe(200);
  expect((await (await owner.request.get('/api/ui/profile')).json()).email).toBe(original.email);
  const code = await profile.mailboxCode('e2e-mailbox-confirmed@example.test');

  expect((await profile.post('email/confirm', { operationId, code })).status()).toBe(204);

  expect((await (await owner.request.get('/api/ui/profile')).json()).email).toBe(
    'e2e-mailbox-confirmed@example.test',
  );
  expect((await copied.request.get('/api/ui/me')).status()).toBe(401);
  expect((await profile.post('email/confirm', { operationId, code })).status()).toBe(404);
});
