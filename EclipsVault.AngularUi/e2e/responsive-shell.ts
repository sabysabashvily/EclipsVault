import type { Page } from '@playwright/test';

export class ResponsiveShell {
  readonly open;
  readonly navigation;
  readonly close;
  readonly search;
  readonly palette;

  constructor(readonly page: Page) {
    this.open = page.getByRole('button', { name: 'Open navigation', exact: true });
    this.navigation = page.getByRole('dialog', { name: 'Navigation', exact: true });
    this.close = this.navigation.getByRole('button', { name: 'Close navigation', exact: true });
    this.search = this.navigation.getByRole('button', {
      name: 'Search secrets or jump to a page',
      exact: true,
    });
    this.palette = page.getByRole('combobox', {
      name: 'Search secrets, or jump to a page',
      exact: true,
    });
  }
}
