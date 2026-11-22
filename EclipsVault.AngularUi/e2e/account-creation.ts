import type { Page } from '@playwright/test';

interface NewAccount {
  firstName: string;
  lastName: string;
  username: string;
  clearance: string;
  project: string;
}

/** The address the New user form gives every browser-test account: its username at a reserved domain. */
export function emailOf(username: string): string {
  return `${username}@example.test`;
}

export class AccountCreation {
  readonly heading;
  readonly firstName;
  readonly lastName;
  readonly email;
  readonly status;
  readonly submitButton;
  readonly back;
  constructor(private readonly page: Page) {
    this.firstName = page.getByLabel('First name');
    this.lastName = page.getByLabel('Last name');
    this.heading = page.getByRole('heading', { name: 'New user', exact: true });
    this.email = page.getByLabel('Email', { exact: true });
    // eslint-disable-next-line playwright/no-raw-locators -- Loading has a separate status role; scope to the shared outcome announcer.
    this.status = page.locator('app-status-line').getByRole('status');
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
    await this.email.fill(emailOf(account.username));
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

  async createMailboxOwner(username: string): Promise<void> {
    await this.create({
      firstName: 'Mailbox',
      lastName: 'Owner',
      username,
      clearance: 'Standard',
      project: 'GLOBAL',
    });
  }
}
