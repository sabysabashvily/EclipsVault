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
import { UiApiError } from '../ui-api/ui-api-errors';

/** Page-scoped writes never replay; leaving or losing the session disposes their callbacks. */
@Injectable()
export class ProfileSession {
  readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly account = inject(CurrentAccount);
  readonly ended = new Subject<void>();
  readonly busy = signal(false);
  readonly blocked = signal(false);
  readonly status = signal({ message: '', error: false });
  readonly localSignOutAvailable = signal(false);
  readonly signInRequired = signal(false);
  private readonly clearers: (() => void)[] = [];

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

  onClear(clear: () => void): void {
    this.clearers.push(clear);
  }

  clear(): void {
    this.ended.next();
    this.clearers.forEach((clear) => clear());
  }

  refreshed(): void {
    this.account.me.reload();
  }

  write<T>(request: Observable<T>, saved: (value: T) => void): void {
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
        error: (error: unknown) => this.failed(error),
      });
  }

  failed(error: unknown): void {
    if (error instanceof UiApiError && error.code === 'changed_sign_in_required') {
      this.blocked.set(true);
      this.signInRequired.set(true);
      this.clear();
    }
    this.status.set({
      message:
        error instanceof UiApiError && error.status !== 0
          ? error.title
          : 'The response was lost. The change may have been saved. Check your account or sign in before deliberately trying again.',
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
    this.write(
      this.http.get<{ requestToken: string }>('/local-sign-out').pipe(
        switchMap((token) =>
          this.http
            .post('/local-sign-out', null, {
              headers: { RequestVerificationToken: token.requestToken },
              responseType: 'text',
            })
            .pipe(
              catchError((error: unknown) => {
                if (
                  error instanceof HttpErrorResponse &&
                  error.status === 503 &&
                  typeof error.error === 'string'
                ) {
                  try {
                    const result: unknown = JSON.parse(error.error);
                    if (
                      result &&
                      typeof result === 'object' &&
                      'code' in result &&
                      result.code === 'sign_out_incomplete'
                    )
                      return of('');
                  } catch {
                    /* A malformed reply cannot confirm local clearing. */
                  }
                }
                return throwError(() => error);
              }),
            ),
        ),
      ),
      () => this.localCookieCleared(),
    );
  }

  private localCookieCleared(): void {
    this.clear();
    this.document.location.assign('/Account/Login?signOut=sign_out_incomplete');
  }
}
