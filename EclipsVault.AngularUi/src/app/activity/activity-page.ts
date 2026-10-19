import { httpResource } from '@angular/common/http';
import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';
import { EmptyState } from '../shared/empty-state';
import { relativeTime, utcTimestamp } from '../shared/relative-time';
import type { components, paths } from '../ui-api/schema.generated';

type Activity = paths['/api/ui/activity']['get']['responses'][200]['content']['application/json'];
type Category = components['schemas']['ActivityItemResponse']['category'];
type Severity = components['schemas']['ActivityItemResponse']['severity'];

const labels: Record<Category, string> = {
  Authentication: 'Sign-in',
  Secrets: 'Secrets',
  Sharing: 'Sharing',
  Account: 'Account',
  Security: 'Security',
  Administration: 'Admin',
  Automation: 'Automation',
  Other: 'Other',
};

// The audit view's tones: muted, warn, critical.
const tones: Record<Severity, string> = { Routine: 'muted', Notable: 'warn', Critical: 'critical' };

/** One page of the signed-in account's own actions. The server picks the account and the rows. */
@Component({
  selector: 'app-activity-page',
  imports: [EmptyState, RouterLink],
  templateUrl: './activity-page.html',
})
export class ActivityPage {
  // The page number stays in the URL, as on the Legacy page, so back and forward refetch.
  private readonly page = toSignal(
    inject(ActivatedRoute).queryParamMap.pipe(map((query) => query.get('page'))),
    {
      requireSync: true,
    },
  );

  protected readonly activity = httpResource<Activity>(() => {
    const page = this.page();
    return { url: '/api/ui/activity', params: page === null ? undefined : { page } };
  });

  /** The rows as shown, with times measured against the clock when the page arrived. */
  protected readonly items = computed(() => {
    if (!this.activity.hasValue()) return [];
    const now = new Date();
    return this.activity.value().items.map((item) => ({
      ...item,
      label: labels[item.category],
      tone: tones[item.severity],
      ago: relativeTime(item.timestampUtc, now),
      utc: utcTimestamp(item.timestampUtc),
    }));
  });

  protected readonly pageNumber = computed(() =>
    this.activity.hasValue() ? Number(this.activity.value().page) : 1,
  );
  protected readonly notableCount = computed(() =>
    this.activity.hasValue() ? Number(this.activity.value().notableCount) : 0,
  );
}
