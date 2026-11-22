import type { Page } from '@playwright/test';

/** The anonymous activation page at `/Account/Activate#<token>` (ADR-0033). */
export class Activation {
  readonly heading;
  readonly lead;
  readonly username;
  readonly newPassword;
  readonly showPassword;
  readonly confirmPassword;
  readonly confirmationCode;
  readonly qrCode;
  readonly secret;
  readonly submitButton;
  readonly alert;
  constructor(private readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Activate your account', exact: true });
    this.lead = page.getByText(
      'Choose your password and set up your authenticator. Multi-factor sign-in is mandatory.',
      { exact: true },
    );
    this.username = page.getByLabel('Username', { exact: true });
    this.newPassword = page.getByLabel('New password', { exact: true });
    this.showPassword = page.getByRole('button', { name: 'Show password', exact: true });
    this.confirmPassword = page.getByLabel('Confirm new password', { exact: true });
    this.confirmationCode = page.getByLabel('Confirmation code', { exact: true });
    this.qrCode = page.getByRole('img', {
      name: 'QR code containing the authenticator enrollment secret',
      exact: true,
    });
    // eslint-disable-next-line playwright/no-raw-locators -- The manual seed is unlabelled monospace text.
    this.secret = page.locator('.secret-code');
    this.submitButton = page.getByRole('button', { name: 'Activate account', exact: true });
    this.alert = page.getByRole('alert');
  }

  /** Sets the theme cookie before opening so a reload cannot drop the fragment token. */
  async setTheme(theme: 'dark' | 'light'): Promise<void> {
    const baseUrl = process.env['E2E_BASE_URL'];
    if (!baseUrl) throw new Error('E2E_BASE_URL is required to set the activation theme');
    await this.page
      .context()
      .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: baseUrl, secure: true }]);
  }

  async open(token?: string) {
    return this.page.goto(token ? `/Account/Activate#${token}` : '/Account/Activate');
  }

  async qrCodeLoaded(): Promise<boolean> {
    return this.qrCode.evaluate(
      (image: HTMLImageElement) => image.complete && image.naturalWidth > 0,
    );
  }

  async activate(password: string, code: string): Promise<void> {
    await this.newPassword.fill(password);
    await this.confirmPassword.fill(password);
    await this.confirmationCode.fill(code);
    await this.submitButton.click();
  }
}
