import type { Locator, Page } from '@playwright/test';

export class SharedWithMe {
  readonly heading;
  readonly introduction;
  readonly columns;
  readonly rows;
  readonly emptyHeading;
  readonly emptyMessage;
  readonly goToSecrets;
  readonly error;
  readonly retry;
  readonly emptyState;
  readonly emptyIcon;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Shared with me', exact: true });
    this.introduction = page.getByText(
      'Secrets outside your project that someone has granted you access to.',
      { exact: true },
    );
    this.columns = page.getByRole('columnheader');
    this.rows = page
      .getByRole('row')
      .filter({ has: page.getByRole('link', { name: 'Open', exact: true }) });
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
    // The empty state's unlabelled layout and icon containers are read only for visual parity.
    // eslint-disable-next-line playwright/no-raw-locators
    this.emptyState = page.locator('.empty-state');
    // eslint-disable-next-line playwright/no-raw-locators
    this.emptyIcon = page.locator('.empty-icon');
  }

  async open(path = '/Secrets/SharedWithMe'): Promise<void> {
    await this.page.goto(path);
  }

  secret(name: string) {
    return this.rows.filter({ has: this.page.getByRole('cell', { name, exact: true }) });
  }

  // Cells have no accessible name of their own; the header order places them.
  cellsOf(row: Locator) {
    return row.getByRole('cell');
  }

  nameOf(row: Locator) {
    return row.getByRole('cell').nth(0);
  }

  environmentBadgeOf(row: Locator, environment: string) {
    return row.getByRole('cell').nth(2).getByText(environment, { exact: true });
  }

  sensitivityBadgeOf(row: Locator, sensitivity: string) {
    return row.getByRole('cell').nth(3).getByText(sensitivity, { exact: true });
  }

  accessUntilOf(row: Locator) {
    return row.getByRole('cell').nth(5);
  }

  openOf(row: Locator) {
    return row.getByRole('link', { name: 'Open', exact: true });
  }
}
