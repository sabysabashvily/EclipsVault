import type { Page } from '@playwright/test';

export class ResponsiveShell {
  readonly open;
  readonly navigation;
  readonly close;
  readonly search;
  readonly palette;
  readonly newUser;
  readonly rowMore;
  readonly editUser;
  readonly usersLink;
  readonly users;
  readonly showPassword;

  constructor(page: Page) {
    this.open = page.getByRole('button', { name: 'Open navigation', exact: true });
    this.navigation = page.getByRole('dialog', { name: 'Navigation', exact: true });
    this.close = this.navigation.getByRole('button', { name: 'Close navigation', exact: true });
    this.search = page.getByRole('button', {
      name: 'Search secrets or jump to a page',
      exact: true,
    });
    this.palette = page.getByRole('combobox', {
      name: 'Search secrets, or jump to a page',
      exact: true,
    });
    this.newUser = page.getByRole('link', { name: 'New user', exact: true });
    this.rowMore = page.getByText('More', { exact: true }).first();
    this.editUser = page.getByRole('menuitem', { name: 'Edit', exact: true }).first();
    this.usersLink = this.navigation.getByRole('link', { name: 'Users', exact: true });
    this.users = page.getByRole('region', { name: 'Users', exact: true });
    this.showPassword = page.getByRole('button', { name: 'Show password', exact: true });
  }
}
