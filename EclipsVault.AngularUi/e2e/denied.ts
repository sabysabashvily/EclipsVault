import type { Page } from '@playwright/test';

/** The Legacy page a secret's details redirect to when the reader may not open it. */
export class Denied {
  readonly heading;
  readonly requestAccess;
  readonly reason;
  readonly submit;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Access denied', exact: true });
    this.requestAccess = page.getByRole('heading', { name: 'Request access', exact: true });
    this.reason = page.getByLabel('Reason');
    this.submit = page.getByRole('button', { name: 'Submit request', exact: true });
  }

  refusal(message: string) {
    return this.page.getByText(message, { exact: true });
  }
}
