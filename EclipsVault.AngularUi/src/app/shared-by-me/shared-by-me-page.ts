import { DatePipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ConfirmationDialog } from '../shared/confirmation-dialog';
import { EmptyState } from '../shared/empty-state';
import { expiry, pillDate } from '../shared/expiry';
import { StatusLine } from '../shared/status-line';
import type { paths } from '../ui-api/schema.generated';
import { UiApiError } from '../ui-api/ui-api-errors';

type SharedByMe =
  paths['/api/ui/shared-by-me']['get']['responses'][200]['content']['application/json'];
type Share = SharedByMe['shares'][number];

@Component({
  selector: 'app-shared-by-me-page',
  imports: [DatePipe, ConfirmationDialog, EmptyState, RouterLink, StatusLine],
  templateUrl: './shared-by-me-page.html',
})
export class SharedByMePage {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly confirmation = viewChild.required(ConfirmationDialog);
  private pending: Share | null = null;

  protected readonly shares = httpResource<SharedByMe>(() => '/api/ui/shared-by-me');
  protected readonly busy = signal(false);
  protected readonly status = signal({ message: '', error: false });

  /** Soonest-ending first, shares with no end date last; the server's order breaks ties. */
  protected readonly sorted = computed(() =>
    this.shares.hasValue()
      ? [...this.shares.value().shares].sort(
          (a, b) =>
            (a.expiresAtUtc ? Date.parse(a.expiresAtUtc) : Infinity) -
            (b.expiresAtUtc ? Date.parse(b.expiresAtUtc) : Infinity),
        )
      : [],
  );

  protected readonly context = computed(() => {
    const consequence = 'Revoking takes effect on their next request.';
    const shares = this.sorted();
    if (shares.length === 0) return consequence;
    const count = `${shares.length} ${shares.length === 1 ? 'share' : 'shares'} you gave`;
    const soonest = shares[0].expiresAtUtc;
    const ends = soonest ? `soonest ends ${pillDate(soonest, new Date())}` : 'none has an end date';
    return `${count} · ${ends}. ${consequence}`;
  });

  protected expiry(expiresAtUtc: string | null) {
    return expiry(expiresAtUtc, new Date());
  }

  protected ask(share: Share): void {
    if (this.busy()) return;
    this.pending = share;
    this.confirmation().open(
      `Revoke ${share.recipient}'s access to '${share.secretName}'? They lose access on their next request.`,
      {
        title: `Revoke ${share.recipient}'s access?`,
        confirm: 'Revoke access',
        facts: [
          ['Secret', share.secretName],
          ['Recipient', share.recipient],
          ['Access until', this.expiry(share.expiresAtUtc).text],
        ],
        note: 'Saved to the audit log',
      },
    );
  }

  protected decide(confirmed: boolean): void {
    const share = this.pending;
    this.pending = null;
    if (!confirmed || share === null || this.busy()) return;

    this.busy.set(true);
    this.status.set({ message: '', error: false });
    this.http
      .delete<void>(`/api/ui/shared-by-me/${share.shareId}`)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => {
          this.status.set({ message: 'Access revoked.', error: false });
          this.shares.reload();
        },
        error: (error: unknown) => {
          this.status.set({
            message:
              error instanceof UiApiError ? error.title : 'Could not revoke access. Try again.',
            error: true,
          });
          // A share that is already gone must not keep its row and Revoke button.
          if (error instanceof UiApiError && error.code === 'share_not_found') this.shares.reload();
        },
      });
  }
}
