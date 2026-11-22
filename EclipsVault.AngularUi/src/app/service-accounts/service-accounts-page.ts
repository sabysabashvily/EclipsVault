import { httpResource } from '@angular/common/http';
import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { EmptyState } from '../shared/empty-state';
import { StatusLine } from '../shared/status-line';
import { PagePrimary } from '../shell/page-primary';
import type { components } from '../ui-api/schema.generated';
import { ServiceAccountNotices } from './service-account-notices';

@Component({
  selector: 'app-service-accounts-page',
  imports: [PagePrimary, RouterLink, StatusLine, EmptyState],
  templateUrl: './service-accounts-page.html',
})
export class ServiceAccountsPage {
  protected readonly accounts = httpResource<components['schemas']['ServiceAccountsResponse']>(
    () => '/api/ui/service-accounts',
  );
  /** "5 service accounts · 3 with a live key." A live key is one the account can still call with. */
  protected readonly summary = computed(() => {
    const intro = 'Non-interactive identities that retrieve secrets over the API using API keys.';
    if (!this.accounts.hasValue()) return intro;
    const accounts = this.accounts.value().serviceAccounts;
    if (accounts.length === 0) return intro;
    const live = accounts.filter(
      (account) => account.enabled && Number(account.activeKeys) > 0,
    ).length;
    const label = accounts.length === 1 ? 'service account' : 'service accounts';
    return `${accounts.length} ${label} · ${live} with a live key.`;
  });
  protected readonly message: string;

  constructor() {
    const notices = inject(ServiceAccountNotices);
    this.message = notices.deleted ? 'Service account and its keys deleted.' : '';
    notices.deleted = false;
  }
}
