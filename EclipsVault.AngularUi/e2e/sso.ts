import type { Page } from '@playwright/test';

/** The vault's sign-in page, at the button that posts to the identity provider. */
export class VaultSingleSignOn {
  constructor(private readonly page: Page) {}

  async open(): Promise<void> {
    await this.page.goto('/Account/Login');
  }

  async continueWithKeycloak(): Promise<void> {
    await this.page.getByRole('button', { name: 'Sign in with Keycloak', exact: true }).click();
  }
}

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
