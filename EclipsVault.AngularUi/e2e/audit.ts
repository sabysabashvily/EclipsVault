import type { Page } from '@playwright/test';
import { openPalette } from './palette';
import type { legacyShell } from './legacy-shell';
import { AppShell } from './app-shell';

export class AuditLog {
  readonly heading;
  readonly summary;
  readonly notice;
  readonly status;
  readonly latestCheckpoint;
  readonly verify;
  readonly checkpoint;
  readonly export;
  readonly filter;
  readonly stepUp;
  readonly proofCode;
  readonly cancelProofButton;
  readonly verifyProofButton;
  constructor(private readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Audit log', exact: true });
    this.summary = page.getByText(/Newest first · \d+ entries\./);
    // eslint-disable-next-line playwright/no-raw-locators -- The notice is an unlabelled banner, as in Legacy; the shell's licence notice is one too.
    this.notice = page.locator('app-audit-page .notice');
    // eslint-disable-next-line playwright/no-raw-locators -- Loading has its own status; this is the shared outcome announcer.
    this.status = page.locator('app-status-line').getByRole('status');
    this.latestCheckpoint = page.getByRole('definition');
    this.verify = page.getByRole('button', { name: 'Verify integrity', exact: true });
    this.checkpoint = page.getByRole('button', { name: 'Create signed checkpoint', exact: true });
    this.export = page.getByRole('button', { name: 'Export signed bundle', exact: true });
    this.filter = page.getByRole('searchbox', { name: 'Filter audit entries', exact: true });
    this.stepUp = page.getByRole('dialog', { name: "Confirm it's you" });
    this.proofCode = this.stepUp.getByLabel('Authenticator code');
    this.cancelProofButton = this.stepUp.getByRole('button', { name: 'Cancel', exact: true });
    this.verifyProofButton = this.stepUp.getByRole('button', {
      name: 'Verify and continue',
      exact: true,
    });
  }
  async open(): Promise<void> {
    await this.page.goto('/Audit/Index');
  }
  rows() {
    return this.page.getByRole('row').filter({ has: this.page.getByRole('cell') });
  }
  async prove(code: string): Promise<void> {
    await this.proofCode.fill(code);
    await this.verifyProofButton.click();
  }
  async cancelProof(): Promise<void> {
    await this.cancelProofButton.click();
  }
  async fromDashboard(link: RegExp): Promise<void> {
    await this.page.goto('/');
    await this.page.getByRole('link', { name: link }).click();
  }
  async fromSidebar(path: typeof legacyShell | '/Access'): Promise<void> {
    await this.page.goto(path);
    // The Angular sidebar folds its administration pages into a group; the Legacy one lists them.
    if (path === '/Access') await new AppShell(this.page).administration.click();
    await this.page.getByRole('link', { name: 'Audit log', exact: true }).click();
  }
  async fromPalette(path: typeof legacyShell | '/Access'): Promise<void> {
    await this.page.goto(path);
    await openPalette(this.page);
    await this.page.getByRole('combobox').fill('Audit log');
    await this.page.getByRole('option', { name: /Audit log/ }).click();
  }
  async useTheme(theme: 'dark' | 'light'): Promise<void> {
    await this.page
      .context()
      .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: this.page.url() }]);
    await this.page.reload();
  }
}
