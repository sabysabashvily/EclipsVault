import type { Page } from '@playwright/test';
import type { components } from '../src/app/ui-api/schema.generated';
import { openPalette } from './palette';

type Encryption = components['schemas']['EncryptionResponse'];

export class EncryptionKeys {
  readonly heading;
  readonly notice;
  readonly currentKey;
  readonly rotate;
  readonly confirmation;
  readonly keepKeys;
  readonly continue;
  /** Every rotation this page has asked the vault for. */
  readonly rotationsSent: string[] = [];
  constructor(private readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Encryption keys', exact: true });
    // eslint-disable-next-line playwright/no-raw-locators -- The notice is an unlabelled banner, as in Legacy; the shell's licence notice is one too.
    this.notice = page.locator('app-encryption-page .notice');
    this.currentKey = page
      .getByRole('row')
      .filter({ has: page.getByRole('cell', { name: 'current', exact: true }) })
      .getByRole('cell')
      .first();
    this.rotate = page.getByRole('button', { name: 'Re-wrap under current KEK', exact: true });
    this.confirmation = page.getByRole('alertdialog');
    this.keepKeys = this.confirmation.getByRole('button', { name: 'Cancel', exact: true });
    this.continue = this.confirmation.getByRole('button', { name: 'Continue', exact: true });
    page.on('request', (request) => {
      if (new URL(request.url()).pathname === '/api/ui/encryption/rotate')
        this.rotationsSent.push(request.method());
    });
  }
  async open(): Promise<void> {
    await this.page.goto('/Encryption/Index');
  }
  /**
   * Adds one secret under a loaded retired key to every status the vault answers. The run's vault
   * loads a single master key, so nothing real can await rotation there; rotation itself is proven
   * over HTTP.
   */
  async reportOnePendingSecret(): Promise<void> {
    const retired = 'kek-0e2e0e2e';
    await this.page.route('**/api/ui/encryption', async (route) => {
      const response = await route.fetch();
      const status = (await response.json()) as Encryption;
      const pending: Encryption = {
        ...status,
        loadedKekIds: [...status.loadedKekIds, retired],
        keys: [...status.keys, { kekId: retired, status: 'retired', secrets: 1, versions: 0 }],
        awaitingRotation: 1,
      };
      await route.fulfill({ response, json: pending });
    });
  }
  async fromSidebar(path: '/Secrets' | '/Access'): Promise<void> {
    await this.page.goto(path);
    await this.page.getByRole('link', { name: 'Encryption keys', exact: true }).click();
  }
  async fromPalette(path: '/Secrets' | '/Access'): Promise<void> {
    await this.page.goto(path);
    await openPalette(this.page);
    await this.page.getByRole('combobox').fill('Encryption keys');
    await this.page.getByRole('option', { name: /Encryption keys/ }).click();
  }
  async useTheme(theme: 'dark' | 'light'): Promise<void> {
    await this.page
      .context()
      .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: this.page.url() }]);
    await this.page.reload();
  }
}
