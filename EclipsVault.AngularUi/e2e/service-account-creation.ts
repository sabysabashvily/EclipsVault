import type { Page } from '@playwright/test';

export class ServiceAccountCreation {
  readonly heading;
  readonly name;
  readonly clearance;
  readonly project;
  readonly create;
  readonly back;
  constructor(page: Page) {
    this.heading = page.getByRole('heading', { name: 'New service account', exact: true });
    this.name = page.getByLabel('Name', { exact: true });
    this.clearance = page.getByLabel('Clearance level', { exact: true });
    this.project = page.getByLabel('Project key', { exact: true });
    this.create = page.getByRole('button', { name: 'Create', exact: true });
    this.back = page.getByRole('link', { name: 'Back', exact: true });
  }
  async fill(name: string): Promise<void> {
    await this.name.fill(name);
    await this.clearance.selectOption({ label: 'Secret' });
    await this.project.fill('GLOBAL');
  }
}
