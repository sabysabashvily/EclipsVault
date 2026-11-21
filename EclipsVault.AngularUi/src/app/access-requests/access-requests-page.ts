import { formatDate } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import {
  afterNextRender,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  LOCALE_ID,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { ConfirmationDialog, type DecisionOptions } from '../shared/confirmation-dialog';
import { EmptyState } from '../shared/empty-state';
import { pillDate } from '../shared/expiry';
import { relativeTime, utcTimestamp } from '../shared/relative-time';
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

const decisionAnnouncements: Record<Decision, string> = {
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
  imports: [ConfirmationDialog, EmptyState, RouterLink, StatusLine],
  templateUrl: './access-requests-page.html',
})
export class AccessRequestsPage {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly locale = inject(LOCALE_ID);
  private readonly confirmation = viewChild.required('confirmation', { read: ConfirmationDialog });
  private readonly approval = viewChild.required('approval', { read: ConfirmationDialog });
  private readonly days = viewChild.required<ElementRef<HTMLInputElement>>('days');
  private approving: RequestToReview | null = null;
  private awaitingConfirmation: { decision: 'reject' | 'withdraw'; requestId: string } | null =
    null;

  protected readonly requests = httpResource<AccessRequests>(() => '/api/ui/access-requests');
  protected readonly busy = signal(false);
  protected readonly status = signal({ message: '', error: false });
  protected readonly badges = badges;
  /** The approve dialog's expiry field as typed, for its "Sent with the share" line. */
  protected readonly endsAfter = signal('0');

  protected readonly pending = computed(() =>
    this.requests.hasValue() ? this.requests.value().toReview.length : 0,
  );

  protected readonly context = computed(() => {
    const pending = this.pending();
    if (pending === 0)
      return 'Nothing waits on you · requests you file from a denied secret appear under My requests.';
    const wait = pending === 1 ? '1 request waits' : `${pending} requests wait`;
    return `${wait} on you · approving shares the secret; you choose when access ends.`;
  });

  protected readonly ends = computed(() => {
    const days = this.endsAfter();
    if (days === '0') return 'No end date';
    return /^[1-9]\d*$/.test(days) ? `Ends after ${days} ${days === '1' ? 'day' : 'days'}` : '';
  });

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
  protected requestedAt(request: RequestToReview | FiledRequest): string {
    return formatDate(request.requestedAtUtc, 'yyyy-MM-dd HH:mm', this.locale, 'UTC');
  }

  /** Legacy's decision cell: who decided and when, then the note when there is one. */
  protected decision(request: FiledRequest): string {
    if (request.decidedAtUtc === null) return '—';
    const on = formatDate(request.decidedAtUtc, 'yyyy-MM-dd', this.locale, 'UTC');
    const note = request.decisionNote ? ` — ${request.decisionNote}` : '';
    return `${request.decidedBy ?? ''} · ${on}${note}`;
  }

  /** How long a request has waited: "3h ago" within the week, then the day it was filed. */
  protected waiting(request: RequestToReview): { text: string; title: string } {
    const now = new Date();
    const week = 7 * 24 * 3_600_000;
    const text =
      now.getTime() - Date.parse(request.requestedAtUtc) < week
        ? relativeTime(request.requestedAtUtc, now)
        : `Since ${pillDate(request.requestedAtUtc, now)}`;
    return { text, title: utcTimestamp(request.requestedAtUtc) };
  }

  protected askToApprove(request: RequestToReview): void {
    if (this.busy()) return;
    this.days().nativeElement.value = '0';
    this.endsAfter.set('0');
    this.showApproval(request);
  }

  private showApproval(request: RequestToReview): void {
    this.approving = request;
    this.approval().open(`${request.requester} gets ${request.secretName} as a share.`, {
      title: `Approve ${request.requester}'s request?`,
      confirm: 'Approve and grant access',
      tone: 'primary',
      facts: [
        ['Secret', request.secretName],
        ['Project', request.project],
        ['Requester', request.requester],
        ['Reason', request.reason],
      ],
      note: 'Saved to the audit log',
    });
  }

  protected decideApproval(confirmed: boolean): void {
    const request = this.approving;
    this.approving = null;
    if (!confirmed || request === null) return;
    const days = this.days().nativeElement;
    // The field's own rules (whole days, 0 to 3650) refuse before anything is sent, as Legacy's
    // form did: the approval stays open with what was typed, and the field says what is wrong.
    // An empty one is sent as null, which the server refuses as out of range.
    if (!days.checkValidity()) {
      this.showApproval(request);
      afterNextRender(() => days.reportValidity(), { injector: this.injector });
      return;
    }
    const expiresInDays = Number.isInteger(days.valueAsNumber) ? days.valueAsNumber : null;
    this.send('approve', request.requestId, { expiresInDays });
  }

  protected askToReject(request: RequestToReview): void {
    this.ask(
      { decision: 'reject', requestId: request.requestId },
      `Reject ${request.requester}'s request for ${request.secretName}?`,
      { title: 'Reject this request?', confirm: 'Reject request', note: 'Saved to the audit log' },
    );
  }

  protected askToWithdraw(request: FiledRequest): void {
    this.ask(
      { decision: 'withdraw', requestId: request.requestId },
      `Withdraw your request for ${request.secretName}?`,
      { confirm: 'Withdraw request' },
    );
  }

  protected decide(confirmed: boolean): void {
    const awaiting = this.awaitingConfirmation;
    this.awaitingConfirmation = null;
    if (confirmed && awaiting !== null) this.send(awaiting.decision, awaiting.requestId, null);
  }

  private ask(
    awaiting: NonNullable<typeof this.awaitingConfirmation>,
    message: string,
    options: DecisionOptions,
  ): void {
    if (this.busy()) return;
    this.awaitingConfirmation = awaiting;
    this.confirmation().open(message, options);
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
        next: () => this.decided({ message: decisionAnnouncements[decision], error: false }),
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
