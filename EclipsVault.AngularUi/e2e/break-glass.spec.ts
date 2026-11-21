import { randomBytes } from 'node:crypto';
import { AccountCreation } from './account-creation';
import { AccountSignIn } from './account-sign-in';
import { savedAdministratorCookies } from './administrator-cookies';
import { refreshAdministratorProof } from './administrator-proof';
import { AuthenticatorEnrollment } from './authenticator-enrollment';
import { BreakGlass, blockRunClient, clearRunClientBlock } from './break-glass';
import { test, expect } from './fixtures';
import { Overview } from './overview';
import { clearSignInBudget } from './profile-state';
import { currentTotpStep, freshTotp } from './totp';
import { Users } from './users';
import type { paths } from '../src/app/ui-api/schema.generated';

test('an administrator on a blocked network recovers with a password and a real code, then continues into the vault', async ({
  page,
  browserHealth,
}) => {
  test.setTimeout(240_000);
  const username = 'e2e-break-glass-admin';
  const password = `E2e-${randomBytes(24).toString('hex')}`;
  await page.context().addCookies(await savedAdministratorCookies());
  // Earlier projects spend the shared source's credential budget; recovery needs some of it.
  clearSignInBudget();
  await refreshAdministratorProof(page);
  const creation = new AccountCreation(page);
  await creation.open();
  await creation.create({
    firstName: 'Break',
    lastName: 'Glass',
    username,
    password,
    clearance: 'Top Secret',
    project: 'GLOBAL',
  });
  await expect(page).toHaveURL(/\/Admin\/Users$/);

  try {
    // The new administrator's first sign-in enrolls the authenticator recovery will ask for.
    const enrolling = await (await browserHealth.newContext()).newPage();
    const signIn = new AccountSignIn(enrolling);
    await signIn.open();
    await signIn.submitPassword(username, password);
    await expect(enrolling).toHaveURL(/\/Account\/EnrollTotp$/);
    const enrollment = new AuthenticatorEnrollment(enrolling);
    const secret = await enrollment.secret.innerText();
    await enrollment.confirm(await freshTotp(secret));
    const enrolledStep = currentTotpStep();
    await expect(enrolling).toHaveURL(/\/Secrets$/);
    const response = await enrolling.request.get('/api/ui/access');
    expect(response.status()).toBe(200);
    const access: paths['/api/ui/access']['get']['responses'][200]['content']['application/json'] =
      await response.json();
    // Docker allocates the proxy's forwarded source address, which every browser in this run shares.
    const address = access.sourceIp;

    blockRunClient(address);
    try {
      const blocked = await (await browserHealth.newContext()).newPage();
      const recovery = new BreakGlass(blocked);
      // The blocked page is a 403 document, which the browser logs.
      browserHealth.expectConsoleError({ text: /status of 403/, url: /\/$/ });
      await blocked.goto('/');
      await expect(recovery.blockedHeading).toBeVisible();
      await recovery.recoverLink.click();
      await expect(blocked).toHaveURL(/\/Account\/Recover$/);
      await expect(recovery.heading).toBeVisible();

      await recovery.recover(username, password, await freshTotp(secret, enrolledStep));

      await expect(recovery.outcome).toHaveText(
        'The intrusion-defence block on your network was lifted and your session restored.',
      );
      await recovery.continueToVault.click();
      await expect(blocked).toHaveURL(/\/$/);
      await expect(new Overview(blocked).heading).toBeVisible();
    } finally {
      clearRunClientBlock(address);
    }
  } finally {
    // Remove the temporary Top Secret administrator so Users still sees vault-admin as the last one.
    const users = new Users(page);
    await users.open();
    await users.more(username, 'Delete user');
    await users.confirm();
    await expect(users.row(username)).toHaveCount(0);
  }
});
