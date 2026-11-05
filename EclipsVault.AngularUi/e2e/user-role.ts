import type { Page } from '@playwright/test';

export class UserRole {
  readonly clearance;
  readonly project;
  readonly heading;
  readonly back;
  readonly saveButton;
  readonly lastAdministratorRefusal;
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
  }
  async save(): Promise<void> {
    await this.saveButton.click();
  }
}
