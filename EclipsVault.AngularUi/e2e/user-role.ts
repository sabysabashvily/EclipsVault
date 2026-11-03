import type { Page } from '@playwright/test';

export class UserRole {
  readonly clearance;
  readonly lastAdministratorRefusal;
  constructor(private readonly page: Page) {
    this.clearance = page.getByLabel('Clearance');
    this.lastAdministratorRefusal = page.getByText(
      'Cannot demote the last TopSecret administrator — the vault would become unmanageable.',
      { exact: true },
    );
  }
  async save(): Promise<void> {
    await this.page.getByRole('button', { name: 'Save role', exact: true }).click();
  }
}
