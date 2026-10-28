import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, DOCUMENT, Injectable, inject, signal } from '@angular/core';
import {
  Observable,
  Subject,
  catchError,
  finalize,
  of,
  takeUntil,
  switchMap,
  throwError,
} from 'rxjs';
import { CurrentAccount } from '../account/current-account';
import { UiApiError, parsedOrNull } from '../ui-api/ui-api-errors';

/** Page-scoped writes never replay; leaving or losing the session disposes their callbacks. */
@Injectable()
export class ProfileWrites {
  readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly account = inject(CurrentAccount);
  readonly ended = new Subject<void>();
  readonly busy = signal(false);
  readonly blocked = signal(false);
  readonly status = signal({ message: '', error: false });
  readonly localSignOutAvailable = signal(false);
  readonly signInRequired = signal(false);
  private readonly clearers = new Set<() => void>();

  constructor() {
    const clear = () => this.clear();
    const lost = () => {
      this.blocked.set(true);
      this.clear();
      this.status.set({
        message:
          'This page was left or its session ended. Reload the page or sign in before making another change.',
        error: true,
      });
    };
    this.document.defaultView?.addEventListener('pagehide', lost);
    this.document.defaultView?.addEventListener('pageshow', clear);
    this.document.addEventListener('vault-session-ended', lost);
    inject(DestroyRef).onDestroy(() => {
      this.document.defaultView?.removeEventListener('pagehide', lost);
      this.document.defaultView?.removeEventListener('pageshow', clear);
      this.document.removeEventListener('vault-session-ended', lost);
      this.clear();
      this.ended.complete();
    });
  }

  onClear(clear: () => void, owner?: DestroyRef): void {
    this.clearers.add(clear);
    owner?.onDestroy(() => {
      clear();
      this.clearers.delete(clear);
    });
  }

  clear(): void {
    this.ended.next();
    this.clearers.forEach((clear) => clear());
  }

  refreshed(): void {
    this.account.me.reload();
  }

  /** @param lostReply What to say when no reply arrived, so the write's outcome is unknown. */
  write<T>(
    request: Observable<T>,
    saved: (value: T) => void,
    onFailure?: () => void,
    lostReply?: string,
  ): void {
    if (this.busy() || this.blocked()) return;
    this.busy.set(true);
    this.status.set({ message: '', error: false });
    request
      .pipe(
        takeUntil(this.ended),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: saved,
        error: (error: unknown) => {
          this.failed(error, lostReply);
          if (!this.blocked()) onFailure?.();
        },
      });
  }

  failed(
    error: unknown,
    lostReply = 'The response was lost. The change may have been saved. Check your account or sign in before deliberately trying again.',
  ): void {
    const unknownOutcome =
      !(error instanceof UiApiError) ||
      error.status === 0 ||
      error.code === 'unknown' ||
      (error.status >= 500 &&
        error.code !== 'audit_unavailable' &&
        error.code !== 'session_store_unavailable' &&
        error.code !== 'changed_sign_in_required');
    if (unknownOutcome || error.code === 'changed_sign_in_required') {
      this.blocked.set(true);
      this.signInRequired.set(true);
      this.clear();
    }
    this.status.set({
      message: unknownOutcome ? lostReply : error.title,
      error: true,
    });
  }

  signOutEverywhere(): void {
    this.localSignOutAvailable.set(true);
    this.write(
      this.http
        .post<void>('/api/ui/profile/sign-out-everywhere', null)
        .pipe(
          catchError((error: unknown) =>
            error instanceof UiApiError &&
            (error.code === 'revoked_unrecorded' || error.code === 'sign_out_incomplete')
              ? of(undefined)
              : throwError(() => error),
          ),
        ),
      () => {
        this.clear();
        this.document.location.assign('/Account/Login');
      },
    );
  }

  /** Uses an anonymous token because an authenticated token cannot pass the outage-only branch. */
  clearLocalCookie(): void {
    if (this.busy()) return;
    this.busy.set(true);
    // Local recovery must remain usable even when an uncertain credential write blocked this page.
    this.http
      .get<{ requestToken: string }>('/local-sign-out')
      .pipe(
        switchMap((token) =>
          this.http
            .post('/local-sign-out', null, {
              headers: { RequestVerificationToken: token.requestToken },
              responseType: 'text',
            })
            .pipe(
              catchError((error: unknown) =>
                isSignOutIncomplete(error) ? of('') : throwError(() => error),
              ),
            ),
        ),
        takeUntil(this.ended),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => this.localCookieCleared(),
        error: (error: unknown) => this.failed(error),
      });
  }

  private localCookieCleared(): void {
    this.clear();
    this.document.location.assign('/Account/Login?signOut=sign_out_incomplete');
  }
}

/** The outage branch's 503 confirms the local cookie was cleared; a malformed reply cannot. */
function isSignOutIncomplete(error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse) || error.status !== 503) return false;
  if (typeof error.error !== 'string') return false;
  const result = parsedOrNull(error.error);
  return (
    !!result &&
    typeof result === 'object' &&
    'code' in result &&
    result.code === 'sign_out_incomplete'
  );
}
