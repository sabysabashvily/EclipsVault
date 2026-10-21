import type { Page } from '@playwright/test';
import type { components } from '../src/app/ui-api/schema.generated';

export type YourDataSummary = components['schemas']['DataResponse'];

export class YourData {
  readonly heading;
  readonly introduction;
  readonly accountLabels;
  readonly accountValues;
  readonly notice;
  readonly download;
  readonly downloadCopy;
  readonly error;
  readonly retry;
  readonly pageHeader;
  readonly accountGrid;
  readonly cardGrid;
  readonly firstCard;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Your data', exact: true });
    this.introduction = page.getByText('Everything EclipsVault holds about your account', {
      exact: false,
    });
    this.accountLabels = page.getByRole('main').getByRole('term');
    this.accountValues = page.getByRole('main').getByRole('definition');
    this.notice = page.getByText('This file contains the account and security metadata', {
      exact: false,
    });
    this.download = page.getByRole('button', { name: 'Download (JSON)', exact: true });
    this.downloadCopy = page.getByRole('button', {
      name: 'Download a copy of my data (JSON)',
      exact: true,
    });
    this.error = page.getByRole('main').getByRole('alert');
    this.retry = page.getByRole('button', { name: 'Try again', exact: true });
    // These unlabelled layout containers are read only by visual parity tests.
    // eslint-disable-next-line playwright/no-raw-locators
    this.pageHeader = page.locator('.page-header');
    // eslint-disable-next-line playwright/no-raw-locators
    this.accountGrid = page.locator('.detail-grid');
    // eslint-disable-next-line playwright/no-raw-locators
    this.cardGrid = page.locator('.stat-grid');
    // eslint-disable-next-line playwright/no-raw-locators
    this.firstCard = page.locator('.stat-card').first();
  }

  /** Each card's value and label, which have no accessible structure of their own. */
  async cards(): Promise<string[][]> {
    // eslint-disable-next-line playwright/no-raw-locators -- A stat card is a styled pair of spans.
    return this.page
      .locator('.stat-card')
      .evaluateAll((cards) =>
        cards.map((card) => [...card.children].map((part) => part.textContent?.trim() ?? '')),
      );
  }
}
