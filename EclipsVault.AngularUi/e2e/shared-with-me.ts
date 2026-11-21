import type { Locator, Page } from '@playwright/test';

export class SharedWithMe {
  readonly heading;
  readonly introduction;
  readonly context;
  readonly columns;
  readonly rows;
  readonly emptyHeading;
  readonly emptyMessage;
  readonly goToSecrets;
  readonly error;
  readonly retry;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Shared with me', exact: true });
    this.introduction = page.getByText(
      'Secrets outside your project that someone has granted you access to.',
      { exact: true },
    );
    this.context = page.getByText(/^\d+ secrets? shared with you · /);
    this.columns = page.getByRole('columnheader');
    // Each listed share's row is a link to the secret; the header row has none.
    this.rows = page.getByRole('row').filter({ has: page.getByRole('link') });
    this.emptyHeading = page.getByRole('heading', {
      name: 'Nothing shared with you yet',
      exact: true,
    });
    this.emptyMessage = page.getByText(
      'This is where secrets from other projects show up when a colleague grants you access to them.',
      { exact: false },
    );
    this.goToSecrets = page.getByRole('link', { name: 'Go to your secrets', exact: true });
    this.error = page.getByRole('main').getByRole('alert');
    this.retry = page.getByRole('button', { name: 'Try again', exact: true });
  }

  async open(path = '/Secrets/SharedWithMe'): Promise<void> {
    await this.page.goto(path);
  }

  secret(name: string) {
    return this.rows.filter({ has: this.page.getByRole('link', { name, exact: true }) });
  }

  // Cells have no accessible name of their own; the header order places them.
  /** The secret's cell: its name, with the project code under it. */
  secretCellOf(row: Locator) {
    return row.getByRole('cell').nth(0);
  }

  environmentBadgeOf(row: Locator, environment: string) {
    return row.getByRole('cell').nth(1).getByText(environment, { exact: true });
  }

  sensitivityBadgeOf(row: Locator, sensitivity: string) {
    return row.getByRole('cell').nth(2).getByText(sensitivity, { exact: true });
  }

  sharedByOf(row: Locator) {
    return row.getByRole('cell').nth(3);
  }

  accessUntilOf(row: Locator) {
    return row.getByRole('cell').nth(4);
  }

  openOf(row: Locator) {
    return row.getByRole('link');
  }
}
