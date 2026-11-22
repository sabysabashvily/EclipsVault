import { httpResource } from '@angular/common/http';
import { Component, computed } from '@angular/core';
import { RouterLink } from '@angular/router';
import { EmptyState } from '../shared/empty-state';
import { expiry, pillDate } from '../shared/expiry';
import { SecretsTabs } from '../secrets/secrets-tabs';
import type { paths } from '../ui-api/schema.generated';

type SharedWithMe =
  paths['/api/ui/shared-with-me']['get']['responses'][200]['content']['application/json'];

@Component({
  selector: 'app-shared-with-me-page',
  imports: [EmptyState, RouterLink, SecretsTabs],
  templateUrl: './shared-with-me-page.html',
  host: { '(click)': 'openRow($event)' },
})
export class SharedWithMePage {
  protected readonly shares = httpResource<SharedWithMe>(() => '/api/ui/shared-with-me');

  /** Soonest-ending first, shares with no end date last; the server's order breaks ties. */
  protected readonly sorted = computed(() =>
    this.shares.hasValue()
      ? [...this.shares.value().shares].sort(
          (a, b) =>
            (a.accessUntilUtc ? Date.parse(a.accessUntilUtc) : Infinity) -
            (b.accessUntilUtc ? Date.parse(b.accessUntilUtc) : Infinity),
        )
      : [],
  );

  protected readonly context = computed(() => {
    const shares = this.sorted();
    const count = `${shares.length} ${shares.length === 1 ? 'secret' : 'secrets'} shared with you`;
    const soonest = shares[0]?.accessUntilUtc;
    return soonest
      ? `${count} · soonest ends ${pillDate(soonest, new Date())}.`
      : `${count} · none of them end.`;
  });

  protected expiry(accessUntilUtc: string | null) {
    return expiry(accessUntilUtc, new Date());
  }

  // A titled pill sits above .row-link::after so its tooltip works; clicking it follows the same link.
  protected openRow(event: MouseEvent): void {
    const target = event.target as Element;
    if (target.closest('a, button, form, input, details')) return;
    target.closest('tr')?.querySelector<HTMLAnchorElement>('a.row-link')?.click();
  }
}
