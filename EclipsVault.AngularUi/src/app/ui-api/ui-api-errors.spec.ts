import {
  HttpClient,
  HttpErrorResponse,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { signInUrl, UiApiError, uiApiErrors } from './ui-api-errors';

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
          useValue: { location: { pathname: '/Access', search: '?tab=grid', assign } },
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
    for (const path of ['/api/ui/me', '/api/ui/access']) {
      backend
        .expectOne(path)
        .flush(
          { status: 401, code: 'stale_session', title: 'Sign out and sign in again.' },
          { status: 401, statusText: 'Unauthorized' },
        );
    }

    expect(assign).not.toHaveBeenCalled();
    const logout = backend.expectOne('/Account/Logout');
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
    backend.expectOne('/Account/Logout').flush('', { status: 503, statusText: 'Unavailable' });
    expect(assign).not.toHaveBeenCalled();
    expect(error).toHaveBeenCalledWith(expect.objectContaining({ code: 'sign_out_failed' }));

    http.get('/api/ui/access').subscribe();
    backend
      .expectOne('/api/ui/access')
      .flush({ code: 'stale_session' }, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/Account/Logout').flush('');
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

    const logout = await vi.waitFor(() => backend.expectOne('/Account/Logout'));
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
