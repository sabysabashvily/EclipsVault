import { HttpClient, httpResource } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, type Observable } from 'rxjs';
import { ConfirmationDialog } from '../shared/confirmation-dialog';
import { EmptyState } from '../shared/empty-state';
import { relativeTime, utcTimestamp } from '../shared/relative-time';
import { StatusLine } from '../shared/status-line';
import type { paths } from '../ui-api/schema.generated';
import { UiApiError } from '../ui-api/ui-api-errors';

type Sessions = paths['/api/ui/sessions']['get']['responses'][200]['content']['application/json'];
type SignedOut =
  paths['/api/ui/sessions/sign-out-others']['post']['responses'][200]['content']['application/json'];
type SignOut = { sessionId: string } | 'others';

export function deviceKind(device: string): 'phone' | 'terminal' | 'monitor' {
  if (/iOS|Android/i.test(device)) return 'phone';
  if (/curl|python|PowerShell/i.test(device)) return 'terminal';
  return 'monitor';
}

function signedOutMessage(count: number): string {
  return count === 0
    ? 'There were no other sessions to sign out.'
    : `Signed out ${count} other session${count === 1 ? '' : 's'}.`;
}

@Component({
  selector: 'app-sessions-page',
  imports: [ConfirmationDialog, EmptyState, StatusLine],
  templateUrl: './sessions-page.html',
})
export class SessionsPage {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly confirmation = viewChild.required(ConfirmationDialog);
  private pending: SignOut | null = null;

  protected readonly sessions = httpResource<Sessions>(() => '/api/ui/sessions');
  protected readonly busy = signal(false);
  protected readonly status = signal({ message: '', error: false });
  protected readonly view = computed(() => {
    if (!this.sessions.hasValue()) return null;
    const now = new Date();
    return this.sessions.value().sessions.map((session) => ({
      ...session,
      kind: deviceKind(session.device),
      signedIn: relativeTime(session.createdAtUtc, now),
      lastActive: relativeTime(session.lastSeenAtUtc, now),
      lastActiveUtc: utcTimestamp(session.lastSeenAtUtc),
    }));
  });
  protected readonly hasOthers = computed(
    () => this.view()?.some((session) => !session.isCurrent) ?? false,
  );

  protected ask(action: SignOut): void {
    if (this.busy()) return;
    this.pending = action;
    this.confirmation().open(
      action === 'others'
        ? 'Sign out all other devices? Each loses access on its next request.'
        : 'Sign out this device? It loses access on its next request.',
    );
  }

  protected decide(confirmed: boolean): void {
    const action = this.pending;
    this.pending = null;
    if (!confirmed || action === null || this.busy()) return;

    this.busy.set(true);
    this.status.set({ message: '', error: false });
    const request: Observable<SignedOut | void> =
      action === 'others'
        ? this.http.post<SignedOut>('/api/ui/sessions/sign-out-others', null)
        : this.http.delete<void>(`/api/ui/sessions/${action.sessionId}`);
    request
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (result) => {
          this.status.set({
            message: result
              ? signedOutMessage(Number(result.signedOut))
              : 'That session was signed out — it loses access on its next request.',
            error: false,
          });
          this.sessions.reload();
        },
        error: (error: unknown) => this.failed(error, action),
      });
  }

  private failed(error: unknown, action: SignOut): void {
    if (error instanceof UiApiError && error.status === 404 && error.code === 'session_not_found') {
      // Missing metadata still leaves a revocation tombstone; refresh instead of keeping a stale device.
      this.sessions.reload();
      return;
    }

    const unrecorded = error instanceof UiApiError && error.code === 'revoked_unrecorded';
    const count =
      unrecorded && action === 'others' && error.signedOut !== null
        ? `${signedOutMessage(error.signedOut)} `
        : '';
    this.status.set({
      message:
        count +
        (error instanceof UiApiError ? error.title : 'Could not sign out the device. Try again.'),
      error: true,
    });
    if (unrecorded) this.sessions.reload();
  }
}
