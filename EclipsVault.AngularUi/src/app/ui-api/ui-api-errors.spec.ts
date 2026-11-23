import {
  HttpClient,
  HttpErrorResponse,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  loginUrl,
  returnUrlOf,
  SessionSignOut,
  signInApiErrors,
  signInUrl,
  UiApiError,
  uiApiErrors,
} from './ui-api-errors';

describe('uiApiErrors', () => {
  const assign = vi.fn();
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    assign.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([uiApiErrors])),
        provideHttpClientTesting(),
        {
          provide: DOCUMENT,
          useValue: {
            dispatchEvent: vi.fn(),
            location: { pathname: '/Access', search: '?tab=grid', assign },
          },
        },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('sends a 401 to sign-in, returning to this page, and gives the page nothing', () => {
    const next = vi.fn();
    const error = vi.fn();
    http.get('/api/ui/me').subscribe({ next, error });

    backend
      .expectOne('/api/ui/me')
      .flush(
        { status: 401, code: 'unauthenticated', title: 'Sign in to continue.' },
        { status: 401, statusText: 'Unauthorized' },
      );

    expect(assign).toHaveBeenCalledExactlyOnceWith(
      '/Account/Login?ReturnUrl=%2FAccess%3Ftab%3Dgrid',
    );
    expect(next).not.toHaveBeenCalled();
    expect(error).not.toHaveBeenCalled();
  });

  it('ends a stale session through sign-out before leaving for sign-in, once for concurrent failures', () => {
    const next = vi.fn();
    const error = vi.fn();
    http.get('/api/ui/me').subscribe({ next, error });
    http.get('/api/ui/access').subscribe({ next, error });
    backend
      .expectOne('/api/ui/me')
      .flush(
        { status: 401, code: 'stale_session', title: 'Sign out and sign in again.' },
        { status: 401, statusText: 'Unauthorized' },
      );
    backend
      .expectOne('/api/ui/access')
      .flush(
        { status: 401, code: 'stale_session', title: 'Sign out and sign in again.' },
        { status: 401, statusText: 'Unauthorized' },
      );

    expect(assign).not.toHaveBeenCalled();
    const logout = backend.expectOne('/api/ui/auth/sign-out');
    expect(logout.request.method).toBe('POST');
    logout.flush('');
    expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login');
    expect(next).not.toHaveBeenCalled();
    expect(error).not.toHaveBeenCalled();
  });

  it('surfaces a failed stale-session sign-out without pretending the session ended, and permits retry', () => {
    const error = vi.fn();
    http.get('/api/ui/access').subscribe({ error });
    backend
      .expectOne('/api/ui/access')
      .flush({ code: 'stale_session' }, { status: 401, statusText: 'Unauthorized' });
    backend
      .expectOne('/api/ui/auth/sign-out')
      .flush('', { status: 503, statusText: 'Unavailable' });
    expect(assign).not.toHaveBeenCalled();
    expect(error).toHaveBeenCalledWith(expect.objectContaining({ code: 'sign_out_failed' }));

    http.get('/api/ui/access').subscribe();
    backend
      .expectOne('/api/ui/access')
      .flush({ code: 'stale_session' }, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/ui/auth/sign-out').flush('');
    expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login');
  });

  it.each([
    [503, 'audit_unavailable'],
    [404, 'not_found'],
    [403, 'forbidden'],
  ])('surfaces a %i problem to the page as its code %s', (status, code) => {
    let surfaced: unknown;
    http.get('/api/ui/me').subscribe({ error: (e: unknown) => (surfaced = e) });

    backend
      .expectOne('/api/ui/me')
      .flush({ status, code, title: 'Refused.' }, { status, statusText: 'Refused' });

    expect(surfaced).toBeInstanceOf(UiApiError);
    expect(surfaced).toMatchObject({ status, code, title: 'Refused.' });
    expect(assign).not.toHaveBeenCalled();
  });

  it.each([
    ['code', 'code'],
    ['sign_in', 'sign_in'],
    ['unrecognized', null],
    [17, null],
    [null, null],
  ])('passes only a recognized step-up proof to the page: %s', (proof, expected) => {
    let surfaced: unknown;
    http
      .post('/api/ui/users/target/enable', null)
      .subscribe({ error: (error: unknown) => (surfaced = error) });
    backend
      .expectOne('/api/ui/users/target/enable')
      .flush(
        { code: 'step_up_required', title: "Confirm it's you.", proof },
        { status: 403, statusText: 'Forbidden' },
      );
    expect(surfaced).toMatchObject({ status: 403, code: 'step_up_required', proof: expected });
  });

  it('reads the Problem Details of a failed file download, whose body arrives as a Blob', async () => {
    const surfaced = new Promise((resolve) =>
      http
        .post('/api/ui/data/export', null, { responseType: 'blob' })
        .subscribe({ error: resolve }),
    );
    const problem = { status: 503, code: 'audit_unavailable', title: 'Refused.' };

    backend
      .expectOne('/api/ui/data/export')
      .flush(new Blob([JSON.stringify(problem)], { type: 'application/problem+json' }), {
        status: 503,
        statusText: 'Unavailable',
      });

    expect(await surfaced).toMatchObject({
      status: 503,
      code: 'audit_unavailable',
      title: 'Refused.',
    });
  });

  it('ends a stale session reported by a failed file download', async () => {
    http.post('/api/ui/data/export', null, { responseType: 'blob' }).subscribe();

    backend
      .expectOne('/api/ui/data/export')
      .flush(new Blob([JSON.stringify({ status: 401, code: 'stale_session', title: 'Stale.' })]), {
        status: 401,
        statusText: 'Unauthorized',
      });

    const logout = await vi.waitFor(() => backend.expectOne('/api/ui/auth/sign-out'));
    logout.flush('');
    expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login');
  });

  it('surfaces a failure without Problem Details as an unknown code', () => {
    let surfaced: unknown;
    http.get('/api/ui/me').subscribe({ error: (e: unknown) => (surfaced = e) });

    backend
      .expectOne('/api/ui/me')
      .flush('Bad gateway', { status: 502, statusText: 'Bad Gateway' });

    expect(surfaced).toMatchObject({ status: 502, code: 'unknown' });
  });

  it('leaves requests outside the UI API alone', () => {
    let surfaced: unknown;
    http.get('/Account/Login').subscribe({ error: (e: unknown) => (surfaced = e) });

    backend.expectOne('/Account/Login').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(surfaced).toBeInstanceOf(HttpErrorResponse);
    expect(assign).not.toHaveBeenCalled();
  });
});

