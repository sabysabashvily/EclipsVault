import type { Page } from '@playwright/test';

/** The Legacy Dynamic secrets page: roles to issue from, the one-time credential, and leases. */
export class DynamicSecretsPage {
  readonly issuedUsername;
  readonly issuedPassword;

  constructor(private readonly page: Page) {
    this.issuedUsername = page.getByRole('row', { name: /^Username / }).getByRole('cell');
    this.issuedPassword = page.getByRole('row', { name: /^Password / }).getByRole('cell');
  }

  async open(): Promise<void> {
    await this.page.goto('/DynamicSecrets');
  }

  async issue(role: string): Promise<void> {
    await this.page
      .getByRole('row', { name: new RegExp(`^${role} `) })
      .getByRole('button', { name: 'Generate' })
      .click();
  }

  async revoke(username: string): Promise<void> {
    await this.page
      .getByRole('row', { name: new RegExp(` ${username} `) })
      .getByRole('button', { name: 'Revoke' })
      .click();
  }

  message(text: string) {
    return this.page.getByRole('status').filter({ hasText: text });
  }
}
