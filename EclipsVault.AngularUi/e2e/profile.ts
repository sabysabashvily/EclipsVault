import type { Page } from '@playwright/test';
import { currentTotpStep, freshTotp } from './totp';

/** The production Profile cards and the account-verification dialog they share. */
export class ProfilePage {
  readonly heading;
  readonly displayName;
  readonly email;
  readonly newEmail;
  readonly mailboxCode;
  readonly resendCode;
  readonly confirmEmail;
  readonly resetAuthenticator;
  readonly verification;
  readonly verificationPassword;
  readonly verificationCode;
  readonly verificationMethod;
  readonly recoveryCode;
  readonly verifyAccount;
  readonly cancelVerification;
  readonly confirmation;
  readonly cancelConfirmation;
  readonly codes;
  readonly avatarFile;

  constructor(readonly page: Page) {
    // eslint-disable-next-line playwright/no-raw-locators -- The hidden native picker has no visible label.
    this.avatarFile = page.locator('input[type="file"]');
    this.heading = page.getByRole('heading', { name: 'Profile', exact: true });
    this.displayName = page.getByLabel('Display name', { exact: true });
    this.email = page.getByLabel('Email', { exact: true });
    this.newEmail = page.getByLabel('New email', { exact: true });
    this.mailboxCode = page.getByLabel('Mailbox confirmation code', { exact: true });
    this.resendCode = page.getByRole('button', { name: 'Resend code', exact: true });
    this.confirmEmail = page.getByRole('button', { name: 'Confirm email', exact: true });
    this.resetAuthenticator = page.getByRole('button', {
      name: 'Reset authenticator',
      exact: true,
    });
    this.verification = page.getByRole('dialog', { name: 'Verify your account' });
    this.verificationPassword = this.verification.getByLabel('Current password', { exact: true });
    this.verificationCode = this.verification.getByLabel('Authenticator code', { exact: true });
    this.verificationMethod = this.verification.getByLabel('Verify with', { exact: true });
    this.recoveryCode = this.verification.getByLabel('Recovery code', { exact: true });
    this.verifyAccount = this.verification.getByRole('button', {
      name: 'Verify account',
      exact: true,
    });
    this.cancelVerification = this.verification.getByRole('button', {
      name: 'Cancel',
      exact: true,
    });
    this.confirmation = page.getByRole('alertdialog');
    this.cancelConfirmation = this.confirmation.getByRole('button', {
      name: 'Cancel',
      exact: true,
    });
    this.codes = page
      .getByRole('list')
      .filter({ has: page.getByText(/^[A-Z0-9]{5}-[A-Z0-9]{5}$/) })
      .getByRole('listitem');
  }

  async open(path = '/Profile'): Promise<void> {
    await this.page.goto(path);
  }

  async themeAppearance() {
    return this.displayName.evaluate((input) => ({
      background: getComputedStyle(document.body).backgroundColor,
      text: getComputedStyle(document.body).color,
      field: getComputedStyle(input).backgroundColor,
    }));
  }

  async recoveryAppearance() {
    return this.codes.first().evaluate((code) => {
      const style = getComputedStyle(code);
      const list = getComputedStyle(code.parentElement!);
      return {
        background: style.backgroundColor,
        text: style.color,
        fontSize: style.fontSize,
        letterSpacing: style.letterSpacing,
        radius: style.borderRadius,
        columns: list.gridTemplateColumns.split(' ').length,
        gap: list.gap,
      };
    });
  }

  /** Fills the dialog with a fresh code and returns the TOTP step it used, which cannot be reused. */
  async fillVerification(password: string, secret: string, lastStep: number): Promise<number> {
    await this.verificationPassword.fill(password);
    await this.verificationCode.fill(await freshTotp(secret, lastStep));
    return currentTotpStep();
  }

  async verify(password: string, secret: string, lastStep: number): Promise<number> {
    const step = await this.fillVerification(password, secret, lastStep);
    await this.verifyAccount.click();
    return step;
  }

