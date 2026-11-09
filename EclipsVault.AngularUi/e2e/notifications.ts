import type { Page } from '@playwright/test';
import { openPalette } from './palette';

export class Notifications {
  readonly heading;
  readonly notice;
  readonly noticeParts;
  readonly filter;
  readonly rows;
  constructor(private readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Notifications', exact: true });
    // eslint-disable-next-line playwright/no-raw-locators -- The Legacy delivery notice has no role of its own; the shell's licence notice is also a .notice.
    this.notice = page.locator('app-notifications-page .notice');
    // eslint-disable-next-line playwright/no-raw-locators -- A flex row: its parts are spaced by the gap, not by text.
    this.noticeParts = this.notice.locator('> *');
    this.filter = page.getByRole('searchbox', { name: 'Filter notifications', exact: true });
    this.rows = page.getByRole('row').filter({ has: page.getByRole('cell') });
  }
  async open(): Promise<void> {
    await this.page.goto('/Notifications');
  }
  row(recipient: string) {
    return this.page
      .getByRole('row')
      .filter({ has: this.page.getByRole('cell', { name: recipient, exact: true }) });
  }
  cells(recipient: string) {
    return this.row(recipient).getByRole('cell');
  }
  /** The subject cell, which carries the email body as its tooltip. */
  subject(recipient: string, subject: string) {
    return this.row(recipient).getByRole('cell', { name: subject });
  }
  async fromSidebar(path: '/Secrets' | '/Access'): Promise<void> {
    await this.page.goto(path);
    await this.page.getByRole('link', { name: 'Notifications', exact: true }).click();
  }
  async fromPalette(path: '/Secrets' | '/Access'): Promise<void> {
    await this.page.goto(path);
    await openPalette(this.page);
    await this.page.getByRole('combobox').fill('Notifications');
    await this.page.getByRole('option', { name: /Notifications/ }).click();
  }
  async useTheme(theme: 'dark' | 'light'): Promise<void> {
    await this.page
      .context()
      .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: this.page.url() }]);
    await this.page.reload();
  }
}
