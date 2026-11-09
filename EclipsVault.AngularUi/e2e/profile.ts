import type { Page } from '@playwright/test';
import { setTimeout } from 'node:timers/promises';
import { clearSignInBudget, readProfileTotpStep } from './profile-state';
import { currentTotpStep, freshTotp, totp } from './totp';

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

  /**
   * The id of the operation this page is preparing. The body is read in the response predicate,
   * because a navigation that follows the response drops it before a later read.
   */
  preparedOperation(): Promise<{ operationId: string }> {
    let body: { operationId: string } | undefined;
    let failed: unknown;
    return this.page
      .waitForResponse(async (response) => {
        if (
          !response.url().endsWith('/api/ui/profile/operations') ||
          response.request().method() !== 'POST'
        ) {
          return false;
        }
        try {
          body = (await response.json()) as { operationId: string };
        } catch (error) {
          failed = error;
        }
        return true;
      })
      .then(() => {
        if (failed) throw failed;
        if (!body?.operationId) throw new Error('The prepared operation had no id');
        return body;
      });
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

  /** Like fillVerification, using the step this run's database recorded for the profile account. */
  async fillOwnerVerification(password: string, secret: string, lastStep: number): Promise<number> {
    await this.verificationPassword.fill(password);
    await this.verificationCode.fill(await freshProfileTotp(secret));
    return Math.max(currentTotpStep(), lastStep);
  }

  async verify(password: string, secret: string, lastStep: number): Promise<number> {
    const step = await this.fillVerification(password, secret, lastStep);
    await this.verifyAccount.click();
    return step;
  }

  /** Like verify, for the profile account whose spent authenticator step is in this run's database. */
  async verifyOwner(password: string, secret: string, lastStep: number): Promise<number> {
    const step = await this.fillOwnerVerification(password, secret, lastStep);
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

  /**
   * The proxy has one address and the production throttle counts it in fixed windows. Clearing this
   * run's counters opens a full budget without waiting for the host clock to cross a boundary.
   */
  releaseSignInBudget(): void {
    clearSignInBudget();
  }
}

/**
 * A code for the profile account. The spent step is the one the server recorded, so a late request
 * that the server matched in the next step is not offered again. One wait, matching the original
 * authenticator helper: a second wait would walk a deliberate resend out of the mailbox minute.
 */
export async function freshProfileTotp(secret: string): Promise<string> {
  const started = Date.now();
  // Only the step the server recorded. A client step ahead of it is the clock rolling after the
  // code was made, and waiting that out pushes a deliberate resend past the mailbox minute.
  const spent = readProfileTotpStep();
  // The read is the round trip this code still has to survive. Keep it inside one step.
  const margin = Math.min(10_000, Math.max(5_000, Date.now() - started + 1_000));
  const now = Date.now();
  const remaining = 30_000 - (now % 30_000);
  if (Math.floor(now / 30_000) <= spent || remaining < margin) {
    await setTimeout(remaining + 100);
  }
  return totp(secret);
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