describe('SessionSignOut', () => {
  const assign = vi.fn();
  let signOut: SessionSignOut;
  let backend: HttpTestingController;

  beforeEach(() => {
    assign.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([uiApiErrors])),
        provideHttpClientTesting(),
        {
          provide: DOCUMENT,
          useValue: {
            dispatchEvent: vi.fn(),
            location: { pathname: '/Profile', search: '', assign },
          },
        },
      ],
    });
    signOut = TestBed.inject(SessionSignOut);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('posts the UI API sign-out once for every caller, and a caller that leaves does not cancel it', () => {
    const left = signOut.end().subscribe();
    signOut.end().subscribe();
    left.unsubscribe();

    const request = backend.expectOne({ method: 'POST', url: '/api/ui/auth/sign-out' });
    expect(request.cancelled).toBe(false);
    request.flush(null, { status: 204, statusText: 'No Content' });

    expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login');
  });

  it('keeps the first caller’s return URL and adds the local-clear warning when revocation was not confirmed', () => {
    const error = vi.fn();
    signOut.end('/Account/Login?ReturnUrl=%2FAdmin%2FUsers').subscribe({ error });

    backend
      .expectOne('/api/ui/auth/sign-out')
      .flush(
        { status: 503, code: 'sign_out_incomplete', title: 'Cookie cleared.' },
        { status: 503, statusText: 'Service Unavailable' },
      );

    expect(assign).toHaveBeenCalledExactlyOnceWith(
      '/Account/Login?ReturnUrl=%2FAdmin%2FUsers&signOut=sign_out_incomplete',
    );
    expect(error).not.toHaveBeenCalled();
  });

  it.each([
    ['a lost reply', (request: TestRequest) => request.error(new ProgressEvent('error'))],
    [
      'any other refusal',
      (request: TestRequest) =>
        request.flush(
          { status: 503, code: 'audit_unavailable', title: 'Audit down.' },
          { status: 503, statusText: 'Service Unavailable' },
        ),
    ],
  ])('stays on the page with sign_out_failed after %s, and permits retry', (_, fail) => {
    const error = vi.fn();
    signOut.end().subscribe({ error });
    fail(backend.expectOne('/api/ui/auth/sign-out'));

    expect(assign).not.toHaveBeenCalled();
    expect(error).toHaveBeenCalledWith(
      expect.objectContaining({
        code: 'sign_out_failed',
        title: 'Could not sign out. Reload the page to try again.',
      }),
    );

    signOut.end().subscribe();
    backend
      .expectOne('/api/ui/auth/sign-out')
      .flush(null, { status: 204, statusText: 'No Content' });
    expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login');
  });
});

