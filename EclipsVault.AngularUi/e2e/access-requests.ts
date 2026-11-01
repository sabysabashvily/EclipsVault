import type { Locator, Page } from '@playwright/test';

export class AccessRequests {
  readonly heading;
  readonly introduction;
  readonly toReview;
  readonly mine;
  readonly reviewScope;
  readonly nothingToReview;
  readonly noRequests;
  readonly browseSecrets;
  readonly error;
  readonly retry;
  readonly emptyIcon;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Access requests', exact: true });
    this.introduction = page.getByText(
      'Request access to a secret you were denied, and review requests you can approve.',
      { exact: true },
    );
    // Each panel's table is told apart by a column only it has.
    this.toReview = page
      .getByRole('table')
      .filter({ has: page.getByRole('columnheader', { name: 'Requester', exact: true }) });
    this.mine = page
      .getByRole('table')
      .filter({ has: page.getByRole('columnheader', { name: 'Decision', exact: true }) });
    this.reviewScope = page.getByText(/^(All projects|Your project)$/);
    this.nothingToReview = page.getByRole('heading', {
      name: 'Nothing waiting on you',
      exact: true,
    });
    this.noRequests = page.getByRole('heading', {
      name: "You haven't requested any access",
      exact: true,
    });
    this.browseSecrets = page.getByRole('link', { name: 'Browse secrets', exact: true });
    // The status line keeps an empty alert region for announcements; only one with text counts.
    this.error = page.getByRole('main').getByRole('alert').filter({ hasText: /\S/ });
    this.retry = page.getByRole('button', { name: 'Try again', exact: true });
    // The empty state's unlabelled icon container is read only for visual parity.
    // eslint-disable-next-line playwright/no-raw-locators
    this.emptyIcon = page.locator('.empty-icon').first();
  }

  async open(path = '/AccessRequests'): Promise<void> {
    await this.page.goto(path);
  }

  // The Secret cell holds the name and its project badge; every secret these specs create is in GLOBAL.
  requestToReview(secret: string): Locator {
    return this.toReview.getByRole('row').filter({ hasText: `${secret} GLOBAL` });
  }

  myRequest(secret: string): Locator {
    return this.mine.getByRole('row').filter({ hasText: `${secret} GLOBAL` });
  }

  // Cells have no accessible name of their own; the header order places them.
  cellsOf(row: Locator): Locator {
    return row.getByRole('cell');
  }

  statusOf(row: Locator): Locator {
    return row.getByRole('cell').nth(1);
  }

  expiryOf(row: Locator): Locator {
    return row.getByLabel('Grant expiry in days');
  }

  approveOf(row: Locator): Locator {
    return row.getByRole('button', { name: 'Approve', exact: true });
  }

  rejectOf(row: Locator): Locator {
    return row.getByRole('button', { name: 'Reject', exact: true });
  }

  withdrawOf(row: Locator): Locator {
    return row.getByRole('button', { name: 'Withdraw', exact: true });
  }

  confirmation(message: string): Locator {
    return this.page.getByRole('alertdialog', { name: message, exact: true });
  }

  announcement(message: string): Locator {
    return this.page.getByText(message, { exact: true });
  }
}
