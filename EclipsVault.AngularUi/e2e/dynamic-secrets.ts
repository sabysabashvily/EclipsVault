import type { Page } from '@playwright/test';

/** Dynamic secrets: roles to issue from, the one-time credential, leases and their dialogs. */
export class DynamicSecretsPage {
  readonly heading;
  readonly credential;
  readonly issuedUsername;
  readonly issuedPassword;
  readonly status;
  readonly alert;
  readonly stepUp;
  readonly confirmation;

  constructor(private readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Dynamic secrets', exact: true });
    this.credential = page.getByRole('region', { name: 'Your new credential — shown once' });
    this.issuedUsername = this.credential
      .getByRole('row', { name: /^Username / })
      .getByRole('cell');
    this.issuedPassword = this.credential
      .getByRole('row', { name: /^Password / })
      .getByRole('cell');
    this.status = page.getByRole('status').filter({ hasText: /\S/ });
    this.alert = page.getByRole('alert').filter({ hasText: /\S/ });
    this.stepUp = page.getByRole('dialog', { name: "Confirm it's you" });
    this.confirmation = page.getByRole('alertdialog');
  }

  async open(): Promise<void> {
    await this.page.goto('/DynamicSecrets');
    await this.page.getByRole('heading', { name: 'Roles you can issue' }).waitFor();
  }

  generate(role: string) {
    return this.roleRow(role).getByRole('button', { name: 'Generate' });
  }

  minutes(role: string) {
    return this.page.getByLabel(`Lease minutes for ${role}`);
  }

  revoke(username: string) {
    return this.page
      .getByRole('row', { name: new RegExp(` ${username} `) })
      .getByRole('button', { name: 'Revoke' });
  }

  note(text: string) {
    return this.page.getByText(text, { exact: true });
  }

  async prove(code: string): Promise<void> {
    await this.stepUp.getByLabel('Authenticator code').fill(code);
    await this.stepUp.getByRole('button', { name: 'Verify and continue' }).click();
  }

  async cancelProof(): Promise<void> {
    await this.stepUp.getByRole('button', { name: 'Cancel' }).click();
  }

  async useTheme(theme: 'dark' | 'light'): Promise<void> {
    await this.page
      .context()
      .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: this.page.url() }]);
    await this.page.reload();
  }

  private roleRow(role: string) {
    return this.page.getByRole('row', { name: new RegExp(`^${role}`) });
  }
}