describe('signInApiErrors', () => {
  const assign = vi.fn();
  let http: HttpClient;
  let backend: HttpTestingController;

  beforeEach(() => {
    assign.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([signInApiErrors])),
        provideHttpClientTesting(),
        {
          provide: DOCUMENT,
          useValue: { location: { search: '?returnurl=%2FSecrets%3Fq%3D1', assign } },
        },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('starts an expired sign-in again at Login with a full page load, keeping its return URL', () => {
    const next = vi.fn();
    const error = vi.fn();
    http.post('/api/ui/auth/totp', { code: '123456' }).subscribe({ next, error });

    backend
      .expectOne('/api/ui/auth/totp')
      .flush(
        { status: 401, code: 'sign_in_expired', title: 'Your sign-in expired. Sign in again.' },
        { status: 401, statusText: 'Unauthorized' },
      );

    expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login?ReturnUrl=%2FSecrets%3Fq%3D1');
    expect(next).not.toHaveBeenCalled();
    expect(error).not.toHaveBeenCalled();
  });

  it('gives the page every other refusal as its code and sentence', () => {
    let surfaced: unknown;
    http.post('/api/ui/auth/password', {}).subscribe({ error: (e: unknown) => (surfaced = e) });

    backend
      .expectOne('/api/ui/auth/password')
      .flush(
        { status: 400, code: 'sign_in_failed', title: 'Invalid username or password.' },
        { status: 400, statusText: 'Bad Request' },
      );

    expect(surfaced).toEqual(
      expect.objectContaining({ code: 'sign_in_failed', title: 'Invalid username or password.' }),
    );
    expect(assign).not.toHaveBeenCalled();
  });
});

describe('loginUrl and returnUrlOf', () => {
  it('carries a return URL on to Login, and none when there is none', () => {
    expect(loginUrl('/Secrets?q=1')).toBe('/Account/Login?ReturnUrl=%2FSecrets%3Fq%3D1');
    expect(loginUrl(null)).toBe('/Account/Login');
  });

  it('reads the return URL whatever its case, as the vault binds it', () => {
    expect(returnUrlOf('?ReturnUrl=%2FA')).toBe('/A');
    expect(returnUrlOf('?returnUrl=%2FB&x=1')).toBe('/B');
    expect(returnUrlOf('?x=1')).toBeNull();
  });
});

describe('signInUrl', () => {
  it.each([
    ['/Access', '', '/Account/Login?ReturnUrl=%2FAccess'],
    ['/Access/Index', '?a=1&b=2', '/Account/Login?ReturnUrl=%2FAccess%2FIndex%3Fa%3D1%26b%3D2'],
    ['//evil.example/Access', '', '/Account/Login?ReturnUrl=%2F'],
    ['/\\evil.example', '', '/Account/Login?ReturnUrl=%2F'],
  ])('returns to %s%s only as a local path', (pathname, search, expected) => {
    expect(signInUrl({ pathname, search })).toBe(expected);
  });
});
