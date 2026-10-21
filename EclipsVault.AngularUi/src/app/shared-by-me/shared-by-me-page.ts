import { DatePipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import { Component, DestroyRef, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ConfirmationDialog } from '../shared/confirmation-dialog';
import { EmptyState } from '../shared/empty-state';
import { StatusLine } from '../shared/status-line';
import type { paths } from '../ui-api/schema.generated';
import { UiApiError } from '../ui-api/ui-api-errors';

type SharedByMe =
  paths['/api/ui/shared-by-me']['get']['responses'][200]['content']['application/json'];
type Share = SharedByMe['shares'][number];

@Component({
  selector: 'app-shared-by-me-page',
  imports: [DatePipe, ConfirmationDialog, EmptyState, StatusLine],
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

  protected ask(share: Share): void {
    if (this.busy()) return;
    this.pending = share;
    this.confirmation().open(
      `Revoke ${share.recipient}'s access to '${share.secretName}'? They lose access on their next request.`,
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
