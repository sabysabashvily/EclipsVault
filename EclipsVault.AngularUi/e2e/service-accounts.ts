import type { Page } from '@playwright/test';
import { openPalette } from './palette';

export class ServiceAccounts {
  readonly heading;
  readonly create;
  readonly status;
  constructor(private readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Service accounts', exact: true });
    this.create = page.getByRole('link', { name: 'New service account', exact: true }).first();
    this.status = page
      .getByRole('status')
      .filter({ hasText: 'Service account and its keys deleted.' });
  }
  async open(): Promise<void> {
    await this.page.goto('/ServiceAccounts');
  }
  row(name: string) {
    return this.page
      .getByRole('row')
      .filter({ has: this.page.getByRole('cell', { name, exact: true }) });
  }
  async manage(name: string): Promise<void> {
    await this.row(name).getByRole('link', { name: 'Manage', exact: true }).click();
  }
  async fromSidebar(path: '/Secrets' | '/Access'): Promise<void> {
    await this.page.goto(path);
    await this.page.getByRole('link', { name: 'Service accounts', exact: true }).click();
  }
  async fromPalette(path: '/Secrets' | '/Access'): Promise<void> {
    await this.page.goto(path);
    await openPalette(this.page);
    await this.page.getByRole('combobox').fill('Service accounts');
    await this.page.getByRole('option', { name: /Service accounts/ }).click();
  }
}
