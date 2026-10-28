import type { Page } from '@playwright/test';

export class AccountCreation {
  constructor(private readonly page: Page) {}

  async open(): Promise<void> {
    await this.page.goto('/Admin/CreateUser');
  }

  async createMailboxOwner(username: string, password: string): Promise<void> {
    await this.page.getByLabel('First name').fill('Mailbox');
    await this.page.getByLabel('Last name').fill('Owner');
    await this.page.getByLabel('Username').fill(username);
    await this.page.getByLabel('Initial password', { exact: true }).fill(password);
    await this.page.getByLabel('Clearance').selectOption({ label: 'Standard' });
    await this.page.getByLabel('Project').fill('GLOBAL');
    await this.page.getByRole('button', { name: 'Create user', exact: true }).click();
  }
}
