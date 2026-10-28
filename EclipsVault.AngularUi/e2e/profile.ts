import type { Page } from '@playwright/test';
import { freshTotp } from './totp';

/** The production Profile cards and the account-verification dialog they share. */
export class ProfilePage {
  readonly heading;
  readonly displayName;
  readonly email;
  readonly newEmail;
  readonly verification;
  readonly confirmation;
  readonly codes;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Profile', exact: true });
    this.displayName = page.getByLabel('Display name', { exact: true });
    this.email = page.getByLabel('Email', { exact: true });
    this.newEmail = page.getByLabel('New email', { exact: true });
    this.verification = page.getByRole('dialog', { name: 'Verify your account' });
    this.confirmation = page.getByRole('alertdialog');
    this.codes = page
      .getByRole('list')
      .filter({ has: page.getByText(/^[A-Z0-9]{5}-[A-Z0-9]{5}$/) })
      .getByRole('listitem');
  }

  async open(path = '/Profile'): Promise<void> {
    await this.page.goto(path);
  }

  async verify(password: string, secret: string, lastStep: number): Promise<number> {
    await this.verification.getByLabel('Current password', { exact: true }).fill(password);
    await this.verification
      .getByLabel('Authenticator code', { exact: true })
      .fill(await freshTotp(secret, lastStep));
    const step = Math.floor(Date.now() / 30_000);
    await this.verification.getByRole('button', { name: 'Verify account', exact: true }).click();
    return step;
  }

  async confirm(): Promise<void> {
    await this.confirmation.getByRole('button', { name: 'Continue', exact: true }).click();
  }

  async freshBudget(): Promise<void> {
    // The disposable TLS proxy supplies one address to the real production throttle.
    const windowMilliseconds = 120_000;
    await this.page.waitForTimeout(windowMilliseconds - (Date.now() % windowMilliseconds) + 1_000);
  }

  async changePassword(current: string, candidate: string): Promise<void> {
    await this.page.getByRole('link', { name: 'Change password', exact: true }).click();
    await this.page.getByLabel('Current password', { exact: true }).fill(current);
    await this.page.getByLabel('New password', { exact: true }).fill(candidate);
    await this.page.getByLabel('Confirm new password', { exact: true }).fill(candidate);
    await this.page.getByRole('button', { name: 'Change password', exact: true }).click();
  }
}
