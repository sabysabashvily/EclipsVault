import type { Page } from '@playwright/test';
import { openPalette } from './palette';
import type { legacyShell } from './legacy-shell';
import { AppShell } from './app-shell';

export class Users {
  readonly heading;
  readonly newUser;
  readonly filter;
  readonly confirmation;
  readonly stepUp;
  readonly status;
  readonly rows;
  constructor(private readonly page: Page) {
    this.newUser = page.getByRole('link', { name: 'New user', exact: true });
    this.heading = page.getByRole('heading', { name: 'Users', exact: true });
    this.filter = page.getByRole('searchbox', { name: 'Filter users' });
    this.confirmation = page.getByRole('alertdialog');
    this.stepUp = page.getByRole('dialog', { name: "Confirm it's you" });
    // eslint-disable-next-line playwright/no-raw-locators -- Loading has a separate status role; scope to the shared outcome announcer.
    this.status = page.locator('app-status-line').getByRole('status');
    this.rows = page.getByRole('row');
  }
  async open(): Promise<void> {
    await this.page.goto('/Admin/Users');
  }
  async fromDashboard(): Promise<void> {
    await this.page.goto('/');
    await this.page.getByRole('link', { name: /Staff accounts Manage users/ }).click();
  }
  async avatarLoaded(username: string): Promise<boolean> {
    return this.row(username)
      .getByAltText('', { exact: true })
      .evaluate((image: HTMLImageElement) => image.complete && image.naturalWidth > 0);
  }
  async useTheme(theme: 'dark' | 'light'): Promise<void> {
    await this.page
      .context()
      .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: this.page.url() }]);
    await this.page.reload();
  }
  row(username: string) {
    return this.page.getByRole('row').filter({ hasText: `${username} ·` });
  }
  moreTrigger(username: string) {
    return this.row(username).getByText('More', { exact: true });
  }
  forceLogout(username: string) {
    return this.row(username).getByRole('menuitem', { name: 'Force logout' });
  }
  editItem(username: string) {
    return this.row(username).getByRole('menuitem', { name: 'Edit', exact: true });
  }
  async edit(username: string): Promise<void> {
    await this.more(username, 'Edit');
  }
  async disable(username: string): Promise<void> {
    await this.more(username, 'Disable');
  }
  // The verb a locked or disabled row carries beside its menu; an active row carries none.
  verbs(username: string) {
    return this.row(username).getByRole('button', { name: /^(Unlock|Enable|Disable)$/ });
  }
  async enable(username: string): Promise<void> {
    await this.row(username).getByRole('button', { name: 'Enable', exact: true }).click();
  }
  unlockButton(username: string) {
    return this.row(username).getByRole('button', { name: 'Unlock', exact: true });
  }
  async unlock(username: string): Promise<void> {
    await this.row(username).getByRole('button', { name: 'Unlock', exact: true }).click();
  }
  async cancelStepUp(): Promise<void> {
    await this.stepUp.getByRole('button', { name: 'Cancel', exact: true }).click();
  }
  async enterCode(code: string): Promise<void> {
    await this.stepUp.getByLabel('Authenticator code').fill(code);
  }
  async verifyCode(): Promise<void> {
    await this.stepUp.getByRole('button', { name: 'Verify and continue' }).click();
  }
  async fromSidebar(path: typeof legacyShell | '/Access'): Promise<void> {
    await this.page.goto(path);
    // The Angular sidebar folds its administration pages into a group; the Legacy one lists them.
    if (path === '/Access') await new AppShell(this.page).administration.click();
    await this.page.getByRole('link', { name: 'Users', exact: true }).click();
  }
  async fromPalette(path: typeof legacyShell | '/Access'): Promise<void> {
    await this.page.goto(path);
    await openPalette(this.page);
    await this.page.getByRole('combobox').fill('Users');
    await this.page.getByRole('option', { name: /Users/ }).click();
  }
  async more(username: string, action: string): Promise<void> {
    const row = this.row(username);
    await row.getByText('More', { exact: true }).click();
    await row.getByRole('menuitem', { name: action, exact: true }).click();
  }
  async confirm(verb: string): Promise<void> {
    await this.confirmation.getByRole('button', { name: verb, exact: true }).click();
  }
}
