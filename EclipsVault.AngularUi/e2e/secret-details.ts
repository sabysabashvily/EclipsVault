import type { Page } from '@playwright/test';

/** A secret's Legacy details page, which New secret, the list and both palettes open. */
export class SecretDetails {
  readonly heading;
  readonly storedMessage;

  constructor(page: Page, name: string) {
    this.heading = page.getByRole('heading', { name, exact: true });
    this.storedMessage = page.getByText(`Secret '${name}' was envelope-encrypted and stored.`, {
      exact: true,
    });
  }
}
