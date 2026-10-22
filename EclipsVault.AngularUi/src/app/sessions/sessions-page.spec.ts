import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import type { components } from '../ui-api/schema.generated';
import { deviceKind, SessionsPage } from './sessions-page';

const current: components['schemas']['SessionResponse'] = {
  sessionId: '11111111-1111-1111-1111-111111111111',
  device: 'Chrome on macOS',
  ipAddress: '198.51.100.1',
  createdAtUtc: '2026-10-20T10:00:00Z',
  lastSeenAtUtc: '2026-10-20T11:00:00Z',
  isCurrent: true,
};
const other: components['schemas']['SessionResponse'] = {
  sessionId: '22222222-2222-2222-2222-222222222222',
  device: 'Safari on iOS',
  ipAddress: '198.51.100.2',
  createdAtUtc: '2026-10-20T09:00:00Z',
  lastSeenAtUtc: '2026-10-20T10:00:00Z',
  isCurrent: false,
};
const unrecorded =
  "The device was signed out, but the vault couldn't record it in the audit trail. Tell your administrator.";

describe('SessionsPage', () => {
  let backend: HttpTestingController;

  function render() {
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(SessionsPage);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    const dialog = page.querySelector('dialog')!;
    dialog.showModal = () => dialog.setAttribute('open', '');
    dialog.close = () => dialog.removeAttribute('open');
    return { fixture, page };
  }

  function button(page: HTMLElement, text: string): HTMLButtonElement {
    return [...page.querySelectorAll('button')].find(
      (candidate) => candidate.textContent?.trim() === text,
    )!;
  }

  afterEach(() => backend.verify());

  it('shows loading until the server answers', async () => {
    const { page } = render();
    expect(page.textContent).toContain('Loading your signed-in devices…');
    expect(page.querySelector('li')).toBeNull();
    backend.expectOne('/api/ui/sessions').flush({ sessions: [] });
  });

  it('offers a retry after a failed fetch and shows the refreshed empty state', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/sessions').flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(page.textContent).toContain('Could not load your signed-in devices. Try again.');
    button(page, 'Try again').click();
    fixture.detectChanges();
    backend.expectOne('/api/ui/sessions').flush({ sessions: [] });
    await fixture.whenStable();

    expect(page.textContent).toContain('No recorded sessions yet');
    expect(page.textContent).toContain('Your active sign-ins will appear here.');
    expect(page.textContent).not.toContain('Could not load');
  });

  it('shows the devices, marks this one and gives only the other device a sign-out button', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/sessions').flush({ sessions: [current, other] });
    await fixture.whenStable();
    const rows = page.querySelectorAll('li');

    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('Chrome on macOS');
    expect(rows[0].textContent).toContain('This device');
    expect(rows[0].textContent).toContain('Current');
    expect(rows[0].querySelector('button')).toBeNull();
    expect(rows[1].textContent).toContain('Safari on iOS');
    expect(rows[1].textContent).toContain('198.51.100.2');
    expect(rows[1].querySelector('[title]')?.getAttribute('title')).toBe('2026-10-20 10:00:00 UTC');
    expect(button(page, 'Sign out')).toBeDefined();
    expect(button(page, 'Sign out all other sessions')).toBeDefined();
    expect(page.querySelector('a')?.getAttribute('href')).toBe('/Profile');
  });

  it('Cancel sends no request and keeps the device in the list', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/sessions').flush({ sessions: [current, other] });
    await fixture.whenStable();
    button(page, 'Sign out').click();
    await fixture.whenStable();
    button(page, 'Cancel').click();
    await fixture.whenStable();

    backend.expectNone({ method: 'DELETE', url: `/api/ui/sessions/${other.sessionId}` });
    expect(page.querySelectorAll('li')).toHaveLength(2);
  });

  it('confirms a sign-out, sends the antiforgery header and announces success before refreshing', async () => {
    const { fixture, page } = render();
    document.cookie = 'XSRF-TOKEN=sessions-request-token; path=/';
    backend.expectOne('/api/ui/sessions').flush({ sessions: [current, other] });
    await fixture.whenStable();
    button(page, 'Sign out').click();
    await fixture.whenStable();
    button(page, 'Continue').click();
    fixture.detectChanges();
    const revoke = backend.expectOne({
      method: 'DELETE',
      url: `/api/ui/sessions/${other.sessionId}`,
    });
    expect(revoke.request.headers.get('RequestVerificationToken')).toBe('sessions-request-token');
    expect(button(page, 'Sign out').disabled).toBe(true);
    revoke.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    backend.expectOne('/api/ui/sessions').flush({ sessions: [current] });
    await fixture.whenStable();

    expect(page.querySelector('[role="status"]')?.textContent).toContain(
      'That session was signed out — it loses access on its next request.',
    );
    expect(page.querySelectorAll('li')).toHaveLength(1);
    document.cookie = 'XSRF-TOKEN=; path=/; max-age=0';
  });

  it('a session_not_found response refreshes the list without a stale row or misleading error', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/sessions').flush({ sessions: [current, other] });
    await fixture.whenStable();
    button(page, 'Sign out').click();
    await fixture.whenStable();
    button(page, 'Continue').click();
    backend
      .expectOne(`/api/ui/sessions/${other.sessionId}`)
      .flush(
        { code: 'session_not_found', title: 'Gone.' },
        { status: 404, statusText: 'Not Found' },
      );
    fixture.detectChanges();
    backend.expectOne('/api/ui/sessions').flush({ sessions: [current] });
    await fixture.whenStable();

    expect(page.querySelectorAll('li')).toHaveLength(1);
    expect(page.textContent).not.toContain('Safari on iOS');
    expect(page.querySelector('[role="alert"]')?.textContent?.trim()).toBe('');
    expect(page.querySelector('[role="status"]')?.textContent?.trim()).toBe('');
  });

  it.each([
    [0, 'There were no other sessions to sign out.'],
    [1, 'Signed out 1 other session.'],
    [2, 'Signed out 2 other sessions.'],
  ])(
    'announces the server count %i after signing out all other devices',
    async (signedOut, text) => {
      const { fixture, page } = render();
      backend.expectOne('/api/ui/sessions').flush({ sessions: [current, other] });
      await fixture.whenStable();
      button(page, 'Sign out all other sessions').click();
      await fixture.whenStable();
      expect(page.querySelector('dialog')?.textContent).toContain(
        'Sign out all other devices? Each loses access on its next request.',
      );
      button(page, 'Continue').click();
      backend
        .expectOne({ method: 'POST', url: '/api/ui/sessions/sign-out-others' })
        .flush({ signedOut });
      fixture.detectChanges();
      backend.expectOne('/api/ui/sessions').flush({ sessions: [current] });
      await fixture.whenStable();

      expect(page.querySelector('[role="status"]')?.textContent).toContain(text);
    },
  );

  it('announces an unrecorded single revocation and refreshes the list', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/sessions').flush({ sessions: [current, other] });
    await fixture.whenStable();
    button(page, 'Sign out').click();
    await fixture.whenStable();
    button(page, 'Continue').click();
    backend
      .expectOne(`/api/ui/sessions/${other.sessionId}`)
      .flush(
        { code: 'revoked_unrecorded', title: unrecorded },
        { status: 503, statusText: 'Unavailable' },
      );
    fixture.detectChanges();
    backend.expectOne('/api/ui/sessions').flush({ sessions: [current] });
    await fixture.whenStable();

    expect(page.querySelector('[role="alert"]')?.textContent?.trim()).toBe(unrecorded);
    expect(page.querySelectorAll('li')).toHaveLength(1);
  });

  it('announces the count and audit failure after a bulk revocation and refreshes', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/sessions').flush({ sessions: [current, other] });
    await fixture.whenStable();
    button(page, 'Sign out all other sessions').click();
    await fixture.whenStable();
    button(page, 'Continue').click();
    backend
      .expectOne('/api/ui/sessions/sign-out-others')
      .flush(
        { code: 'revoked_unrecorded', title: unrecorded, signedOut: 2 },
        { status: 503, statusText: 'Unavailable' },
      );
    fixture.detectChanges();
    backend.expectOne('/api/ui/sessions').flush({ sessions: [current] });
    await fixture.whenStable();

    expect(page.querySelector('[role="alert"]')?.textContent?.trim()).toBe(
      `Signed out 2 other sessions. ${unrecorded}`,
    );
  });

  it('an incomplete bulk sign-out announces confirmed removals and refreshes the device list', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/sessions').flush({ sessions: [current, other] });
    await fixture.whenStable();
    button(page, 'Sign out all other sessions').click();
    await fixture.whenStable();
    button(page, 'Continue').click();
    const title =
      "Confirmed 1 other session signed out. The vault couldn't confirm every sign-out. Try again.";
    backend
      .expectOne('/api/ui/sessions/sign-out-others')
      .flush(
        { code: 'sign_out_incomplete', title, signedOut: 1 },
        { status: 503, statusText: 'Unavailable' },
      );
    fixture.detectChanges();
    backend.expectOne('/api/ui/sessions').flush({ sessions: [current] });
    await fixture.whenStable();

    expect(page.querySelector('[role="alert"]')?.textContent?.trim()).toBe(title);
    expect(page.querySelectorAll('li')).toHaveLength(1);
  });

  it('a refused write announces the problem and keeps the live device visible', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/sessions').flush({ sessions: [current, other] });
    await fixture.whenStable();
    button(page, 'Sign out').click();
    await fixture.whenStable();
    button(page, 'Continue').click();
    backend
      .expectOne(`/api/ui/sessions/${other.sessionId}`)
      .flush(
        { code: 'antiforgery_rejected', title: 'Reload the page and try again.' },
        { status: 400, statusText: 'Bad Request' },
      );
    await fixture.whenStable();

    expect(page.querySelector('[role="alert"]')?.textContent).toContain(
      'Reload the page and try again.',
    );
    expect(button(page, 'Sign out').disabled).toBe(false);
    expect(page.querySelectorAll('li')).toHaveLength(2);
  });
});

describe('deviceKind', () => {
  it.each([
    ['Safari on iOS', 'phone'],
    ['Chrome on Android', 'phone'],
    ['curl', 'terminal'],
    ['PYTHON', 'terminal'],
    ['PowerShell', 'terminal'],
    ['Firefox on Linux', 'monitor'],
  ] as const)('shows %s as a %s', (device, kind) => expect(deviceKind(device)).toBe(kind));
});
