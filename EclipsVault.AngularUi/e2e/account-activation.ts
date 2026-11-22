import type { Page } from '@playwright/test';
import { setTimeout } from 'node:timers/promises';
import { emailOf } from './account-creation';
import { AccountSignIn } from './account-sign-in';
import { clearSignInBudget } from './profile-state';
import { invitationToken } from './smtp-capture';
import { currentTotpStep, freshTotp, totp } from './totp';
import type { components } from '../src/app/ui-api/schema.generated';

const stepMilliseconds = 30_000;

/**
 * Activates the account just invited as `username` with `password`, through the API an activation
 * page calls, then signs it in through `page`, a browser with no session, with its password and
 * authenticator. Returns the authenticator secret and the step the sign-in spent, which the vault
 * never accepts again.
 */
export async function signInNewAccount(
  page: Page,
  username: string,
  password: string,
): Promise<{ secret: string; step: number }> {
  const token = await invitationToken(emailOf(username));
  // Every browser shares the proxy's address. Activation and sign-in are four of its eleven requests.
  clearSignInBudget();
  const signIn = new AccountSignIn(page);
  await signIn.open();
  const invitation: components['schemas']['ActivationResponse'] = await (
    await activationCall(page, 'activation', { token })
  ).json();
  const secret = invitation.secretBase32;
  // Activation spends the previous step, which the vault still accepts, so the sign-in below can
  // spend the current one without waiting for a new step.
  const activationStep = await previousStep();
  await activationCall(page, 'activation/complete', {
    token,
    password,
    code: totp(secret, activationStep * stepMilliseconds),
  });

  await signIn.open();
  await signIn.submitPassword(username, password);
  await signIn.submitCode(await freshTotp(secret, activationStep));
  const step = currentTotpStep();
  await page.waitForURL(/\/Secrets$/);
  return { secret, step };
}

/** The step before this one, taken early enough in this step that the vault still accepts it. */
async function previousStep(): Promise<number> {
  const margin = 5_000;
  const remaining = stepMilliseconds - (Date.now() % stepMilliseconds);
  if (remaining < margin) await setTimeout(remaining + 100);
  return currentTotpStep() - 1;
}

/** Posts with the request token the sign-in page set, as the Angular sign-in pages do. */
async function activationCall(page: Page, path: string, data: object) {
  const requestToken = (await page.context().cookies()).find(
    (cookie) => cookie.name === 'XSRF-TOKEN',
  );
  if (!requestToken) throw new Error('The sign-in page set no antiforgery token');
  const response = await page.request.post(`/api/ui/auth/${path}`, {
    headers: { RequestVerificationToken: decodeURIComponent(requestToken.value) },
    data,
  });
  if (!response.ok()) throw new Error(`Activation answered ${response.status()} for ${path}`);
  return response;
}
