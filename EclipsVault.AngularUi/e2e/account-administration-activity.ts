import type { Page } from '@playwright/test';

export class AccountAdministrationActivity {
  readonly heading;
  constructor(private readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'My activity', exact: true });
  }
  async open(): Promise<void> {
    await this.page.goto('/Activity');
  }
  entry(title: string) {
    return this.page
      .getByRole('main')
      .getByRole('listitem')
      .filter({ has: this.page.getByText(title, { exact: true }) })
      .first();
  }
}
