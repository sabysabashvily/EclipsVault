import type { Page } from '@playwright/test';

export class UserRole {
  readonly clearance;
  readonly project;
  readonly heading;
  readonly back;
  readonly saveButton;
  readonly lastAdministratorRefusal;
  readonly invitation;
  readonly resendButton;
  readonly invitationAddress;
  readonly replaceAddressButton;
  readonly status;
  constructor(private readonly page: Page) {
    this.saveButton = page.getByRole('button', { name: 'Save role', exact: true });
    this.clearance = page.getByLabel('Clearance');
    this.project = page.getByLabel('Project');
    this.heading = page.getByRole('heading', { name: 'Edit user', exact: true });
    this.back = page.getByRole('link', { name: 'Back to users' });
    this.lastAdministratorRefusal = page.getByText(
      'Cannot demote the last TopSecret administrator — the vault would become unmanageable.',
      { exact: true },
    );
    this.invitation = page.getByRole('region', { name: 'Invitation', exact: true });
    this.resendButton = this.invitation.getByRole('button', {
      name: 'Resend invitation',
      exact: true,
    });
    this.invitationAddress = this.invitation.getByLabel('Invitation address', { exact: true });
    this.replaceAddressButton = this.invitation.getByRole('button', {
      name: 'Replace address',
      exact: true,
    });
    // eslint-disable-next-line playwright/no-raw-locators -- Loading has a separate status role; scope to the shared outcome announcer.
    this.status = page.locator('app-status-line').getByRole('status');
  }
  async save(): Promise<void> {
    await this.saveButton.click();
  }
}
