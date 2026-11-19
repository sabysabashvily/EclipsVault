import type { Page } from '@playwright/test';

/** The page a refused caller lands on, with the policy's reasons and a request form for a secret. */
export class Denied {
  readonly heading;
  readonly refusedBecause;
  readonly generic;
  readonly requestAccess;
  readonly reason;
  readonly submit;
  readonly backToSecrets;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Access denied', exact: true });
    this.refusedBecause = page.getByText('Access was refused because:', { exact: true });
    this.generic = page.getByText(
      "Your clearance, project assignment, the time window, or your network location did not satisfy the resource's requirements. The attempt has been logged.",
      { exact: true },
    );
    this.requestAccess = page.getByRole('heading', { name: 'Request access', exact: true });
    this.reason = page.getByLabel('Reason');
    this.submit = page.getByRole('button', { name: 'Submit request', exact: true });
    this.backToSecrets = page.getByRole('link', { name: 'Back to secrets', exact: true });
  }

  /** One of the policy's reasons, as the page lists it. */
  denial(reason: string) {
    return this.page.getByRole('listitem').filter({ hasText: reason });
  }

  refusal(message: string) {
    return this.page.getByText(message, { exact: true });
  }
}
