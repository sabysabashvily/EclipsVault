import type { Page } from '@playwright/test';

interface NewAccount {
  firstName: string;
  lastName: string;
  username: string;
  password: string;
  clearance: string;
  project: string;
}

export class AccountCreation {
  readonly heading;
  readonly firstName;
  readonly lastName;
  readonly password;
  readonly submitButton;
  readonly back;
  constructor(private readonly page: Page) {
    this.firstName = page.getByLabel('First name');
    this.lastName = page.getByLabel('Last name');
    this.heading = page.getByRole('heading', { name: 'New user', exact: true });
    this.password = page.getByLabel('Initial password', { exact: true });
    this.submitButton = page.getByRole('button', { name: 'Create user', exact: true });
    this.back = page.getByRole('link', { name: 'Back to users' });
  }

  async fromDashboard(): Promise<void> {
    await this.page.goto('/');
    await this.page
      .getByRole('link', { name: 'Provision a user TOTP enrollment at first sign-in', exact: true })
      .click();
  }

  async open(): Promise<void> {
    await this.page.goto('/Admin/CreateUser');
  }

  async fill(account: NewAccount): Promise<void> {
    await this.page.getByLabel('First name').fill(account.firstName);
    await this.page.getByLabel('Last name').fill(account.lastName);
    await this.page.getByLabel('Username').fill(account.username);
    await this.password.fill(account.password);
    await this.page.getByLabel('Clearance').selectOption({ label: account.clearance });
    await this.page.getByLabel('Project').fill(account.project);
  }

  async submit(): Promise<void> {
    await this.submitButton.click();
  }

  async create(account: NewAccount): Promise<void> {
    await this.fill(account);
    await this.submit();
  }

  async createMailboxOwner(username: string, password: string): Promise<void> {
    await this.create({
      firstName: 'Mailbox',
      lastName: 'Owner',
      username,
      password,
      clearance: 'Standard',
      project: 'GLOBAL',
    });
  }
}
