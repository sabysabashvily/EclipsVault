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
    // The confirm is a fetch, not a form post: wait for its answer, which carries the session cookie,
    // so a caller's next navigation or cookie copy cannot cancel or miss it.
    const answered = this.page.waitForResponse((response) =>
      response.url().endsWith('/api/ui/auth/enrollment/confirm'),
    );
    await this.page.getByRole('button', { name: 'Confirm enrollment', exact: true }).click();
    await answered;
  }
}
