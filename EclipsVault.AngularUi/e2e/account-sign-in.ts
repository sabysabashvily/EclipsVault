import type { Page } from '@playwright/test';

export class AccountSignIn {
  constructor(private readonly page: Page) {}

  get username() {
    return this.page.getByLabel('Username');
  }

  get password() {
    return this.page.getByLabel('Password', { exact: true });
  }

  get themeSwitch() {
    return this.page.getByRole('button', { name: 'Switch color theme', exact: true });
  }

  get restartedNotice() {
    return this.page.getByRole('alert').filter({
      hasText: 'Your account changed while you were signing in. Sign in again.',
    });
  }

  get localSignOutWarning() {
    return this.page.getByRole('alert').filter({
      hasText: "This browser's cookie was cleared. Other devices may still be signed in.",
    });
  }

  /** Login's StatusLine announces a warning as an alert. */
  get warning() {
    return this.page
      .getByRole('alert')
      .filter({ hasText: "couldn't record it in the audit trail" });
  }

  async open(): Promise<void> {
    await this.page.goto('/Account/Login');
  }

  async submitPassword(username: string, password: string): Promise<void> {
    await this.username.fill(username);
    await this.password.fill(password);
    await this.page.getByRole('button', { name: 'Continue', exact: true }).click();
  }

  /** Login's single sign-on button, which posts to the identity provider named on it. */
  async continueWithSingleSignOn(provider: string): Promise<void> {
    await this.page.getByRole('button', { name: `Sign in with ${provider}`, exact: true }).click();
  }

  /** The second-factor page's authenticator code. */
  async submitAuthenticatorCode(code: string): Promise<void> {
    await this.page.getByLabel('Authenticator code').fill(code);
    await this.page.getByRole('button', { name: 'Verify and sign in', exact: true }).click();
  }
}
