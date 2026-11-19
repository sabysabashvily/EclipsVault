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
  readonly sidebarOverview;
  constructor(private readonly page: Page) {
    const main = page.getByRole('main');
    this.heading = page.getByRole('heading', { name: 'Overview', exact: true });
    this.welcome = page.getByText(/Welcome back, .+\. Here is the vault at a glance\./);
    // eslint-disable-next-line playwright/no-raw-locators -- The notice is an unlabelled banner, as in Legacy; the shell's licence notice is one too.
    this.notice = page.locator('app-overview-page .notice');
    this.newSecret = main.getByRole('link', { name: 'New secret', exact: true });
    this.activeSecrets = main.getByRole('link', { name: /Active secrets/ });
    this.expiringCard = main.getByRole('link', { name: /Expiring within 7 days/ });
    this.criticalAlertsLink = main.getByRole('link', { name: /Critical alerts \(24 h\)/ });
    this.criticalAlerts = main.getByText('Critical alerts (24 h)', { exact: true });
    this.staffAccounts = main.getByRole('link', { name: /Staff accounts/ });
    this.vaultActivity = page.getByRole('heading', { name: 'Recent activity', exact: true });
    this.ownActivity = page.getByRole('heading', { name: 'Your recent activity', exact: true });
    this.activityList = main.getByRole('list');
    this.activity = main.getByRole('listitem');
    this.fullAuditLog = main.getByRole('link', { name: 'Full audit log', exact: true });
    this.browseSecrets = main.getByRole('link', { name: /^Browse secrets/ });
    this.provisionUser = main.getByRole('link', { name: /^Provision a user/ });
    this.manageNetworks = main.getByRole('link', { name: /^Manage trusted networks/ });
    this.expiringSoon = page.getByRole('heading', { name: 'Expiring soon', exact: true });
    this.sidebarOverview = page
      .getByRole('navigation', { name: 'Main navigation' })
      .getByRole('link', { name: 'Overview', exact: true });
  }
  async open(): Promise<void> {
    await this.page.goto('/');
  }
  expiringRow(name: string) {
    return this.page.getByRole('row').filter({ hasText: name });
  }
  activityRow(resource: string) {
    return this.activity.filter({ hasText: resource });
  }
  async useTheme(theme: 'dark' | 'light'): Promise<void> {
    await this.page
      .context()
      .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: this.page.url() }]);
    await this.page.reload();
  }
}
