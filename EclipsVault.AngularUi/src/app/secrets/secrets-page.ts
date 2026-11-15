import { formatDate } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, computed, inject, LOCALE_ID, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { EmptyState } from '../shared/empty-state';
import { StatusLine } from '../shared/status-line';
import { TableFilter } from '../shared/table-filter';
import { PAGE_LOCATION } from '../shell/command-palette';
import type { components } from '../ui-api/schema.generated';

const week = 7 * 24 * 3_600_000;

/** The secrets the caller could open now. Opening one goes to its Legacy details page. */
@Component({
  selector: 'app-secrets-page',
  imports: [EmptyState, RouterLink, StatusLine, TableFilter],
  templateUrl: './secrets-page.html',
  // Legacy's rows open their secret on a click anywhere but a control; the Open link is the keyboard's way.
  host: { '(click)': 'openRow($event)' },
})
export class SecretsPage {
  private readonly locale = inject(LOCALE_ID);
  private readonly location = inject(PAGE_LOCATION);
  protected readonly deleted: boolean;
  protected readonly secrets = httpResource<components['schemas']['SecretsResponse']>(
    () => '/api/ui/secrets',
  );
  protected readonly query = signal('');
  private readonly rows = computed(() => {
    if (!this.secrets.hasValue()) return [];
    const now = Date.now();
    return this.secrets.value().secrets.map((secret) => ({
      ...secret,
      expires: secret.expiresAtUtc
        ? formatDate(secret.expiresAtUtc, 'yyyy-MM-dd HH:mm', this.locale, 'UTC')
        : '—',
      soon: secret.expiresAtUtc !== null && new Date(secret.expiresAtUtc).getTime() <= now + week,
    }));
  });
  protected readonly count = computed(() => this.rows().length);
  // Legacy's filter matched a row's text, so it matches what the row shows.
  protected readonly filtered = computed(() => {
    const query = this.query().trim().toLowerCase();
    return this.rows().filter((row) =>
      [
        row.name,
        row.project,
        row.environment,
        row.sensitivityLabel,
        row.expires,
        row.soon ? 'soon' : '',
        'Open',
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

  protected openRow(event: MouseEvent): void {
    const target = event.target as Element;
    if (target.closest('a, button, form, input, details')) return;
    const details = target.closest('tr[data-href]')?.getAttribute('data-href');
    if (details) this.location.assign(details);
  }
}
