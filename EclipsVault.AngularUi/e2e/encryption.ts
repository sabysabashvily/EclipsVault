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
  // The throwaway vault loads one key and has no pending rows. HTTP tests prove rotation;
  // this complete fixture lets the browser open and cancel its confirmation.
  async reportOnePendingSecret(): Promise<void> {
    const current = 'kek-0e2e0001';
    const retired = 'kek-0e2e0e2e';
    await this.page.route('**/api/ui/encryption', async (route) => {
      const response = await route.fetch();
      const pending = {
        currentKekId: current,
        loadedKekIds: [current, retired],
        keys: [
          { kekId: current, status: 'current', secrets: 0, versions: 0 },
          { kekId: retired, status: 'retired', secrets: 1, versions: 0 },
        ],
        awaitingRotation: 1,
        onMissingKeys: 0,
      } satisfies Encryption;
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
