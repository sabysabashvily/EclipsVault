import type { Page } from '@playwright/test';

/** Keycloak's own sign-in pages: password, then a one-time code when the account has one. */
export class KeycloakSignIn {
  constructor(private readonly page: Page) {}

  async submitPassword(username: string, password: string): Promise<void> {
    await this.page.getByLabel('Username or email').fill(username);
    await this.page.getByLabel('Password', { exact: true }).fill(password);
    await this.page.getByRole('button', { name: 'Sign In', exact: true }).click();
  }

  async submitOneTimeCode(code: string): Promise<void> {
    await this.page.getByLabel('One-time code').fill(code);
    await this.page.getByRole('button', { name: 'Sign In', exact: true }).click();
  }
}
