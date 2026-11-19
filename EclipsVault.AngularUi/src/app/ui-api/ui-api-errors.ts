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
import { PAGE_LOCATION } from '../shell/command-palette';
import type { components } from './schema.generated';

export type StepUpProof = NonNullable<
  components['schemas']['AdministratorProblemResponse']['proof']
>;

/** A failed UI API call, carrying the Problem Details `code` the server chose (ADR-0006). */
export class UiApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    readonly title: string,
    readonly signedOut: number | null = null,
    readonly proof: StepUpProof | null = null,
    readonly errors: Readonly<Record<string, string[]>> = {},
    readonly reasons: readonly string[] = [],
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
  const body: unknown = response.error;
  const problem = typeof body === 'object' && body !== null ? body : {};
  return new UiApiError(
    response.status,
    'code' in problem && typeof problem.code === 'string' ? problem.code : 'unknown',
    'title' in problem && typeof problem.title === 'string'
      ? problem.title
      : 'The vault could not complete the request.',
    'signedOut' in problem &&
      typeof problem.signedOut === 'number' &&
      Number.isSafeInteger(problem.signedOut) &&
      problem.signedOut >= 0
      ? problem.signedOut
      : null,
    'proof' in problem && (problem.proof === 'code' || problem.proof === 'sign_in')
      ? problem.proof
      : null,
    fieldErrors(problem),
    'reasons' in problem &&
      Array.isArray(problem.reasons) &&
      problem.reasons.every((reason: unknown) => typeof reason === 'string')
      ? (problem.reasons as string[])
      : [],
  );
}

function fieldErrors(problem: object): Readonly<Record<string, string[]>> {
  if (!('errors' in problem) || typeof problem.errors !== 'object' || problem.errors === null)
    return {};
  const errors: Record<string, string[]> = {};
  for (const [field, messages] of Object.entries(problem.errors)) {
    if (
      Array.isArray(messages) &&
      messages.every((message: unknown) => typeof message === 'string')
    )
      errors[field] = messages;
  }
  return errors;
}

export function parsedOrNull(text: string): unknown {
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

/**
 * The one sign-out of this browser's session: the account menu, the step-up dialog and stale
 * responses from the shell and page share a single antiforgery-protected call, which a disposed
 * caller does not cancel. A revocation the vault could not confirm still cleared the cookie, so it
 * leaves for sign-in with the local-clear warning.
 */
@Injectable({ providedIn: 'root' })
export class SessionSignOut {
  private readonly http = inject(HttpClient);
  private readonly location = inject(PAGE_LOCATION);
  private ending: Observable<never> | undefined;

  /** @param signIn Where sign-in starts, possibly with a return URL; the first caller's wins. */
  end(signIn = '/Account/Login'): Observable<never> {
    return (this.ending ??= this.http.post('/api/ui/auth/sign-out', null).pipe(
      map(() => signIn),
      catchError((error: unknown) =>
        error instanceof UiApiError && error.code === 'sign_out_incomplete'
          ? of(withQuery(signIn, 'signOut', 'sign_out_incomplete'))
          : throwError(() => error),
      ),
      switchMap((target) => {
        this.location.assign(target);
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

function withQuery(path: string, name: string, value: string): string {
  return `${path}${path.includes('?') ? '&' : '?'}${name}=${encodeURIComponent(value)}`;
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
  const staleSession = inject(SessionSignOut);
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

/** Sign-in, keeping the return URL the page was given; the vault judges it when sign-in finishes. */
export function loginUrl(returnUrl: string | null): string {
  return returnUrl ? `/Account/Login?ReturnUrl=${encodeURIComponent(returnUrl)}` : '/Account/Login';
}

/** The return URL in a sign-in page's query, matched without regard to case as the vault binds it. */
export function returnUrlOf(search: string): string | null {
  for (const [name, value] of new URLSearchParams(search))
    if (name.toLowerCase() === 'returnurl') return value;
  return null;
}

/**
 * The sign-in pages' counterpart of {@link uiApiErrors}. A pending sign-in that expired starts again
 * at Login with a full page load, which issues the anonymous request token its next call needs;
 * every other failure reaches the page as a {@link UiApiError}.
 */
export const signInApiErrors: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/ui/')) {
    return next(request);
  }

  const document = inject(DOCUMENT);
  const location = inject(PAGE_LOCATION);
  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }
      if (error.status === 401) {
        location.assign(loginUrl(returnUrlOf(document.location.search)));
        return NEVER;
      }
      return throwError(() => toUiApiError(error));
    }),
  );
};
