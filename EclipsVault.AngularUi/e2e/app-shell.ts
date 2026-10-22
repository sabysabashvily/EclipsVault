import type { Page } from '@playwright/test';

export class AppShell {
  readonly yourData;
  readonly accessGroup;
  readonly myAccess;
  readonly paletteQuery;

  constructor(page: Page) {
    this.yourData = page.getByRole('link', { name: 'Your data', exact: true });
    this.accessGroup = page.getByText('Access', { exact: true });
    this.myAccess = page.getByRole('link', { name: 'My access', exact: true });
    this.paletteQuery = page.getByRole('combobox');
  }
}
