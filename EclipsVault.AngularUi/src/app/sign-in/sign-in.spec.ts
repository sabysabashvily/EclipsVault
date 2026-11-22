import { provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { PasskeyProof } from '../profile/passkey-proof';
import { PAGE_LOCATION } from '../shell/command-palette';
import { signInApiErrors } from '../ui-api/ui-api-errors';
import { EnrollTotpPage } from './enroll-totp-page';
import { LoginPage, loginNotice } from './login-page';
import { RecoverPage } from './recover-page';
import { RecoveryCodePage } from './recovery-code-page';
import { TotpPage } from './totp-page';

const reset = 'Your authenticator was reset. Sign in to set up a new one.';

describe('loginNotice', () => {
  it.each([
    [
      '?signOut=sign_out_incomplete',
      "This browser's cookie was cleared. Other devices may still be signed in.",
      true,
    ],
    [
      '?signOut=everywhere',
      'You have been signed out of all sessions. Sign in again to continue.',
      false,
    ],
    [
      '?signOut=revoked_unrecorded',
      "You have been signed out of all sessions, but the vault couldn't record it in the audit trail. Tell your administrator.",
      true,
    ],
    [
      '?signOut=everywhere_incomplete',
      "This browser was signed out, but the vault couldn't confirm that your other devices were. Sign in and try again.",
      true,
    ],
    ['?sso=failed', 'Single sign-on did not complete. Please try again.', true],
    [
      '?sso=refused',
      'Single sign-on did not grant access to this vault. Contact an administrator.',
      true,
    ],
  ])('says what %s means', (search, message, error) => {
    expect(loginNotice(search, null)).toEqual({ message, error });
  });

  it('says nothing for a code it does not know, a typed reset code included', () => {
    expect(loginNotice('?signOut=authenticator_reset', null)).toEqual({
      message: '',
      error: false,
    });
    expect(loginNotice('?signOut=other&sso=other', null)).toEqual({
      message: '',
      error: false,
    });
  });

  it.each(['?sso=constructor', '?signOut=toString', '?signOut=__proto__'])(
    'says nothing for %s, a name every object inherits',
    (search) => {
      expect(loginNotice(search, null)).toEqual({ message: '', error: false });
    },
  );

  it('says the reset sentence only from the handoff', () => {
    expect(loginNotice('', 'authenticator_reset')).toEqual({ message: reset, error: false });
  });

  it('puts a restarted sign-in first', () => {
    expect(loginNotice('?sso=failed&signIn=restarted', null)).toEqual({
      message: 'Your account changed while you were signing in. Sign in again.',
      error: true,
    });
  });
});

describe('sign-in pages', () => {
  const assign = vi.fn();
  const passkey = { get: vi.fn() };
  let backend: HttpTestingController;
  let navigate: ReturnType<typeof vi.fn<(target: unknown, extras?: unknown) => Promise<boolean>>>;

  async function render<T>(page: Type<T>, url: string) {
    history.replaceState(null, '', url);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(
          withInterceptors([signInApiErrors]),
          withXsrfConfiguration({
            cookieName: 'XSRF-TOKEN',
            headerName: 'RequestVerificationToken',
          }),
        ),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: PAGE_LOCATION, useValue: { assign } },
        { provide: PasskeyProof, useValue: passkey },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    navigate = vi
      .fn<(target: unknown, extras?: unknown) => Promise<boolean>>()
      .mockResolvedValue(true);
    vi.spyOn(router, 'navigate').mockImplementation(navigate);
    vi.spyOn(router, 'navigateByUrl').mockImplementation(navigate);
    const fixture = TestBed.createComponent(page);
    // Not whenStable: the page's first call is still waiting for the test to answer it.
    TestBed.tick();
    return { fixture, page: fixture.nativeElement as HTMLElement };
  }

  function type(page: HTMLElement, id: string, value: string): void {
    const input = page.querySelector<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function submit(page: HTMLElement): void {
    page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
  }

  function button(page: HTMLElement, text: string): HTMLButtonElement {
    return [...page.querySelectorAll('button')].find((b) =>
      b.textContent?.trim().startsWith(text),
    )!;
  }

  function refuse(url: string, status: number, code: string, title: string): void {
    backend.expectOne(url).flush({ status, code, title }, { status, statusText: 'Refused' });
  }

  beforeEach(() => {
    assign.mockReset();
    passkey.get.mockReset();
    document.cookie = 'XSRF-TOKEN=anonymous-token; path=/';
  });

  afterEach(() => {
    backend.verify();
    document.cookie = 'XSRF-TOKEN=; path=/; max-age=0';
    vi.unstubAllGlobals();
    history.replaceState(null, '', '/');
  });

  describe('Login', () => {
    function options(ssoEnabled: boolean): void {
      backend
        .expectOne('/api/ui/auth/options')
        .flush({ ssoEnabled, ssoDisplayName: 'Contoso SSO' });
    }

    it('is the Legacy page: step one of two, the password form, the passkey button', async () => {
      const { page, fixture } = await render(LoginPage, '/Account/Login');
      options(false);
      await fixture.whenStable();

      expect(page.querySelector('h1')?.textContent).toBe('Sign in');
      expect(page.querySelector('.auth-card > .muted')?.textContent).toBe(
        'Step 1 of 2 — password. An authenticator code is always required next.',
      );
      expect(page.querySelector('label[for="Username"]')?.textContent).toBe('Username or email');
      expect(page.querySelector('#Username')?.getAttribute('autocomplete')).toBe('username');
      expect(page.querySelector('#Password')?.getAttribute('autocomplete')).toBe(
        'current-password',
      );
      expect(button(page, 'Continue').type).toBe('submit');
      expect(button(page, 'Sign in with a passkey').textContent?.trim()).toBe(
        'Sign in with a passkey',
      );
      expect(page.querySelector('form[action="/Account/ExternalLogin"]')).toBeNull();
      expect(page.querySelector('.flash')).toBeNull();
    });

    it('shows the restarted notice in a fresh Login document and keeps the return URL', async () => {
      const { page, fixture } = await render(
        LoginPage,
        '/Account/Login?ReturnUrl=%2FSecrets&signIn=restarted',
      );
      options(true);
      await fixture.whenStable();

      expect(page.querySelector('[role="alert"]')?.textContent?.trim()).toBe(
        'Your account changed while you were signing in. Sign in again.',
      );
      expect(button(page, 'Sign in with Contoso SSO').disabled).toBe(false);
      type(page, 'Username', 'devon');
      type(page, 'Password', 'correct horse');
      submit(page);
      const request = backend.expectOne('/api/ui/auth/password');
      expect(request.request.body).toEqual({
        username: 'devon',
        password: 'correct horse',
        returnUrl: '/Secrets',
      });
      request.flush({ next: 'totp' });
    });

    it('sends the password with the return URL, then goes on to the authenticator code', async () => {
      const { page, fixture } = await render(
        LoginPage,
        '/Account/Login?ReturnUrl=%2FSecrets%3Fq%3D1',
      );
      options(false);
      type(page, 'Username', 'devon');
      type(page, 'Password', 'correct horse');
      submit(page);

      const request = backend.expectOne({ method: 'POST', url: '/api/ui/auth/password' });
      expect(request.request.body).toEqual({
        username: 'devon',
        password: 'correct horse',
        returnUrl: '/Secrets?q=1',
      });
      expect(request.request.headers.get('RequestVerificationToken')).toBe('anonymous-token');
      request.flush({ next: 'totp' });
      await fixture.whenStable();

      expect(navigate).toHaveBeenCalledWith(['/Account/Totp'], {
        queryParams: { ReturnUrl: '/Secrets?q=1' },
      });
    });

    it('goes on to enrollment when the account has no authenticator yet', async () => {
      const { page } = await render(LoginPage, '/Account/Login');
      options(false);
      submit(page);
      backend.expectOne('/api/ui/auth/password').flush({ next: 'enroll' });

      expect(navigate).toHaveBeenCalledWith(['/Account/EnrollTotp'], { queryParams: {} });
    });

    it.each([
      [400, 'sign_in_failed', 'Invalid username or password.'],
      [429, 'throttled', 'Too many attempts. Wait a minute and try again.'],
      [503, 'audit_unavailable', 'The audit trail is unavailable.'],
    ])('shows a %i %s refusal and lets the person try again', async (status, code, title) => {
      const { page, fixture } = await render(LoginPage, '/Account/Login');
      options(false);
      submit(page);
      refuse('/api/ui/auth/password', status, code, title);
      await fixture.whenStable();

      expect(page.querySelector('.validation-summary')?.textContent?.trim()).toBe(title);
      expect(page.querySelector('fieldset')?.disabled).toBe(false);
      expect(navigate).not.toHaveBeenCalled();
    });

    it('shows a sign-out-everywhere outcome from its own code in the status line', async () => {
      const { page, fixture } = await render(
        LoginPage,
        '/Account/Login?signOut=revoked_unrecorded',
      );
      options(false);
      await fixture.whenStable();

      expect(page.querySelector('[role="alert"] .flash-error')?.textContent?.trim()).toBe(
        "You have been signed out of all sessions, but the vault couldn't record it in the audit trail. Tell your administrator.",
      );
    });

    it('shows the authenticator-reset sentence from the document handoff once, and nothing for the typed code', async () => {
      const meta = document.createElement('meta');
      meta.name = 'eclipsvault-sign-in-notice';
      meta.content = 'authenticator_reset';
      document.head.append(meta);
      const first = await render(LoginPage, '/Account/Login?signOut=authenticator_reset');
      options(false);
      await first.fixture.whenStable();

      expect(first.page.querySelector('[role="status"] .flash-success')?.textContent?.trim()).toBe(
        reset,
      );
      expect(document.querySelector('meta[name="eclipsvault-sign-in-notice"]')).toBeNull();

      const again = TestBed.createComponent(LoginPage);
      TestBed.tick();
      backend.expectOne('/api/ui/auth/options').flush({ ssoEnabled: false, ssoDisplayName: '' });
      await again.whenStable();
      expect((again.nativeElement as HTMLElement).querySelector('.flash')).toBeNull();
    });

    it('starts single sign-on with a real form post carrying the request token and return URL', async () => {
      const { page, fixture } = await render(LoginPage, '/Account/Login?ReturnUrl=%2FSecrets');
      options(true);
      await fixture.whenStable();

      const form = page.querySelector<HTMLFormElement>('form[action="/Account/ExternalLogin"]')!;
      expect(form.method).toBe('post');
      expect(button(form, 'Sign in with Contoso SSO').type).toBe('submit');
      expect(form.querySelector<HTMLInputElement>('input[name="ReturnUrl"]')?.value).toBe(
        '/Secrets',
      );
      form.addEventListener('submit', (event) => event.preventDefault());
      form.dispatchEvent(new Event('submit', { cancelable: true }));
      expect(
        form.querySelector<HTMLInputElement>('input[name="__RequestVerificationToken"]')?.value,
      ).toBe('anonymous-token');
    });

    describe('with a passkey', () => {
      beforeEach(() => vi.stubGlobal('PublicKeyCredential', class {}));

      it('signs in without reading the username, then leaves for the page the vault chose', async () => {
        const { page, fixture } = await render(LoginPage, '/Account/Login?ReturnUrl=%2FSecrets');
        options(false);
        type(page, 'Username', 'not-sent');
        passkey.get.mockResolvedValue({ id: 'credential' });
        button(page, 'Sign in with a passkey').click();
        await fixture.whenStable();

        const begin = backend.expectOne({ method: 'POST', url: '/api/ui/auth/passkey/begin' });
        expect(begin.request.body).toEqual({ returnUrl: '/Secrets' });
        expect(page.querySelector('.passkey-status')?.textContent?.trim()).toBe(
          'Follow the prompt on your device…',
        );
        begin.flush({ publicKey: { challenge: 'abc' } });
        await vi.waitFor(() => expect(passkey.get).toHaveBeenCalled());
        expect(passkey.get.mock.calls[0]?.[0]).toEqual({ challenge: 'abc' });
        await Promise.resolve();
        const complete = backend.expectOne({
          method: 'POST',
          url: '/api/ui/auth/passkey/complete',
        });
        expect(complete.request.body).toEqual({ credential: { id: 'credential' } });
        complete.flush({ redirect: '/Secrets' });

        await vi.waitFor(() => expect(assign).toHaveBeenCalledExactlyOnceWith('/Secrets'));
      });

      it('says a dismissed prompt was dismissed and lets the person try again', async () => {
        const { page, fixture } = await render(LoginPage, '/Account/Login');
        options(false);
        passkey.get.mockRejectedValue(new DOMException('No', 'NotAllowedError'));
        button(page, 'Sign in with a passkey').click();
        backend.expectOne('/api/ui/auth/passkey/begin').flush({ publicKey: {} });
        await vi.waitFor(() =>
          expect(page.querySelector('.passkey-status')?.textContent?.trim()).toBe(
            'The passkey prompt was dismissed or timed out.',
          ),
        );
        await fixture.whenStable();

        expect(button(page, 'Sign in with a passkey').disabled).toBe(false);
      });

      it('shows the vault’s refusal of the passkey', async () => {
        const { page } = await render(LoginPage, '/Account/Login');
        options(false);
        passkey.get.mockResolvedValue({ id: 'credential' });
        button(page, 'Sign in with a passkey').click();
        backend.expectOne('/api/ui/auth/passkey/begin').flush({ publicKey: {} });
        await vi.waitFor(() => expect(passkey.get).toHaveBeenCalled());
        await Promise.resolve();
        refuse(
          '/api/ui/auth/passkey/complete',
          409,
          'sign_in_restarted',
          'Sign-in timed out. Try again.',
        );

        await vi.waitFor(() =>
          expect(page.querySelector('.passkey-status')?.textContent?.trim()).toBe(
            'Sign-in timed out. Try again.',
          ),
        );
        expect(assign).not.toHaveBeenCalled();
      });
    });

    it('leaves the passkey button off in a browser without passkeys', async () => {
      const { page } = await render(LoginPage, '/Account/Login');
      options(false);

      expect(button(page, 'Sign in with a passkey').disabled).toBe(true);
      expect(button(page, 'Sign in with a passkey').title).toBe(
        'This browser does not support passkeys.',
      );
    });
  });

  describe('Totp', () => {
    it('is the Legacy page, linking to the recovery code with the return URL', async () => {
      const { page } = await render(TotpPage, '/Account/Totp?ReturnUrl=%2FSecrets');

      expect(page.querySelector('h1')?.textContent).toBe('Two-factor verification');
      expect(page.querySelector('.auth-card > .muted')?.textContent).toBe(
        'Step 2 of 2 — enter the 6-digit code from your authenticator app.',
      );
      expect(page.querySelector('label[for="Code"]')?.textContent).toBe('Authenticator code');
      const code = page.querySelector('#Code')!;
      expect(code.getAttribute('inputmode')).toBe('numeric');
      expect(code.getAttribute('maxlength')).toBe('6');
      expect(page.querySelector('.footnote a')?.getAttribute('href')).toBe(
        '/Account/RecoveryCode?ReturnUrl=%2FSecrets',
      );
      expect(button(page, 'Verify and sign in').type).toBe('submit');
    });

    it('signs in with the code and leaves for the page the vault chose', async () => {
      const { page } = await render(TotpPage, '/Account/Totp?ReturnUrl=%2FSecrets');
      type(page, 'Code', '123456');
      submit(page);

      const request = backend.expectOne({ method: 'POST', url: '/api/ui/auth/totp' });
      expect(request.request.body).toEqual({ code: '123456', returnUrl: '/Secrets' });
      request.flush({ redirect: '/Secrets' });

      expect(assign).toHaveBeenCalledExactlyOnceWith('/Secrets');
    });

    it('shows a wrong code and a field error from validation', async () => {
      const { page, fixture } = await render(TotpPage, '/Account/Totp');
      submit(page);
      refuse('/api/ui/auth/totp', 400, 'code_invalid', 'Invalid authenticator code.');
      await fixture.whenStable();
      expect(page.querySelector('.validation-summary')?.textContent?.trim()).toBe(
        'Invalid authenticator code.',
      );

      submit(page);
      backend.expectOne('/api/ui/auth/totp').flush(
        {
          status: 400,
          code: 'validation_failed',
          title: 'One or more fields are invalid.',
          errors: { Code: ['Enter the 6-digit code from your authenticator app.'] },
        },
        { status: 400, statusText: 'Bad Request' },
      );
      await fixture.whenStable();
      expect(page.querySelector('.validation-summary')?.textContent?.trim()).toBe(
        'Enter the 6-digit code from your authenticator app.',
      );
      expect(assign).not.toHaveBeenCalled();
    });

    it('starts again at Login, with the reason, when the account changed under the sign-in', async () => {
      const { page } = await render(TotpPage, '/Account/Totp?ReturnUrl=%2FSecrets');
      submit(page);
      refuse(
        '/api/ui/auth/totp',
        409,
        'sign_in_restarted',
        'Your account changed while you were signing in. Sign in again.',
      );

      expect(assign).toHaveBeenCalledExactlyOnceWith(
        '/Account/Login?ReturnUrl=%2FSecrets&signIn=restarted',
      );
      expect(navigate).not.toHaveBeenCalled();
      expect(page.querySelector('.validation-summary')).toBeNull();
    });

    it('starts again at Login with a page load when the pending sign-in expired', async () => {
      const { page } = await render(TotpPage, '/Account/Totp?ReturnUrl=%2FSecrets');
      submit(page);
      refuse('/api/ui/auth/totp', 401, 'sign_in_expired', 'Your sign-in expired. Sign in again.');

      expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login?ReturnUrl=%2FSecrets');
    });
  });

  describe('RecoveryCode', () => {
    it('is the Legacy page, linking back to the authenticator code', async () => {
      const { page } = await render(RecoveryCodePage, '/Account/RecoveryCode');

      expect(page.querySelector('h1')?.textContent).toBe('Use a recovery code');
      expect(
        page.querySelector('.auth-card > .muted')?.textContent?.replace(/\s+/g, ' ').trim(),
      ).toBe(
        'Step 2 of 2 — enter one of the single-use recovery codes you saved when you set up MFA. Each code works only once.',
      );
      const code = page.querySelector('#Code')!;
      expect(code.getAttribute('placeholder')).toBe('XXXXX-XXXXX');
      expect(code.getAttribute('maxlength')).toBe('16');
      expect(page.querySelector('.footnote a')?.getAttribute('href')).toBe('/Account/Totp');
    });

    it('says how many codes are left, and continues only when asked', async () => {
      const { page, fixture } = await render(
        RecoveryCodePage,
        '/Account/RecoveryCode?ReturnUrl=%2FSecrets',
      );
      type(page, 'Code', 'ABCDE-12345');
      submit(page);

      const request = backend.expectOne({ method: 'POST', url: '/api/ui/auth/recovery-code' });
      expect(request.request.body).toEqual({ code: 'ABCDE-12345', returnUrl: '/Secrets' });
      request.flush({ redirect: '/Secrets', codesRemaining: 1 });
      await fixture.whenStable();

      expect(page.querySelector('.flash-info')?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
        "You signed in with a recovery code — that code is now used up. You have 1 recovery code left. Generate a new set from your profile if you're running low.",
      );
      expect(page.querySelector('form')).toBeNull();
      expect(assign).not.toHaveBeenCalled();

      button(page, 'Continue').click();
      expect(assign).toHaveBeenCalledExactlyOnceWith('/Secrets');
    });

    it('counts codes in the plural', async () => {
      const { page, fixture } = await render(RecoveryCodePage, '/Account/RecoveryCode');
      submit(page);
      backend
        .expectOne('/api/ui/auth/recovery-code')
        .flush({ redirect: '/Secrets', codesRemaining: 0 });
      await fixture.whenStable();

      expect(page.querySelector('.flash-info')?.textContent).toContain(
        'You have 0 recovery codes left.',
      );
    });

    it('shows a used or wrong code', async () => {
      const { page, fixture } = await render(RecoveryCodePage, '/Account/RecoveryCode');
      submit(page);
      refuse(
        '/api/ui/auth/recovery-code',
        400,
        'code_invalid',
        'That recovery code is not valid, or it has already been used.',
      );
      await fixture.whenStable();

      expect(page.querySelector('.validation-summary')?.textContent?.trim()).toBe(
        'That recovery code is not valid, or it has already been used.',
      );
    });
  });

  describe('EnrollTotp', () => {
    const enrollment = {
      secretBase32: 'JBSWY3DPEHPK3PXP',
      qrCodeDataUri: 'data:image/png;base64,AAAA',
    };

    it('shows the new authenticator’s QR code and key', async () => {
      const { page, fixture } = await render(EnrollTotpPage, '/Account/EnrollTotp');
      backend.expectOne({ method: 'POST', url: '/api/ui/auth/enrollment' }).flush(enrollment);
      await fixture.whenStable();

      expect(page.querySelector('h1')?.textContent).toBe('Set up two-factor authentication');
      expect(page.querySelector('img.totp-qr')?.getAttribute('src')).toBe(enrollment.qrCodeDataUri);
      expect(page.querySelector('img.totp-qr')?.getAttribute('alt')).toBe(
        'QR code containing the authenticator enrollment secret',
      );
      expect(page.querySelector('code.secret-code')?.textContent).toBe('JBSWY3DPEHPK3PXP');
      expect(page.querySelector('label[for="Code"]')?.textContent).toBe('Confirmation code');
    });

    it('confirms with a code and leaves for the page the vault chose', async () => {
      const { page } = await render(EnrollTotpPage, '/Account/EnrollTotp?ReturnUrl=%2FSecrets');
      backend.expectOne('/api/ui/auth/enrollment').flush(enrollment);
      type(page, 'Code', '654321');
      submit(page);

      const request = backend.expectOne('/api/ui/auth/enrollment/confirm');
      expect(request.request.body).toEqual({ code: '654321', returnUrl: '/Secrets' });
      request.flush({ redirect: '/Secrets' });

      expect(assign).toHaveBeenCalledExactlyOnceWith('/Secrets');
    });

    it('shows a code that did not match', async () => {
      const { page, fixture } = await render(EnrollTotpPage, '/Account/EnrollTotp');
      backend.expectOne('/api/ui/auth/enrollment').flush(enrollment);
      submit(page);
      refuse(
        '/api/ui/auth/enrollment/confirm',
        400,
        'code_invalid',
        'The confirmation code did not match. Try again.',
      );
      await fixture.whenStable();

      expect(page.querySelector('.validation-summary')?.textContent?.trim()).toBe(
        'The confirmation code did not match. Try again.',
      );
    });

    it('starts again at Login when the account changed before enrollment began', async () => {
      await render(EnrollTotpPage, '/Account/EnrollTotp');
      refuse(
        '/api/ui/auth/enrollment',
        409,
        'sign_in_restarted',
        'Your account changed while you were signing in. Sign in again.',
      );

      expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login?signIn=restarted');
    });
  });
  describe('Recover', () => {
    const lifted =
      'The intrusion-defence block on your network was lifted and your session restored.';
    const restored = 'Access restored. Your network was not blocked.';

    function recoverWith(page: HTMLElement): void {
      type(page, 'Username', 'vault-admin');
      type(page, 'Password', 'correct horse');
      type(page, 'Code', '123456');
      submit(page);
    }

    function text(page: HTMLElement): string {
      return page.textContent?.replace(/\s+/g, ' ').trim() ?? '';
    }

    it('is the Legacy page, and makes no call until the form is sent', async () => {
      const { page } = await render(RecoverPage, '/Account/Recover');

      expect(page.querySelector('h1')?.textContent).toBe('Recover access');
      expect(page.querySelector('p.muted')?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
        'Administrator break-glass recovery. Enter all of your credentials in one step — if they ' +
          'verify and your account holds TopSecret clearance, the intrusion-defence block on your ' +
          'network is lifted and your session restored. Every attempt is audited.',
      );
      expect([...page.querySelectorAll('label')].map((label) => label.textContent)).toEqual([
        'Username',
        'Password',
        'Authenticator code',
      ]);
      expect(page.querySelector<HTMLInputElement>('#Password')?.type).toBe('password');
      expect(page.querySelector('#Code')?.getAttribute('autocomplete')).toBe('one-time-code');
      expect(button(page, 'Verify and restore access').type).toBe('submit');
      expect(page.querySelector('p.footnote')?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
        'Not an administrator? Ask one to lift the block from Admin → Networks, or restart the ' +
          'application in development.',
      );
      backend.expectNone(() => true);
    });

    it('sends every factor at once, says the block was lifted and continues to the vault', async () => {
      const { page, fixture } = await render(RecoverPage, '/Account/Recover');
      recoverWith(page);

      const request = backend.expectOne({ method: 'POST', url: '/api/ui/auth/recover' });
      expect(request.request.body).toEqual({
        username: 'vault-admin',
        password: 'correct horse',
        code: '123456',
      });
      expect(request.request.headers.get('RequestVerificationToken')).toBe('anonymous-token');
      request.flush({ blockLifted: true });
      await fixture.whenStable();

      expect(page.querySelector('[role="status"].flash-success')?.textContent?.trim()).toBe(lifted);
      expect(page.querySelector('form')).toBeNull();
      expect(assign).not.toHaveBeenCalled();
      button(page, 'Continue to the vault').click();
      expect(assign).toHaveBeenCalledExactlyOnceWith('/');
    });

    it('says access was restored when the network was not blocked', async () => {
      const { page, fixture } = await render(RecoverPage, '/Account/Recover');
      recoverWith(page);
      backend.expectOne('/api/ui/auth/recover').flush({ blockLifted: false });
      await fixture.whenStable();

      expect(page.querySelector('.flash-success')?.textContent?.trim()).toBe(restored);
      expect(text(page)).not.toContain('lifted');
    });

    it.each([
      [400, 'recovery_failed', 'Recovery could not be completed with these credentials.'],
      [
        503,
        'recovery_incomplete',
        'Your sign-in was recorded, but the vault could not finish restoring network access. This source remains blocked. Contact an administrator.',
      ],
      [
        503,
        'audit_unavailable',
        'The audit trail is unavailable, so the vault refused the request.',
      ],
    ])(
      'shows a %s %s in the vault’s words and claims no access restored',
      async (status, code, title) => {
        const { page, fixture } = await render(RecoverPage, '/Account/Recover');
        recoverWith(page);
        refuse('/api/ui/auth/recover', status, code, title);
        await fixture.whenStable();

        expect(page.querySelector('.validation-summary')?.textContent?.trim()).toBe(title);
        expect(page.querySelector('.flash-success')).toBeNull();
        expect(text(page)).not.toContain(restored);
        expect(text(page)).not.toContain(lifted);
        expect(text(page)).not.toContain('Continue to the vault');
        expect(assign).not.toHaveBeenCalled();
        expect(navigate).not.toHaveBeenCalled();
      },
    );

    it('shows a field error from validation and lets the person send again', async () => {
      const { page, fixture } = await render(RecoverPage, '/Account/Recover');
      recoverWith(page);
      backend.expectOne('/api/ui/auth/recover').flush(
        {
          status: 400,
          code: 'validation_failed',
          title: 'One or more fields are invalid.',
          errors: { Code: ['Enter the 6-digit code from your authenticator app.'] },
        },
        { status: 400, statusText: 'Bad Request' },
      );
      await fixture.whenStable();

      expect(page.querySelector('.validation-summary')?.textContent?.trim()).toBe(
        'Enter the 6-digit code from your authenticator app.',
      );
      expect(page.querySelector('fieldset')?.disabled).toBe(false);
      submit(page);
      backend.expectOne('/api/ui/auth/recover').flush({ blockLifted: false });
    });
  });
});
