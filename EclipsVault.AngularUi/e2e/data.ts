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
  readonly error;
  readonly retry;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Your data', exact: true });
    this.introduction = page.getByText('What the vault holds about you', { exact: false });
    this.accountLabels = page.getByRole('main').getByRole('term');
    this.accountValues = page.getByRole('main').getByRole('definition');
    this.notice = page.getByText('This file contains the account and security metadata', {
      exact: false,
    });
    this.download = page.getByRole('button', { name: 'Download your data (JSON)', exact: true });
    this.error = page.getByRole('main').getByRole('alert');
    this.retry = page.getByRole('button', { name: 'Try again', exact: true });
  }
}
