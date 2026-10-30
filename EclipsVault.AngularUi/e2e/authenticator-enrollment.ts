import type { Page } from '@playwright/test';

export class AuthenticatorEnrollment {
  constructor(private readonly page: Page) {}

  get secret() {
    // Enrollment shows the disposable seed as unlabelled text.
    // eslint-disable-next-line playwright/no-raw-locators -- The seed has no label or semantic role.
    return this.page.locator('.secret-code');
  }

  async confirm(code: string): Promise<void> {
    await this.page.getByLabel('Confirmation code').fill(code);
    await this.page.getByRole('button', { name: 'Confirm enrollment', exact: true }).click();
  }
}
