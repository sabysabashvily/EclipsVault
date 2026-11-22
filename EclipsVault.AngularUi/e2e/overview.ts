import type { Page } from '@playwright/test';

export class Overview {
  readonly heading;
  readonly welcome;
  readonly notice;
  readonly newSecret;
  readonly activeSecrets;
  readonly expiringCard;
  readonly criticalAlertsLink;
  readonly criticalAlerts;
  readonly staffAccounts;
  readonly vaultActivity;
  readonly ownActivity;
  readonly activityList;
  readonly activity;
  readonly fullAuditLog;
  readonly browseSecrets;
  readonly provisionUser;
  readonly manageNetworks;
  readonly expiringSoon;
  readonly expiringList;
  readonly sidebarOverview;
  constructor(private readonly page: Page) {
    const main = page.getByRole('main');
    this.heading = page.getByRole('heading', { name: 'Overview', exact: true });
    this.welcome = page.getByText(/Welcome back, .+\. Here is the vault at a glance\./);
    this.notice = main.getByText(/will expire within 7 days and be shredded automatically/);
    this.newSecret = main.getByRole('link', { name: 'New secret', exact: true });
    this.activeSecrets = main.getByRole('link', { name: /Active secrets/ });
    this.expiringCard = main.getByRole('link', { name: /Expiring within 7 days/ });
    this.criticalAlertsLink = main.getByRole('link', { name: /Critical alerts \(24 h\)/ });
    this.criticalAlerts = main.getByText('Critical alerts (24 h)', { exact: true });
    this.staffAccounts = main.getByRole('link', { name: /Staff accounts/ });
    this.vaultActivity = page.getByRole('heading', { name: 'Recent activity', exact: true });
    this.ownActivity = page.getByRole('heading', { name: 'Your recent activity', exact: true });
    this.activityList = main.getByRole('list', { name: /recent activity$/i });
    this.activity = this.activityList.getByRole('listitem');
    this.fullAuditLog = main.getByRole('link', { name: 'Full audit log', exact: true });
    this.browseSecrets = main.getByRole('link', { name: /^Browse secrets/ });
    this.provisionUser = main.getByRole('link', { name: /^Provision a user/ });
    this.manageNetworks = main.getByRole('link', { name: /^Manage trusted networks/ });
    this.expiringSoon = page.getByRole('heading', { name: 'Expiring soon', exact: true });
    this.expiringList = main.getByRole('list', { name: 'Expiring soon' });
    this.sidebarOverview = page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('link', { name: 'Overview', exact: true });
  }
  async open(): Promise<void> {
    await this.page.goto('/');
  }
  expiringRow(name: string) {
    return this.expiringList.getByRole('listitem').filter({ hasText: name });
  }
  activityRow(resource: string) {
    return this.activity.filter({ hasText: resource });
  }
  criticalIconOf(row: ReturnType<Overview['activityRow']>) {
    // eslint-disable-next-line playwright/no-raw-locators -- The warning icon is decorative beside the "Critical" label, so it has no ARIA role.
    return row.locator('.critical-mark svg');
  }
  async useTheme(theme: 'dark' | 'light'): Promise<void> {
    await this.page
      .context()
      .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: this.page.url() }]);
    await this.page.reload();
  }
}
