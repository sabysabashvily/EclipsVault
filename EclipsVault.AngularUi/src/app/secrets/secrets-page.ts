import { formatDate } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, computed, inject, LOCALE_ID, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { EmptyState } from '../shared/empty-state';
import { TableFilter } from '../shared/table-filter';
import type { components } from '../ui-api/schema.generated';

const week = 7 * 24 * 3_600_000;

/** The secrets the caller could open now. Opening one goes to its Legacy details page. */
@Component({
  selector: 'app-secrets-page',
  imports: [EmptyState, RouterLink, TableFilter],
  templateUrl: './secrets-page.html',
})
export class SecretsPage {
  private readonly locale = inject(LOCALE_ID);
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
}
