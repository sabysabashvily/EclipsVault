import type { Locator, Page } from '@playwright/test';

export class SharedByMe {
  readonly heading;
  readonly rows;
  readonly columns;
  readonly emptyHeading;
  readonly goToSecrets;
  readonly confirmation;
  readonly cancel;
  readonly continue;
  readonly status;
  readonly error;
  readonly retry;
  readonly introduction;
  readonly emptyMessage;
  readonly emptyState;
  readonly emptyIcon;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Shared by me', exact: true });
    this.rows = page
      .getByRole('row')
      .filter({ has: page.getByRole('button', { name: 'Revoke', exact: true }) });
    this.columns = page.getByRole('columnheader');
    this.emptyHeading = page.getByRole('heading', {
      name: "You haven't shared anything",
      exact: true,
    });
    this.goToSecrets = page.getByRole('link', { name: 'Go to your secrets', exact: true });
    this.confirmation = page.getByRole('alertdialog');
    this.cancel = this.confirmation.getByRole('button', { name: 'Cancel', exact: true });
    this.continue = this.confirmation.getByRole('button', { name: 'Continue', exact: true });
    this.status = page.getByRole('main').getByRole('status');
    this.error = page.getByRole('main').getByRole('alert');
    this.retry = page.getByRole('button', { name: 'Try again', exact: true });
    this.introduction = page.getByText("Everyone you've granted access to one of your secrets.", {
      exact: false,
    });
    this.emptyMessage = page.getByText(
      'When you grant a colleague access to one of your secrets,',
      {
        exact: false,
      },
    );
    // The empty state's unlabelled layout and icon containers are read only for visual parity.
    // eslint-disable-next-line playwright/no-raw-locators
    this.emptyState = page.locator('.empty-state');
    // eslint-disable-next-line playwright/no-raw-locators
    this.emptyIcon = page.locator('.empty-icon');
  }

  share(secretName: string) {
    return this.rows.filter({
      has: this.page.getByRole('cell', { name: secretName, exact: true }),
    });
  }

  // Cells have no accessible name of their own; the header order places them.
  secretOf(row: Locator) {
    return row.getByRole('cell').nth(0);
  }

  accessUntilOf(row: Locator) {
    return row.getByRole('cell').nth(3);
  }

  recipientOf(row: Locator) {
    return row.getByRole('cell').nth(1);
  }

  sharedOnOf(row: Locator) {
    return row.getByRole('cell').nth(2);
  }
}
