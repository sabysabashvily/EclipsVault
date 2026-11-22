import type { Page } from '@playwright/test';

export class RecoveryCodeSignIn {
  readonly code;
  readonly notice;
  readonly continue;

  constructor(page: Page) {
    this.code = page.getByLabel('Recovery code');
    this.notice = page
      .getByRole('status')
      .filter({ hasText: 'You signed in with a recovery code' });
    this.continue = page.getByRole('button', { name: 'Continue', exact: true });
  }
}
