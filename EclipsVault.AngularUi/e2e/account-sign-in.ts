import type { Page } from '@playwright/test';

export class AccountSignIn {
  constructor(private readonly page: Page) {}

  async open(): Promise<void> {
    await this.page.goto('/Account/Login');
  }

  async submitPassword(username: string, password: string): Promise<void> {
    await this.page.getByLabel('Username').fill(username);
    await this.page.getByLabel('Password', { exact: true }).fill(password);
    await this.page.getByRole('button', { name: 'Continue', exact: true }).click();
  }
}
