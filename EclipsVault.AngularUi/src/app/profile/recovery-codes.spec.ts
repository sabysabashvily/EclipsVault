import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By, Title } from '@angular/platform-browser';
import { appConfig } from '../app.config';
import { EmailChange } from './email-change';
import { ProfilePage } from './profile-page';
import { button, dialogs, fill, operation, profile, refreshed } from './profile-test';

describe('ProfilePage recovery codes', () => {
  let backend: HttpTestingController;
  const codes = [
    'AAAAA-11111',
    'BBBBB-22222',
    'CCCCC-33333',
    'DDDDD-44444',
    'EEEEE-55555',
    'FFFFF-66666',
    'GGGGG-77777',
    'HHHHH-88888',
    'JJJJJ-99999',
    'KKKKK-00000',
  ];

  async function render(account = profile) {
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
    TestBed.inject(Title).setTitle('Profile — EclipsVault');
    const fixture = TestBed.createComponent(ProfilePage);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    dialogs(page);
    backend.expectOne('/api/ui/me').flush({});
    backend.expectOne('/api/ui/profile').flush(account);
    await fixture.whenStable();
    return { fixture, page, changes: page.querySelector<HTMLElement>('app-credential-change')! };
  }

  async function verifiedReplacement(account = profile) {
    const rendered = await render(account);
    const { fixture, page, changes } = rendered;
    button(page, 'Regenerate').click();
    await fixture.whenStable();
    button(changes, 'Continue').click();
    backend
      .expectOne('/api/ui/profile/operations')
      .flush({ ...operation, kind: 'ReplaceRecoveryCodes' });
    await fixture.whenStable();
    fill(changes, 'credential-verification-password', 'Current-Password');
    fill(changes, 'credential-verification-code', '123456');
    button(changes, 'Verify account').click();
    backend
      .expectOne(`/api/ui/profile/operations/${operation.operationId}/verify`)
      .flush({ operationId: operation.operationId, commitByUtc: '2099-01-01T00:00:00Z' });
    return { ...rendered, commit: backend.expectOne('/api/ui/profile/recovery-codes') };
  }

  async function shownCodes() {
    const shown = await verifiedReplacement();
    shown.commit.flush({ codes });
    shown.fixture.detectChanges();
    refreshed(backend, { ...profile, recoveryCodesRemaining: 10 });
    await shown.fixture.whenStable();
    return shown;
  }

  function listedCodes(page: HTMLElement): (string | null)[] {
    return [...page.querySelectorAll('.recovery-codes li')].map((code) => code.textContent);
  }

  afterEach(() => backend.verify({ ignoreCancelled: true }));

  it('asks before replacing the codes and freezes the replacement for verification', async () => {
    const { fixture, page, changes } = await render();
    button(page, 'Regenerate').click();
    await fixture.whenStable();
    expect(changes.querySelector('dialog[role=alertdialog]')?.textContent).toContain(
      'Generate a new set of recovery codes? Any codes you already have will immediately stop working.',
    );
    button(changes, 'Continue').click();
    const prepare = backend.expectOne({ method: 'POST', url: '/api/ui/profile/operations' });
    expect(prepare.request.body).toEqual({ kind: 'ReplaceRecoveryCodes' });
    prepare.flush({ ...operation, kind: 'ReplaceRecoveryCodes' });
    await fixture.whenStable();
    expect(changes.textContent).toContain(
      'Generate new recovery codes. Your current codes stop working, and any other signed-in devices will be signed out.',
    );
  });

  it('shows the ten returned codes with Print and Done', async () => {
    const { page } = await shownCodes();
    expect(page.querySelector('h1')?.textContent).toBe('Your recovery codes');
    expect(listedCodes(page)).toEqual(codes);
    expect(button(page, 'Print')).toBeDefined();
    expect(button(page, "I've saved them — done")).toBeDefined();
  });

  it('commits the verified replacement once and only by its operation', async () => {
    const { commit } = await verifiedReplacement();
    expect(commit.request.method).toBe('POST');
    expect(commit.request.body).toEqual({ operationId: operation.operationId });
    commit.flush({ codes });
    backend.expectNone('/api/ui/profile/recovery-codes');
  });

  it('titles the page Recovery codes while it shows them, and Profile again after Done', async () => {
    const { fixture, page } = await shownCodes();
    const title = TestBed.inject(Title);
    expect(title.getTitle()).toBe('Recovery codes — EclipsVault');
    button(page, "I've saved them — done").click();
    await fixture.whenStable();
    expect(title.getTitle()).toBe('Profile — EclipsVault');
  });

  it('Print prints the page', async () => {
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    const { page } = await shownCodes();
    button(page, 'Print').click();
    expect(print).toHaveBeenCalledOnce();
    print.mockRestore();
  });

  it('Done drops the codes and returns to Profile', async () => {
    const { fixture, page } = await shownCodes();
    button(page, "I've saved them — done").click();
    await fixture.whenStable();
    expect(page.querySelector('h1')?.textContent).toBe('Profile');
    expect(page.textContent).not.toContain('AAAAA-11111');
  });

  it.each(['pagehide', 'pageshow'])('drops the codes on %s', async (event) => {
    const { fixture, page } = await shownCodes();
    window.dispatchEvent(new Event(event));
    await fixture.whenStable();
    expect(page.textContent).not.toContain('AAAAA-11111');
  });

  it('drops the codes when the session ends', async () => {
    const { fixture, page } = await shownCodes();
    document.dispatchEvent(new Event('vault-session-ended'));
    await fixture.whenStable();
    expect(page.textContent).not.toContain('AAAAA-11111');
  });

  it('keeps the codes out of browser storage, cookies and the URL', async () => {
    const before = document.location.href;
    await shownCodes();
    expect(localStorage.length).toBe(0);
    expect(sessionStorage.length).toBe(0);
    expect(document.cookie).not.toContain('AAAAA');
    expect(document.location.href).toBe(before);
  });

  it('shows no codes when the browser could not stay signed in, and says how to see a new set', async () => {
    const { fixture, page, commit } = await verifiedReplacement();
    commit.flush(
      {
        code: 'changed_sign_in_required',
        title:
          'The change was saved, but this browser could not stay signed in. Sign in again to continue.',
      },
      { status: 503, statusText: 'Service Unavailable' },
    );
    await fixture.whenStable();
    expect(page.querySelector('.recovery-codes')).toBeNull();
    expect(page.querySelector('[role=alert]')?.textContent).toContain(
      'New recovery codes were generated. Sign in and deliberately generate a new set to see codes.',
    );
    expect(page.querySelector<HTMLAnchorElement>('a[href="/Account/Login"]')?.textContent).toBe(
      'Sign in again',
    );
  });

  it('explains that a lost reply needs a deliberate new set, without sending it again', async () => {
    const { fixture, page, commit } = await verifiedReplacement();
    commit.error(new ProgressEvent('error'));
    fixture.detectChanges();
    backend.expectNone('/api/ui/profile');
    await fixture.whenStable();
    expect(page.querySelector('.recovery-codes')).toBeNull();
    expect(page.querySelector('[role=alert]')?.textContent).toContain(
      'The response was lost, so no new codes can be shown. If a new set was generated, your old codes no longer work. Check your account or sign in, then deliberately generate a new set.',
    );
    expect(button(page, 'Regenerate').disabled).toBe(true);
    expect(page.querySelector('a[href="/Account/Login"]')?.textContent).toBe('Sign in again');
    backend.expectNone('/api/ui/me');
    backend.expectNone('/api/ui/profile/recovery-codes');
  });

  it('clears hidden mailbox input when recovery codes replace the Profile cards', async () => {
    const { fixture, page, commit } = await verifiedReplacement({
      ...profile,
      pendingEmail: {
        operationId: 'email-operation',
        address: 'new@example.test',
        expiresAtUtc: '2099-01-01T00:00:00Z',
      },
    });
    const email = fixture.debugElement.query(By.directive(EmailChange)).componentInstance;
    fill(page, 'mailbox-code', 'private-mailbox-code');
    expect(email.code()).toBe('private-mailbox-code');
    commit.flush({ codes });
    fixture.detectChanges();
    refreshed(backend);
    await fixture.whenStable();
    expect(email.code()).toBe('');
    button(page, "I've saved them — done").click();
    await fixture.whenStable();
    expect(email.code()).toBe('');
  });

  it.each([502, 504, 200])(
    'treats an unreadable HTTP %s reply as an unknown replacement outcome',
    async (status) => {
      const { fixture, page, commit } = await verifiedReplacement();
      commit.error(new ProgressEvent('error'), { status, statusText: 'Unreadable response' });
      await fixture.whenStable();
      expect(page.querySelector('[role=alert]')?.textContent).toContain(
        'If a new set was generated, your old codes no longer work.',
      );
      expect(button(page, 'Regenerate').disabled).toBe(true);
      expect(page.querySelector('a[href="/Account/Login"]')?.textContent).toBe('Sign in again');
      backend.expectNone('/api/ui/profile');
      backend.expectNone('/api/ui/profile/recovery-codes');
    },
  );

  it('commits nothing when verification expires before the replacement', async () => {
    const { fixture, page, changes } = await render();
    button(page, 'Regenerate').click();
    await fixture.whenStable();
    button(changes, 'Continue').click();
    backend
      .expectOne('/api/ui/profile/operations')
      .flush({ ...operation, kind: 'ReplaceRecoveryCodes' });
    await fixture.whenStable();
    fill(changes, 'credential-verification-password', 'Current-Password');
    fill(changes, 'credential-verification-code', '123456');
    button(changes, 'Verify account').click();
    backend
      .expectOne(`/api/ui/profile/operations/${operation.operationId}/verify`)
      .flush({ operationId: operation.operationId, commitByUtc: '2000-01-01T00:00:00Z' });
    await fixture.whenStable();
    expect(page.textContent).toContain('Your verification expired. Start the change again.');
    backend.expectNone('/api/ui/profile/recovery-codes');
  });

  it('prepares one replacement however often Regenerate is clicked', async () => {
    const { fixture, page, changes } = await render();
    button(page, 'Regenerate').click();
    button(page, 'Regenerate').click();
    await fixture.whenStable();
    button(changes, 'Continue').click();
    backend
      .expectOne('/api/ui/profile/operations')
      .flush({ ...operation, kind: 'ReplaceRecoveryCodes' });
    await fixture.whenStable();
    backend.expectNone('/api/ui/profile/operations');
  });

  it('explains why codes cannot be generated without an enrolled authenticator', async () => {
    const { page } = await render({
      ...profile,
      authenticatorEnrolled: false,
      recoveryCodesRemaining: 0,
      changes: {
        ...profile.changes,
        replaceRecoveryCodes: { available: false, unavailableReason: 'authenticator_not_enrolled' },
      },
    });
    expect(button(page, 'Generate codes').disabled).toBe(true);
    expect(page.textContent).toContain('This needs an authenticator app set up first.');
  });
});
