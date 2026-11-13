import type { Page } from '@playwright/test';

interface NewSecret {
  name: string;
  project: string;
  sensitivity: string;
  ttlDays: string;
}

export class NewSecretForm {
  constructor(private readonly page: Page) {}

  async open(): Promise<void> {
    await this.page.goto('/Secrets/Create');
  }

  async create(secret: NewSecret): Promise<void> {
    await this.page.getByLabel('Name', { exact: true }).fill(secret.name);
    await this.page.getByLabel('Secret value').fill(`e2e-${secret.name}`);
    await this.page.getByLabel('Project key').fill(secret.project);
    await this.page.getByLabel('Environment').selectOption({ label: 'Development' });
    await this.page.getByLabel('Sensitivity').selectOption({ label: secret.sensitivity });
    await this.page.getByLabel('TTL in days (0 = never expires)').fill(secret.ttlDays);
    await this.page.getByRole('button', { name: 'Encrypt and store' }).click();
  }
}
