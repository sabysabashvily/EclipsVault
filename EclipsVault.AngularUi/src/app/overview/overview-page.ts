import { httpResource } from '@angular/common/http';
import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CurrentAccount } from '../account/current-account';
import { relativeTime } from '../shared/relative-time';
import type { components } from '../ui-api/schema.generated';

type Overview = components['schemas']['OverviewResponse'];

const hour = 3_600_000;
const day = 24 * hour;

/** The vault at a glance. Administrator cards and links follow the areas `/api/ui/me` grants. */
@Component({
  selector: 'app-overview-page',
  imports: [RouterLink],
  templateUrl: './overview-page.html',
})
export class OverviewPage {
  private readonly me = inject(CurrentAccount).me;
  private readonly areas = computed(() => new Set(this.me.hasValue() ? this.me.value().areas : []));
  protected readonly overview = httpResource<Overview>(() => '/api/ui/overview');
  protected readonly mayOpenAuditLog = computed(() => this.areas().has('audit'));
  protected readonly mayManageUsers = computed(() => this.areas().has('users'));
  protected readonly mayManageNetworks = computed(() => this.areas().has('networks'));
  protected readonly expiring = computed(() =>
    this.overview.hasValue() ? Number(this.overview.value().expiringWithin7Days) : 0,
  );
  protected readonly critical = computed(() =>
    this.overview.hasValue() ? Number(this.overview.value().criticalEventsLast24h) : 0,
  );
  protected readonly events = computed(() => {
    if (!this.overview.hasValue()) return [];
    const now = new Date();
    return this.overview
      .value()
      .recentEvents.map((event) => ({ ...event, ago: relativeTime(event.timestampUtc, now) }));
  });
  protected readonly expiringSoon = computed(() => {
    if (!this.overview.hasValue()) return [];
    const now = Date.now();
    return this.overview.value().expiringSoon.map((secret) => {
      const until = new Date(secret.expiresAtUtc).getTime() - now;
      return {
        ...secret,
        expires: new Date(secret.expiresAtUtc).toISOString().slice(0, 16).replace('T', ' '),
        withinDay: until <= day,
        until: untilLabel(until),
      };
    });
  });
}

/** Legacy's "now", "in Nh" under a day (at least 1h), then "in Nd". */
function untilLabel(until: number): string {
  if (until <= 0) return 'now';
  if (until < day) return `in ${Math.max(1, Math.trunc(until / hour))}h`;
  return `in ${Math.trunc(until / day)}d`;
}
