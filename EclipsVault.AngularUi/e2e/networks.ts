import { execFileSync } from 'node:child_process';
import type { Page } from '@playwright/test';
import { openPalette } from './palette';

export class Networks {
  readonly heading;
  readonly currentAddress;
  readonly address;
  readonly trustBadge;
  readonly range;
  readonly label;
  readonly trust;
  readonly confirmation;
  readonly keepBlock;
  readonly stepUp;
  readonly status;
  constructor(private readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Networks', exact: true });
    // eslint-disable-next-line playwright/no-raw-locators -- The Legacy panel owns the address and its badge.
    this.currentAddress = page.locator('.current-ip-value');
    this.address = this.currentAddress.getByRole('code');
    this.trustBadge = this.currentAddress.getByText(/^(Trusted|Not trusted)$/);
    this.range = page.getByRole('textbox', { name: 'IP or CIDR', exact: true });
    this.label = page.getByRole('textbox', { name: 'Label', exact: true });
    this.trust = page.getByRole('button', { name: 'Trust', exact: true });
    this.confirmation = page.getByRole('alertdialog');
    this.keepBlock = this.confirmation.getByRole('button', { name: 'Cancel', exact: true });
    this.stepUp = page.getByRole('dialog', { name: "Confirm it's you" });
    // eslint-disable-next-line playwright/no-raw-locators -- Loading has its own status; this is the shared outcome announcer.
    this.status = page.locator('app-status-line').getByRole('status');
  }
  async open(): Promise<void> {
    await this.page.goto('/Admin/Networks');
  }
  row(range: string) {
    return this.page
      .getByRole('row')
      .filter({ has: this.page.getByRole('cell', { name: range, exact: true }) });
  }
  remove(range: string) {
    return this.row(range).getByRole('button', { name: 'Remove', exact: true });
  }
  unblock(range: string) {
    return this.row(range).getByRole('button', { name: 'Unblock', exact: true });
  }
  blockedSince(range: string) {
    return this.row(range).getByRole('cell').nth(2);
  }
  async fromDashboard(): Promise<void> {
    await this.page.goto('/');
    await this.page.getByRole('link', { name: /Manage trusted networks/ }).click();
  }
  async fromDenied(): Promise<void> {
    await this.page.goto('/Account/Denied');
    await this.page.getByRole('link', { name: 'Manage trusted networks', exact: true }).click();
  }
  async fromSidebar(path: '/Secrets' | '/Access'): Promise<void> {
    await this.page.goto(path);
    await this.page.getByRole('link', { name: 'Networks', exact: true }).click();
  }
  async fromPalette(path: '/Secrets' | '/Access'): Promise<void> {
    await this.page.goto(path);
    await openPalette(this.page);
    await this.page.getByRole('combobox').fill('Networks');
    await this.page.getByRole('option', { name: /Networks/ }).click();
  }
  async useTheme(theme: 'dark' | 'light'): Promise<void> {
    await this.page
      .context()
      .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: this.page.url() }]);
    await this.page.reload();
  }
  async cancelProof(): Promise<void> {
    await this.stepUp.getByRole('button', { name: 'Cancel', exact: true }).click();
  }
  async prove(code: string): Promise<void> {
    await this.stepUp.getByLabel('Authenticator code').fill(code);
    await this.stepUp.getByRole('button', { name: 'Verify and continue', exact: true }).click();
  }
  async confirm(): Promise<void> {
    await this.confirmation.getByRole('button', { name: 'Continue', exact: true }).click();
  }
  async cancel(): Promise<void> {
    await this.confirmation.getByRole('button', { name: 'Cancel', exact: true }).click();
  }
}

/**
 * Writes an intrusion-defence block into this run's own Redis, in the record format the vault stores.
 * Tripping a decoy from the browser would block the browser itself, and no production screen makes a
 * block. redis-cli inside the container reads the password from its own environment.
 */
export function blockInRunStore(range: string, reason: string): void {
  if (!/^192\.0\.2\.\d{1,3}\/32$/.test(range)) throw new Error('Block only a TEST-NET-1 host');
  const project = process.env['E2E_COMPOSE_PROJECT'];
  if (!project) throw new Error('Run through npm run e2e, which names the compose project');
  const record = JSON.stringify({ Reason: reason, BlockedAtUtc: new Date().toISOString() });
  const answer = execFileSync(
    'docker',
    [
      'compose',
      '--project-name',
      project,
      '--file',
      'e2e/compose.yml',
      'exec',
      '-T',
      'redis',
      'redis-cli',
      'SET',
      `eclipsvault:ipblock:${range}`,
      record,
      'NX',
    ],
    { encoding: 'utf8' },
  );
  if (answer.trim() !== 'OK') throw new Error(`The block on ${range} was not written`);
}
