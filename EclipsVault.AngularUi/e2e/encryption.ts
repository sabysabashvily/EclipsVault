import type { Page, Route } from '@playwright/test';
import type { components } from '../src/app/ui-api/schema.generated';
import { openPalette } from './palette';
import { AppShell } from './app-shell';

type Encryption = components['schemas']['EncryptionResponse'];

export class EncryptionKeys {
  readonly heading;
  readonly verdict;
  readonly nextKeySteps;
  readonly currentKey;
  readonly rewrap;
  readonly rotate;
  readonly confirmation;
  readonly keepKeys;
  readonly continue;
  readonly rotationsSent: string[] = [];
  constructor(private readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Encryption keys', exact: true });
    this.verdict = page.getByText(
      /^(All current|Re-wrap pending|Re-wrapping|Action needed|Stopped partway)$/,
    );
    this.nextKeySteps = page.getByRole('region', {
      name: 'Moving to a new master key',
      exact: true,
    });
    this.currentKey = page
      .getByRole('row')
      .filter({ has: page.getByRole('cell', { name: 'Current', exact: true }) })
      .getByRole('cell')
      .first();
    // The figures and the one action; only there while something waits.
    this.rewrap = page.getByRole('region', { name: 'Re-wrap', exact: true });
    this.rotate = this.rewrap.getByRole('button', { name: /^Re-wrap(ping…| \d+ items?)$/ });
    this.confirmation = page.getByRole('alertdialog');
    this.keepKeys = this.confirmation.getByRole('button', { name: 'Cancel', exact: true });
    this.continue = this.confirmation.getByRole('button', { name: /^Re-wrap \d+ items?$/ });
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
  // A re-wrap the vault answers with `response`, sent only once `release` resolves.
  async answerRewrap(response: Parameters<Route['fulfill']>[0], release = Promise.resolve()) {
    await this.page.route('**/api/ui/encryption/rotate', async (route) => {
      await release;
      await route.fulfill(response);
    });
  }
  async fromSidebar(path: '/Account/Denied' | '/Access'): Promise<void> {
    await this.page.goto(path);
    // The Angular sidebar folds its administration pages into a group; the Legacy one lists them.
    if (path === '/Access') await new AppShell(this.page).administration.click();
    await this.page.getByRole('link', { name: 'Encryption keys', exact: true }).click();
  }
  async fromPalette(path: '/Account/Denied' | '/Access'): Promise<void> {
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
