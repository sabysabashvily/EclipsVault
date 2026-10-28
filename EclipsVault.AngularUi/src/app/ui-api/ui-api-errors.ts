import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { DOCUMENT, Injectable, inject } from '@angular/core';
import {
  NEVER,
  Observable,
  catchError,
  from,
  map,
  of,
  shareReplay,
  switchMap,
  throwError,
} from 'rxjs';

/** A failed UI API call, carrying the Problem Details `code` the server chose (ADR-0006). */
export class UiApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    readonly title: string,
    readonly signedOut: number | null = null,
  ) {
    super(title);
    this.name = 'UiApiError';
  }
}

/**
 * The vault's sign-in page, returning to the page the browser is on. The return URL is always a
 * local path: `//host` or `/\host` would be another origin to a browser, so those return to `/`.
 */
export function signInUrl(location: Pick<Location, 'pathname' | 'search'>): string {
  const here = location.pathname + location.search;
  const local = /^\/(?![/\\])/.test(here) ? here : '/';
  return `/Account/Login?ReturnUrl=${encodeURIComponent(local)}`;
}

function toUiApiError(response: HttpErrorResponse): UiApiError {
  const problem = response.error as { code?: unknown; title?: unknown; signedOut?: unknown } | null;
  return new UiApiError(
    response.status,
    typeof problem?.code === 'string' ? problem.code : 'unknown',
    typeof problem?.title === 'string'
      ? problem.title
      : 'The vault could not complete the request.',
    typeof problem?.signedOut === 'number' &&
      Number.isSafeInteger(problem.signedOut) &&
      problem.signedOut >= 0
      ? problem.signedOut
      : null,
  );
}

function parsedOrNull(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

/** A file download's failure arrives as a Blob; its Problem Details are read like any other's. */
function withReadableBody(response: HttpErrorResponse): Observable<HttpErrorResponse> {
  if (!(response.error instanceof Blob)) return of(response);
  return from(response.error.text()).pipe(
    map(
      (text) =>
        new HttpErrorResponse({
          error: parsedOrNull(text),
          headers: response.headers,
          status: response.status,
          statusText: response.statusText,
          url: response.url ?? undefined,
        }),
    ),
  );
}

/** Coalesces stale responses from the shell and page into one antiforgery-protected sign-out. */
@Injectable({ providedIn: 'root' })
class StaleSessionSignOut {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private ending: Observable<never> | undefined;

  end(): Observable<never> {
    return (this.ending ??= this.http.post('/Account/Logout', null, { responseType: 'text' }).pipe(
      switchMap(() => {
        this.document.location.assign('/Account/Login');
        return NEVER;
      }),
      catchError(() => {
        this.ending = undefined;
        return throwError(
          () =>
            new UiApiError(
              0,
              'sign_out_failed',
              'Could not sign out. Reload the page to try again.',
            ),
        );
      }),
      shareReplay({ bufferSize: 1, refCount: false }),
    ));
  }
}

/**
 * The one place the Angular UI reacts to UI API failures. A 401 (no session, or a revoked one)
 * sends the browser to sign-in; every other failure reaches the page as a {@link UiApiError}.
 */
export const uiApiErrors: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/ui/')) {
    return next(request);
  }

  const document = inject(DOCUMENT);
  const staleSession = inject(StaleSessionSignOut);
  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }
      return withReadableBody(error).pipe(
        switchMap((response) => {
          if (response.status === 401) {
            if (toUiApiError(response).code === 'stale_session') {
              return new Observable<never>((subscriber) => {
                // Start the shared sign-out before page disposal can unsubscribe this failed write.
                const signOut = staleSession.end().subscribe(subscriber);
                document.dispatchEvent(new Event('vault-session-ended'));
                return signOut;
              });
            }
            document.location.assign(signInUrl(document.location));
            document.dispatchEvent(new Event('vault-session-ended'));
            // The page is being left: nothing more for it to show.
            return NEVER;
          }
          return throwError(() => toUiApiError(response));
        }),
      );
    }),
  );
};
