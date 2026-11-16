import type { Page } from '@playwright/test';

/** The Legacy Secrets list, where links from the Angular pages land. */
export class SecretsList {
  readonly heading;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Secrets', exact: true });
  }
}
