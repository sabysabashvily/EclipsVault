import type { Locator, Page } from '@playwright/test';

export class AccessRequests {
  readonly heading;
  readonly context;
  readonly verdict;
  readonly toReview;
  readonly mine;
  readonly reviewScope;
  readonly nothingToReview;
  readonly noRequests;
  readonly browseSecrets;
  readonly error;
  readonly retry;
  readonly approval;
  readonly expiry;
  readonly approveAndGrant;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Access requests', exact: true });
    this.context = page.getByText(/^(Nothing waits on you|\d+ requests? waits? on you) · /);
    this.verdict = page.getByText(/^(Waiting on you|Nothing to review)$/);
    this.toReview = page.getByRole('list', { name: 'Requests to review', exact: true });
    this.mine = page.getByRole('table');
    this.reviewScope = page.getByText(/^(All projects|Your project) · Newest first$/);
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
    this.approval = page.getByRole('alertdialog', { name: /^Approve .+'s request\?$/ });
    this.expiry = this.approval.getByLabel('Access ends after (days)');
    this.approveAndGrant = this.approval.getByRole('button', {
      name: 'Approve and grant access',
      exact: true,
    });
  }

  async open(path = '/AccessRequests'): Promise<void> {
    await this.page.goto(path);
  }

  // A request's title holds the name and its project code; every secret these specs create is in GLOBAL.
  requestToReview(secret: string): Locator {
    return this.toReview.getByRole('listitem').filter({ hasText: `${secret} GLOBAL` });
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
