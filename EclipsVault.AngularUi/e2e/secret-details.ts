import type { Page } from '@playwright/test';

/** A secret's details page, which New secret, the list and both palettes open. */
export class SecretDetails {
  readonly heading;
  readonly storedMessage;
  readonly revealValue;
  readonly copy;
  readonly rotateValue;
  readonly newValue;
  readonly changeNote;
  readonly renewFor;
  readonly rotateSecret;
  readonly delete;
  readonly sharing;
  readonly shareWith;
  readonly shareExpiresInDays;
  readonly shareAccess;
  readonly versionHistory;
  readonly confirmation;
  readonly stepUp;
  readonly authenticatorCode;
  readonly verifyAndContinue;

  constructor(
    private readonly page: Page,
    name: string,
  ) {
    this.heading = page.getByRole('heading', { name, exact: true });
    this.storedMessage = this.announcement(`Secret '${name}' was envelope-encrypted and stored.`);
    this.revealValue = page.getByRole('button', { name: 'Reveal value', exact: true });
    this.copy = page.getByRole('button', { name: 'Copy', exact: true });
    this.rotateValue = page.getByText('Rotate value', { exact: true });
    this.newValue = page.getByLabel('New value', { exact: true });
    this.changeNote = page.getByLabel('Change note (optional)');
    this.renewFor = page.getByLabel('Renew for (days)');
    this.rotateSecret = page.getByRole('button', { name: 'Rotate secret', exact: true });
    this.delete = page.getByRole('button', { name: 'Delete', exact: true });
    this.sharing = page.getByRole('heading', { name: 'Sharing', exact: true });
    this.shareWith = page.getByLabel('User to share with');
    this.shareExpiresInDays = page.getByLabel('Access expires in days');
    this.shareAccess = page.getByRole('button', { name: 'Share access', exact: true });
    this.versionHistory = page.getByRole('list').filter({ hasText: 'Version ' });
    this.confirmation = page.getByRole('alertdialog');
    this.stepUp = page.getByRole('dialog', { name: "Confirm it's you" });
    this.authenticatorCode = this.stepUp.getByLabel('Authenticator code');
    this.verifyAndContinue = this.stepUp.getByRole('button', { name: 'Verify and continue' });
  }

  async open(path: string): Promise<void> {
    await this.page.goto(path);
    await this.heading.waitFor();
  }

  /** A success or refusal line at the top of the page, exactly as written. */
  announcement(text: string) {
    return this.page.getByText(text, { exact: true });
  }

  /** The revealed value under its heading, e.g. "Decrypted current value". */
  revealed(label: string) {
    return this.page.getByText(`Decrypted ${label}`, { exact: true });
  }

  version(number: number) {
    return this.versionHistory
      .getByRole('listitem')
      .filter({ has: this.page.getByText(`Version ${number}`, { exact: true }) });
  }

  share(recipient: string) {
    return this.page.getByRole('row').filter({ hasText: recipient });
  }

  async confirm(label: string): Promise<void> {
    await this.confirmation.getByRole('button', { name: label, exact: true }).click();
  }
}
