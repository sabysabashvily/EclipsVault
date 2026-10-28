import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { CurrentAccount } from '../account/current-account';
import { UiApiError } from '../ui-api/ui-api-errors';
import { ProfileSession } from './profile-session';

describe('ProfileSession', () => {
  let backend: HttpTestingController;
  const assign = vi.fn();
  beforeEach(() => {
    assign.mockReset();
    TestBed.configureTestingModule({
      providers: [
        ...appConfig.providers,
        provideHttpClientTesting(),
        ProfileSession,
        { provide: CurrentAccount, useValue: { me: { reload: vi.fn() } } },
        {
          provide: DOCUMENT,
          useValue: {
            defaultView: window,
            addEventListener: document.addEventListener.bind(document),
            removeEventListener: document.removeEventListener.bind(document),
            location: { assign },
            cookie: '',
            dispatchEvent: document.dispatchEvent.bind(document),
          },
        },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
  });
  afterEach(() => backend.verify({ ignoreCancelled: true }));
  function session() {
    const session = TestBed.inject(ProfileSession);
    return session;
  }
  it('blocks further writes after a known committed change needs sign-in', () => {
    const state = session();
    const cleared = vi.fn();
    state.onClear(cleared);
    state.failed(
      new UiApiError(503, 'changed_sign_in_required', 'The change was saved. Sign in again.'),
    );
    state.write(state.http.post('/api/ui/profile/password', {}), vi.fn());
    expect(state.signInRequired()).toBe(true);
    expect(cleared).toHaveBeenCalledOnce();
    backend.expectNone('/api/ui/profile/password');
  });
  it('clears sensitive state on pagehide and back-forward restoration', () => {
    const state = session();
    const cleared = vi.fn();
    state.onClear(cleared);
    window.dispatchEvent(new Event('pagehide'));
    window.dispatchEvent(new Event('pageshow'));
    expect(cleared).toHaveBeenCalledTimes(2);
    expect(state.blocked()).toBe(true);
  });
  it.each(['revoked_unrecorded', 'sign_out_incomplete'])(
    'leaves for sign-in with the server warning after %s',
    (code) => {
      const state = session();
      state.signOutEverywhere();
      backend
        .expectOne('/api/ui/profile/sign-out-everywhere')
        .flush({ code, title: 'Sign-out warning.' }, { status: 503, statusText: 'Unavailable' });
      expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login');
    },
  );
  it('offers local clearing after a store failure without claiming account-wide success', () => {
    const state = session();
    state.signOutEverywhere();
    backend
      .expectOne('/api/ui/profile/sign-out-everywhere')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    expect(assign).not.toHaveBeenCalled();
    expect(state.localSignOutAvailable()).toBe(true);
  });
  it('gets an anonymous token and preserves the incomplete warning after local clearing', () => {
    const state = session();
    state.clearLocalCookie();
    backend.expectOne('/local-sign-out').flush({ requestToken: 'anonymous-token' });
    const clear = backend.expectOne({ method: 'POST', url: '/local-sign-out' });
    expect(clear.request.headers.get('RequestVerificationToken')).toBe('anonymous-token');
    clear.flush('{"code":"sign_out_incomplete"}', { status: 503, statusText: 'Unavailable' });
    expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login?signOut=sign_out_incomplete');
  });
  it('does not claim the cookie cleared when the fallback itself fails', () => {
    const state = session();
    state.clearLocalCookie();
    backend.expectOne('/local-sign-out').flush({ requestToken: 'anonymous-token' });
    backend
      .expectOne({ method: 'POST', url: '/local-sign-out' })
      .flush('Failure', { status: 500, statusText: 'Error' });
    expect(assign).not.toHaveBeenCalled();
    expect(state.status().error).toBe(true);
  });
  it('can clear the local cookie after the account-wide sign-out reply is lost', () => {
    const state = session();
    state.signOutEverywhere();
    backend.expectOne('/api/ui/profile/sign-out-everywhere').error(new ProgressEvent('error'));
    expect(state.blocked()).toBe(true);
    state.clearLocalCookie();
    backend.expectOne('/local-sign-out').flush({ requestToken: 'anonymous-token' });
    backend
      .expectOne({ method: 'POST', url: '/local-sign-out' })
      .flush('{"code":"sign_out_incomplete"}', { status: 503, statusText: 'Unavailable' });
    expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login?signOut=sign_out_incomplete');
  });
  it('can retry local-cookie clearing after its own reply is lost', () => {
    const state = session();
    state.clearLocalCookie();
    backend.expectOne('/local-sign-out').error(new ProgressEvent('error'));
    state.clearLocalCookie();
    backend.expectOne('/local-sign-out').flush({ requestToken: 'retry-token' });
    backend
      .expectOne({ method: 'POST', url: '/local-sign-out' })
      .flush('{"code":"sign_out_incomplete"}', { status: 503, statusText: 'Unavailable' });
    expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login?signOut=sign_out_incomplete');
  });
  it('starts shared logout even when session loss disposes the Profile write', () => {
    const state = session();
    const cleared = vi.fn();
    state.onClear(cleared);
    state.write(state.http.patch('/api/ui/profile/details', { displayName: 'Renamed' }), vi.fn());
    backend
      .expectOne('/api/ui/profile/details')
      .flush(
        { code: 'stale_session', title: 'Sign out and sign in again.' },
        { status: 401, statusText: 'Unauthorized' },
      );
    const logout = backend.expectOne('/Account/Logout');
    expect(logout.cancelled).toBe(false);
    expect(cleared).toHaveBeenCalledOnce();
    logout.flush('');
    expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login');
  });
});
