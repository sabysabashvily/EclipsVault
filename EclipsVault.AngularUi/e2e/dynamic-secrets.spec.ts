import { savedAdministratorCookies } from './administrator-cookies';
import { DynamicSecretsPage } from './dynamic-secrets';
import { dynamicRoles, loginExists, provisionDynamicTarget } from './dynamic-target';
import { test, expect } from './fixtures';

test('an Internal role issues a real login on the target, and revoking drops it', async ({
  page,
}) => {
  provisionDynamicTarget();
  await page.context().addCookies(await savedAdministratorCookies());
  const dynamicSecrets = new DynamicSecretsPage(page);
  await dynamicSecrets.open();

  await dynamicSecrets.issue(dynamicRoles.internal);

  await expect(dynamicSecrets.issuedPassword).toHaveText(/^[A-Za-z0-9]{40}$/);
  const username = (await dynamicSecrets.issuedUsername.textContent())!.trim();
  expect(loginExists(username)).toBe(true);

  await dynamicSecrets.revoke(username);

  await expect(
    dynamicSecrets.message('Credential handed back — it no longer works.'),
  ).toBeVisible();
  expect(loginExists(username)).toBe(false);
});
