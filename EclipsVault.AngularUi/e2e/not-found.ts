import type { Page } from '@playwright/test';

export class NotFoundPage {
  readonly heading;
  readonly explanation;
  readonly backToSafety;

  constructor(private readonly page: Page) {
    this.heading = page.getByRole('heading', { name: '404 — Page not found', exact: true });
    this.explanation = page.getByText('There is no page at this address.', { exact: true });
    this.backToSafety = page.getByRole('link', { name: 'Back to safety', exact: true });
  }

  open(path: string) {
    return this.page.goto(path);
  }
}
