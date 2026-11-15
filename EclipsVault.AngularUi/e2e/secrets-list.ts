import type { Locator, Page } from '@playwright/test';

/** The Secrets list, whose rows Shared with me copies. */
export class SecretsList {
  readonly heading;
  readonly introduction;
  readonly columns;
  readonly rows;
  readonly filter;
  readonly newSecret;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Secrets', exact: true });
    // The subtitle starts with a count of the caller's secrets.
    this.introduction = page.getByText('Opening one records an audit entry.', { exact: false });
    this.columns = page.getByRole('columnheader');
    this.rows = page.getByRole('row');
    this.filter = page.getByRole('searchbox', { name: 'Filter secrets' });
    this.newSecret = page.getByRole('main').getByRole('link', { name: 'New secret', exact: true });
  }

  async open(): Promise<void> {
    await this.page.goto('/Secrets');
  }

  secret(name: string) {
    return this.page
      .getByRole('row')
      .filter({ has: this.page.getByRole('cell', { name, exact: true }) });
  }

  // Cells have no accessible name of their own; the header order places them.
  nameOf(row: Locator) {
    return row.getByRole('cell').nth(0);
  }

  projectOf(row: Locator) {
    return row.getByRole('cell').nth(1);
  }

  expiresOf(row: Locator) {
    return row.getByRole('cell').nth(4);
  }

  environmentBadgeOf(row: Locator, environment: string) {
    return row.getByRole('cell').nth(2).getByText(environment, { exact: true });
  }

  sensitivityBadgeOf(row: Locator, sensitivity: string) {
    return row.getByRole('cell').nth(3).getByText(sensitivity, { exact: true });
  }

  openOf(row: Locator) {
    return row.getByRole('link', { name: 'Open', exact: true });
  }
}
