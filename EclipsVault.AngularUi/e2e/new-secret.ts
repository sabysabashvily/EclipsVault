import type { Page } from '@playwright/test';

interface NewSecret {
  name: string;
  project: string;
  sensitivity: string;
  ttlDays: string;
}

export class NewSecretForm {
  readonly name;
  readonly value;
  readonly project;
  readonly ttlDays;
  readonly environment;
  readonly sensitivity;
  readonly submit;
  /** The form-level message: a taken name, or the policy's reasons for a refusal. */
  readonly refusal;

  constructor(private readonly page: Page) {
    this.name = page.getByLabel('Name', { exact: true });
    this.value = page.getByLabel('Secret value');
    this.project = page.getByLabel('Project key');
    this.ttlDays = page.getByLabel('TTL in days (0 = never expires)');
    this.environment = page.getByLabel('Environment');
    this.sensitivity = page.getByLabel('Sensitivity');
    this.submit = page.getByRole('button', { name: 'Encrypt and store' });
    this.refusal = page.getByRole('main').getByRole('alert');
  }

  async open(): Promise<void> {
    await this.page.goto('/Secrets/Create');
  }

  async create(secret: NewSecret): Promise<void> {
    await this.name.fill(secret.name);
    await this.value.fill(`e2e-${secret.name}`);
    await this.project.fill(secret.project);
    await this.environment.selectOption({ label: 'Development' });
    await this.sensitivity.selectOption({ label: secret.sensitivity });
    await this.ttlDays.fill(secret.ttlDays);
    await this.submit.click();
  }
}
