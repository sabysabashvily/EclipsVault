import { formatDate } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import { Component, DestroyRef, inject, LOCALE_ID, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';
import { ConfirmationDialog } from '../shared/confirmation-dialog';
import { EmptyState } from '../shared/empty-state';
import { StatusLine } from '../shared/status-line';
import type { components, paths } from '../ui-api/schema.generated';
import { UiApiError } from '../ui-api/ui-api-errors';

type AccessRequests =
  paths['/api/ui/access-requests']['get']['responses'][200]['content']['application/json'];
type RequestToReview = components['schemas']['RequestToReviewResponse'];
type FiledRequest = components['schemas']['FiledRequestResponse'];
type Decision = 'approve' | 'reject' | 'withdraw';

/** What the Legacy page flashed after filing from the Denied page, by the code in its redirect. */
const refusals = new Map<string, string>([
  [
    'share_cannot_help',
    "A share would not help: your clearance is below this secret's sensitivity.",
  ],
  [
    'already_able_to_open',
    'A share would not help: your project, clearance or an active share already covers this secret.',
  ],
  ['already_pending', 'You already have a pending request for this secret.'],
  ['reason_too_short', 'Add a short reason so a reviewer can decide.'],
  ['secret_not_found', 'That secret no longer exists.'],
]);

const decided: Record<Decision, string> = {
  approve: 'Request approved — a grant was created for the requester.',
  reject: 'Request rejected.',
  withdraw: 'Request withdrawn.',
};

const badges: Record<FiledRequest['status'], { tone: string; label: string }> = {
  Pending: { tone: 'tone-warn', label: 'Pending' },
  Approved: { tone: 'tone-ok', label: 'Approved' },
  Rejected: { tone: 'tone-danger', label: 'Rejected' },
  Cancelled: { tone: 'tone-muted', label: 'Withdrawn' },
};

@Component({
  selector: 'app-access-requests-page',
  imports: [ConfirmationDialog, EmptyState, StatusLine],
  templateUrl: './access-requests-page.html',
})
export class AccessRequestsPage {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly locale = inject(LOCALE_ID);
  private readonly confirmation = viewChild.required(ConfirmationDialog);
  private pending: { decision: 'reject' | 'withdraw'; requestId: string } | null = null;

  protected readonly requests = httpResource<AccessRequests>(() => '/api/ui/access-requests');
  protected readonly busy = signal(false);
  protected readonly status = signal({ message: '', error: false });
  protected readonly badges = badges;

  constructor() {
    const route = inject(ActivatedRoute);
    const query = route.snapshot.queryParamMap;
    const filed = query.get('filed');
    const refused = query.get('refused');
    if (filed === null && refused === null) return;

    if (filed === '1') {
      this.status.set({
        message: 'Access request submitted. A reviewer for this secret will decide.',
        error: false,
      });
    } else if (refused !== null && refusals.has(refused)) {
      this.status.set({ message: refusals.get(refused)!, error: true });
    }
    // The outcome is shown once: reloading or bookmarking the page must not show it again.
    void inject(Router).navigate([], {
      relativeTo: route,
      queryParams: { filed: null, refused: null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  // Formatted here so the cell holds only this text, as Legacy's did; a template expression this
  // long is wrapped onto its own lines, and the cell would gain the spaces around it.
  protected requested(request: RequestToReview | FiledRequest): string {
    return formatDate(request.requestedAtUtc, 'yyyy-MM-dd HH:mm', this.locale, 'UTC');
  }

  /** Legacy's decision cell: who decided and when, then the note when there is one. */
  protected decision(request: FiledRequest): string {
    if (request.decidedAtUtc === null) return '—';
    const on = formatDate(request.decidedAtUtc, 'yyyy-MM-dd', this.locale, 'UTC');
    const note = request.decisionNote ? ` — ${request.decisionNote}` : '';
    return `${request.decidedBy ?? ''} · ${on}${note}`;
  }

  protected approve(event: Event, request: RequestToReview, days: HTMLInputElement): void {
    event.preventDefault();
    // The browser refuses a fractional or out-of-range expiry before submitting, as it did for
    // Legacy's form. An empty one is sent as null, which the server refuses as out of range.
    const expiresInDays = Number.isInteger(days.valueAsNumber) ? days.valueAsNumber : null;
    this.send('approve', request.requestId, { expiresInDays });
  }

  protected askToReject(request: RequestToReview): void {
    this.ask(
      { decision: 'reject', requestId: request.requestId },
      `Reject ${request.requester}'s request for ${request.secretName}?`,
    );
  }

  protected askToWithdraw(request: FiledRequest): void {
    this.ask(
      { decision: 'withdraw', requestId: request.requestId },
      `Withdraw your request for ${request.secretName}?`,
    );
  }

  protected decide(confirmed: boolean): void {
    const pending = this.pending;
    this.pending = null;
    if (confirmed && pending !== null) this.send(pending.decision, pending.requestId, null);
  }

  private ask(pending: NonNullable<typeof this.pending>, message: string): void {
    if (this.busy()) return;
    this.pending = pending;
    this.confirmation().open(message);
  }

  private send(decision: Decision, requestId: string, body: object | null): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.status.set({ message: '', error: false });
    this.http
      .post<void>(`/api/ui/access-requests/${requestId}/${decision}`, body)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => this.decided({ message: decided[decision], error: false }),
        error: (error: unknown) =>
          this.decided({
            message:
              error instanceof UiApiError
                ? error.title
                : 'Could not update the request. Try again.',
            error: true,
          }),
      });
  }

  /** Legacy showed both lists afresh after every decision, whatever its outcome. */
  private decided(outcome: { message: string; error: boolean }): void {
    this.status.set(outcome);
    this.requests.reload();
  }
}
