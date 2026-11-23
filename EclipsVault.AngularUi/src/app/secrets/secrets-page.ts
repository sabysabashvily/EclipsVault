import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { EmptyState } from '../shared/empty-state';
import { expiry } from '../shared/expiry';
import { StatusLine } from '../shared/status-line';
import { TableFilter } from '../shared/table-filter';
import type { components } from '../ui-api/schema.generated';
import { SecretsTabs } from './secrets-tabs';

/** The secrets the caller could open now. Opening one goes to its details page. */
@Component({
  selector: 'app-secrets-page',
  imports: [EmptyState, RouterLink, SecretsTabs, StatusLine, TableFilter],
  templateUrl: './secrets-page.html',
  host: { '(click)': 'openRow($event)' },
})
export class SecretsPage {
  protected readonly deleted: boolean;
  protected readonly secrets = httpResource<components['schemas']['SecretsResponse']>(
    () => '/api/ui/secrets',
  );
  protected readonly query = signal('');

  /** Soonest-expiring first, secrets with no expiry last; the server's order breaks ties. */
  private readonly rows = computed(() => {
    if (!this.secrets.hasValue()) return [];
    const now = new Date();
    return [...this.secrets.value().secrets]
      .sort(
        (a, b) =>
          (a.expiresAtUtc ? Date.parse(a.expiresAtUtc) : Infinity) -
          (b.expiresAtUtc ? Date.parse(b.expiresAtUtc) : Infinity),
      )
      .map((secret) => ({
        ...secret,
        expiry: expiry(secret.expiresAtUtc, now),
      }));
  });

  protected readonly count = computed(() => this.rows().length);
  protected readonly expiringCount = computed(
    () => this.rows().filter((row) => row.expiry.tone !== null).length,
  );
  protected readonly urgentTone = computed<'danger' | 'warn' | null>(() => {
    const rows = this.rows();
    if (rows.some((row) => row.expiry.tone === 'danger')) return 'danger';
    if (rows.some((row) => row.expiry.tone === 'warn')) return 'warn';
    return null;
  });

  protected readonly introduction = computed(() => {
    const total = this.count();
    const soon = this.expiringCount();
    const countText = `${total} active secret${total === 1 ? '' : 's'}`;
    const soonText =
      soon > 0 ? ` · ${soon} ${soon === 1 ? 'expires' : 'expire'} within 7 days` : '';
    return `${countText}${soonText}. Opening one records an audit entry.`;
  });

  protected readonly filtered = computed(() => {
    const query = this.query().trim().toLowerCase();
    if (!query) return this.rows();
    return this.rows().filter((row) =>
      [
        row.name,
        row.project,
        row.environment,
        row.sensitivityLabel,
        row.expiry.text,
        row.expiry.title ?? '',
        row.expiry.tone ? 'soon' : '',
      ]
        .join(' ')
        .toLowerCase()
        .includes(query),
    );
  });

  constructor() {
    const route = inject(ActivatedRoute);
    this.deleted = route.snapshot.queryParamMap.get('deleted') === '1';
    if (!this.deleted) return;
    // The outcome is shown once: reloading or bookmarking the page must not show it again.
    void inject(Router).navigate([], {
      relativeTo: route,
      queryParams: { deleted: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  // A titled pill sits above .row-link::after so its tooltip works; clicking it follows the same link.
  protected openRow(event: MouseEvent): void {
    const target = event.target as Element;
    if (target.closest('a, button, form, input, details')) return;
    target.closest('tr')?.querySelector<HTMLAnchorElement>('a.row-link')?.click();
  }
}