  /**
   * The alert announcing `text`. Right after a proof is submitted the dialog's own, still empty
   * alert region is in the page too, so a bare alert locator would match two.
   */
  alertSaying(text: string) {
    return this.page.getByRole('alert').filter({ hasText: text });
  }

  async confirm(): Promise<void> {
    await this.confirmation.getByRole('button', { name: 'Continue', exact: true }).click();
  }

  async signOutThisBrowser(): Promise<void> {
    await this.page.request.get('/api/ui/me');
    const token = (await this.page.context().cookies()).find(
      (cookie) => cookie.name === 'XSRF-TOKEN',
    );
    if (!token) throw new Error('The signed-in browser has no antiforgery token');
    await this.page.request.post('/Account/Logout', {
      headers: { RequestVerificationToken: decodeURIComponent(token.value) },
    });
    await this.page.goto('/Account/Login');
  }

  async waitForNextThrottleWindow(): Promise<void> {
    // The disposable TLS proxy supplies one address to the real production throttle.
    const windowMilliseconds = 120_000;
    await this.page.waitForTimeout(windowMilliseconds - (Date.now() % windowMilliseconds) + 1_000);
  }
}

/** The separate Change password page that Profile links to. */
export class ChangePasswordPage {
  readonly heading;
  readonly currentPassword;
  readonly newPassword;
  readonly confirmPassword;
  readonly strength;
  readonly showPassword;
  readonly hidePassword;
  readonly submit;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Change password', exact: true });
    // By role, not label: the closed verification dialog also labels a hidden "Current password",
    // and a label locator can settle on it before the form has loaded.
    this.currentPassword = page.getByRole('textbox', { name: 'Current password', exact: true });
    this.newPassword = page.getByRole('textbox', { name: 'New password', exact: true });
    this.confirmPassword = page.getByRole('textbox', { name: 'Confirm new password', exact: true });
    this.strength = page.getByRole('img', { name: /^Password strength: / });
    this.showPassword = page.getByRole('button', { name: 'Show password', exact: true });
    this.hidePassword = page.getByRole('button', { name: 'Hide password', exact: true });
    this.submit = page.getByRole('button', { name: 'Change password', exact: true });
  }

  /** Follows Profile's link, then submits the form so the page prepares the change. */
  async changeFromProfile(current: string, candidate: string): Promise<void> {
    await this.page.getByRole('link', { name: 'Change password', exact: true }).click();
    await this.currentPassword.fill(current);
    await this.newPassword.fill(candidate);
    await this.confirmPassword.fill(candidate);
    await this.submit.click();
  }

  async meterAppearance() {
    return this.strength.evaluate((track) => ({
      height: getComputedStyle(track).height,
      background: getComputedStyle(track).backgroundColor,
      fill: getComputedStyle(track.firstElementChild!).backgroundColor,
      full:
        track.firstElementChild!.getBoundingClientRect().width ===
        track.getBoundingClientRect().width,
    }));
  }

  async revealIcon(button: 'Show password' | 'Hide password') {
    // eslint-disable-next-line playwright/no-raw-locators -- Visual parity compares Legacy's eye icon geometry.
    return this.page
      .getByRole('button', { name: button, exact: true })
      .locator('svg')
      .evaluate((svg) =>
        [...svg.children].map(
          (shape) =>
            `${shape.tagName}:${[...shape.attributes].map((attribute) => `${attribute.name}=${attribute.value}`).join(',')}`,
        ),
      );
  }
}

/** Native canvas produces disposable image inputs; none contains credential material. */
export async function avatarImage(
  page: Page,
  width: number,
  height: number,
  mimeType: string,
): Promise<Buffer> {
  const data = await page.evaluate(
    ({ width, height, mimeType }) => {
      const canvas = document.createElement('canvas');
      canvas.width = width;
      canvas.height = height;
      const context = canvas.getContext('2d')!;
      context.fillStyle = '#00ff00';
      context.fillRect(0, 0, width, height);
      return canvas.toDataURL(mimeType).split(',')[1]!;
    },
    { width, height, mimeType },
  );
  return Buffer.from(data, 'base64');
}
