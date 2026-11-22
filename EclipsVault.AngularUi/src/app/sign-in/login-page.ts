import { HttpClient, HttpXsrfTokenExtractor, httpResource } from '@angular/common/http';
import { Component, DestroyRef, DOCUMENT, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PasskeyProof } from '../profile/passkey-proof';
import { StatusLine } from '../shared/status-line';
import type { components } from '../ui-api/schema.generated';
import { UiApiError } from '../ui-api/ui-api-errors';
import { SignInFlow, SignInFocus } from './sign-in-flow';

type Schemas = components['schemas'];

export interface LoginNotice {
  readonly message: string;
  readonly error: boolean;
}

/** The meta tag the Login document carries a one-time notice in; see the server's SignInNotice. */
const handedOffNotice = 'meta[name="eclipsvault-sign-in-notice"]';

const signOutNotices: Readonly<Record<string, LoginNotice>> = {
  sign_out_incomplete: {
    message: "This browser's cookie was cleared. Other devices may still be signed in.",
    error: true,
  },
  everywhere: {
    message: 'You have been signed out of all sessions. Sign in again to continue.',
    error: false,
  },
  revoked_unrecorded: {
    message:
      "You have been signed out of all sessions, but the vault couldn't record it in the audit trail. Tell your administrator.",
    error: true,
  },
  everywhere_incomplete: {
    message:
      "This browser was signed out, but the vault couldn't confirm that your other devices were. Sign in and try again.",
    error: true,
  },
};

const ssoNotices: Readonly<Record<string, LoginNotice>> = {
  failed: { message: 'Single sign-on did not complete. Please try again.', error: true },
  refused: {
    message: 'Single sign-on did not grant access to this vault. Contact an administrator.',
    error: true,
  },
};

/**
 * What Login says on arrival. A query code can be typed by anyone, so it only ever explains an
 * outcome; the authenticator-reset sentence comes from the document's one-time handoff instead.
 */
export function loginNotice(search: string, handedOff: string | null): LoginNotice {
  const query = new URLSearchParams(search);
  if (query.get('signIn') === 'restarted')
    return {
      message: 'Your account changed while you were signing in. Sign in again.',
      error: true,
    };
  if (handedOff === 'authenticator_reset')
    return { message: 'Your authenticator was reset. Sign in to set up a new one.', error: false };
  return (
    known(ssoNotices, query.get('sso')) ??
    known(signOutNotices, query.get('signOut')) ?? { message: '', error: false }
  );
}

/** Only the table's own codes: a typed `?sso=constructor` must not find an inherited member. */
function known(notices: Readonly<Record<string, LoginNotice>>, code: string | null) {
  return code !== null && Object.hasOwn(notices, code) ? notices[code] : undefined;
}

@Component({
  selector: 'app-login-page',
  imports: [StatusLine, SignInFocus],
  templateUrl: './login-page.html',
  host: { class: 'auth-page' },
})
export class LoginPage {
  private readonly http = inject(HttpClient);
  private readonly flow = inject(SignInFlow);
  private readonly passkeys = inject(PasskeyProof);
  private readonly xsrf = inject(HttpXsrfTokenExtractor);
  private readonly document = inject(DOCUMENT);
  private readonly prompt = new AbortController();
  protected readonly returnUrl = this.flow.returnUrl();
  protected readonly options = httpResource<Schemas['AuthOptionsResponse']>(
    () => '/api/ui/auth/options',
  );
  protected readonly username = signal('');
  protected readonly password = signal('');
  protected readonly shown = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly passkeyBusy = signal(false);
  protected readonly passkeyStatus = signal({ message: '', error: false });
  protected readonly passkeysSupported = typeof PublicKeyCredential !== 'undefined';
  protected readonly notice: LoginNotice;

  constructor() {
    const meta = this.document.querySelector(handedOffNotice);
    // Said once: a later visit to Login inside this document must not repeat it.
    meta?.remove();
    this.notice = loginNotice(this.document.location.search, meta?.getAttribute('content') ?? null);
    inject(DestroyRef).onDestroy(() => this.prompt.abort());
  }

  protected requestToken(): string {
    return this.xsrf.getToken() ?? '';
  }

  protected signIn(event: Event): void {
    event.preventDefault();
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    const request: Schemas['PasswordSignInRequest'] = {
      username: this.username(),
      password: this.password(),
      returnUrl: this.returnUrl,
    };
    this.http.post<Schemas['PasswordStageResponse']>('/api/ui/auth/password', request).subscribe({
      next: (stage) =>
        this.flow.step(stage.next === 'enroll' ? '/Account/EnrollTotp' : '/Account/Totp'),
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(this.flow.refused(error) ?? '');
      },
    });
  }

  /** Discoverable sign-in: the passkey names the account, so the username field is not read. */
  protected async signInWithPasskey(): Promise<void> {
    this.passkeyBusy.set(true);
    this.passkeyStatus.set({ message: 'Follow the prompt on your device…', error: false });
    try {
      const request: Schemas['PasskeySignInBeginRequest'] = { returnUrl: this.returnUrl };
      const options = await firstValueFrom(
        this.http.post<Schemas['PasskeyOptionsResponse']>('/api/ui/auth/passkey/begin', request),
      );
      const credential = await this.passkeys.get(options.publicKey, this.prompt.signal);
      const result = await firstValueFrom(
        this.http.post<Schemas['SignInRedirectResponse']>('/api/ui/auth/passkey/complete', {
          credential,
        }),
      );
      this.flow.leave(result.redirect);
    } catch (error: unknown) {
      this.passkeyStatus.set({ message: passkeyFailure(error), error: true });
      this.passkeyBusy.set(false);
    }
  }
}

function passkeyFailure(error: unknown): string {
  if (error instanceof UiApiError) return error.title;
  if (error instanceof DOMException && ['NotAllowedError', 'AbortError'].includes(error.name))
    return 'The passkey prompt was dismissed or timed out.';
  return (error instanceof Error && error.message) || 'Something went wrong with the passkey.';
}
