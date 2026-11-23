import type { Page } from '@playwright/test';

export class AuthenticatorVerification {
  readonly code;
  readonly recoveryCode;

  constructor(page: Page) {
    this.code = page.getByLabel('Authenticator code');
    this.recoveryCode = page.getByRole('link', { name: 'Use a recovery code', exact: true });
  }
}
