import type { Page } from '@playwright/test';
import { openPalette } from './palette';
import type { legacyShell } from './legacy-shell';
import { AppShell } from './app-shell';

export class License {
  readonly heading;
  readonly content;
  readonly status;
  readonly details;
  readonly viewLicense;
  constructor(private readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'License', exact: true });
    // eslint-disable-next-line playwright/no-raw-locators -- The shell's licence notice repeats the page's message, so reads stay inside the page.
    this.content = page.locator('app-license-page');
    // eslint-disable-next-line playwright/no-raw-locators -- The status badge sits in the Status panel's header, unlabelled as in Legacy.
    this.status = this.content.locator('.panel-header .badge');
    this.details = this.content.getByText('Licensed to', { exact: true });
    this.viewLicense = page.getByRole('link', { name: 'View license', exact: true });
  }
  message(text: string) {
    return this.content.getByText(text, { exact: true });
  }
  async open(): Promise<void> {
    await this.page.goto('/License/Index');
  }
  async fromSidebar(path: typeof legacyShell | '/Access'): Promise<void> {
    await this.page.goto(path);
    // The Angular sidebar folds its administration pages into a group; the Legacy one lists them.
    if (path === '/Access') await new AppShell(this.page).administration.click();
    await this.page.getByRole('link', { name: 'License', exact: true }).click();
  }
  async fromPalette(path: typeof legacyShell | '/Access'): Promise<void> {
    await this.page.goto(path);
    await openPalette(this.page);
    await this.page.getByRole('combobox').fill('License');
    await this.page.getByRole('option', { name: /License/ }).click();
  }
  async useTheme(theme: 'dark' | 'light'): Promise<void> {
    await this.page
      .context()
      .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: this.page.url() }]);
    await this.page.reload();
  }
}
