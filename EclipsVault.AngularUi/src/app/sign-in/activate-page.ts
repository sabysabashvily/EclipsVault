import { HttpClient } from '@angular/common/http';
import { afterNextRender, Component, computed, DOCUMENT, inject, signal } from '@angular/core';
import { passwordStrength } from '../profile/password-strength';
import { PAGE_LOCATION } from '../shell/command-palette';
import type { components } from '../ui-api/schema.generated';
import { UiApiError } from '../ui-api/ui-api-errors';
import { SignInFocus } from './sign-in-flow';

type Schemas = components['schemas'];

export const invalidInvitation =
  'This invitation link is not valid. It may have expired or been used. Ask an administrator to send a new one.';

/**
 * Activation by invitation (ADR-0033). The emailed link carries the token in its fragment, which a
 * browser never sends; the page posts it in a request body only, then drops it from the address bar.
 */
@Component({
  selector: 'app-activate-page',
  imports: [SignInFocus],
  host: { class: 'auth-page' },
  template: `
    <section class="auth-card activation-card">
      <h1>Activate your account</h1>
      @if (invalid()) {
        <p class="validation-summary" role="alert">{{ invalid() }}</p>
      } @else if (invitation(); as invitation) {
        <p class="muted">
          Choose your password and set up your authenticator. Multi-factor sign-in is mandatory.
        </p>

        <form (submit)="activate($event)">
          <fieldset [disabled]="busy()">
            @if (error()) {
              <div class="validation-summary" role="alert">
                <ul>
                  <li>{{ error() }}</li>
                </ul>
              </div>
            }

            <div class="field">
              <label for="Username">Username</label>
              <input
                id="Username"
                class="mono"
                autocomplete="username"
                readonly
                [value]="invitation.username"
              />
            </div>

            <div class="field">
              <label for="new-password">New password</label>
              <div class="input-affix">
                <input
                  #newPasswordInput
                  id="new-password"
                  [type]="shown() ? 'text' : 'password'"
                  autocomplete="new-password"
                  minlength="12"
                  maxlength="256"
                  required
                  appSignInFocus
                  [value]="password()"
                  (input)="password.set($any($event.target).value)"
                /><button
                  class="affix-button"
                  type="button"
                  [attr.aria-label]="shown() ? 'Hide password' : 'Show password'"
                  (click)="shown.set(!shown()); newPasswordInput.focus()"
                >
                  <svg
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    stroke-width="1.7"
                    stroke-linecap="round"
                    stroke-linejoin="round"
                    aria-hidden="true"
                  >
                    @if (shown()) {
                      <path
                        d="M2 12s3.6-7 10-7c2 0 3.8.7 5.2 1.6M22 12s-3.6 7-10 7c-2 0-3.8-.7-5.2-1.6"
                      />
                      <path d="m4 4 16 16" />
                    } @else {
                      <path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7-10-7-10-7Z" />
                      <circle cx="12" cy="12" r="3" />
                    }
                  </svg>
                </button>
              </div>
              @if (password()) {
                <div class="strength" [attr.data-score]="strength().score">
                  <div
                    class="strength-track"
                    role="img"
                    [attr.aria-label]="'Password strength: ' + strength().text"
                  >
                    <span class="strength-fill"></span>
                  </div>
                  <p class="strength-label">{{ strength().text }}</p>
                </div>
              }
              <span class="hint muted"
                >At least 12 characters. Checked against a corpus of passwords exposed in public
                breaches (NIST 800-63B).</span
              >
            </div>
            <div class="field">
              <label for="confirm-password">Confirm new password</label
              ><input
                id="confirm-password"
                type="password"
                autocomplete="new-password"
                maxlength="256"
                required
                [value]="confirmation()"
                (input)="confirmation.set($any($event.target).value)"
              />
            </div>

            <div class="totp-secret">
              <p><strong>Scan this QR code with your authenticator app</strong></p>
              <img
                class="totp-qr"
                [src]="invitation.qrCodeDataUri"
                alt="QR code containing the authenticator enrollment secret"
              />
              <p class="muted">Can't scan it? Enter this key manually:</p>
              <code class="secret-code">{{ invitation.secretBase32 }}</code>
            </div>

            <div class="field">
              <label for="Code">Confirmation code</label>
              <input
                id="Code"
                name="Code"
                class="code-input"
                inputmode="numeric"
                autocomplete="one-time-code"
                maxlength="6"
                required
                [value]="code()"
                (input)="code.set($any($event.target).value)"
              />
            </div>

            <button type="submit" class="button primary wide">Activate account</button>
          </fieldset>
        </form>
      } @else if (error()) {
        <p class="validation-summary" role="alert">{{ error() }}</p>
      } @else {
        <p class="muted" role="status">Checking your invitation…</p>
      }
    </section>
  `,
})
export class ActivatePage {
  private readonly http = inject(HttpClient);
  private readonly location = inject(PAGE_LOCATION);
  private readonly token: string;
  protected readonly invitation = signal<Schemas['ActivationResponse'] | null>(null);
  protected readonly invalid = signal('');
  protected readonly password = signal('');
  protected readonly confirmation = signal('');
  protected readonly code = signal('');
  protected readonly shown = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly strength = computed(() => passwordStrength(this.password()));

  constructor() {
    const document = inject(DOCUMENT);
    this.token = document.location.hash.slice(1);
    // Once the router has settled the URL, so the token stays out of the address bar and history.
    afterNextRender(() => {
      const { pathname, search } = document.location;
      document.defaultView?.history.replaceState(null, '', pathname + search);
    });
    if (!this.token) {
      this.invalid.set(invalidInvitation);
      return;
    }
    const request: Schemas['ActivationRequest'] = { token: this.token };
    this.http.post<Schemas['ActivationResponse']>('/api/ui/auth/activation', request).subscribe({
      next: (invitation) => this.invitation.set(invitation),
      error: (error: unknown) => this.refused(error),
    });
  }

  protected activate(event: Event): void {
    event.preventDefault();
    if (this.busy()) return;
    const password = this.password();
    let message = '';
    if (password.length < 12 || password.length > 256)
      message = 'Your new password must be 12–256 characters.';
    else if (password !== this.confirmation()) message = 'The passwords do not match.';
    this.error.set(message);
    if (message) return;
    this.busy.set(true);
    const request: Schemas['CompleteActivationRequest'] = {
      token: this.token,
      password,
      code: this.code(),
    };
    this.http.post<void>('/api/ui/auth/activation/complete', request).subscribe({
      next: () => this.location.assign('/Account/Login?activated=1'),
      error: (error: unknown) => {
        this.busy.set(false);
        this.refused(error);
      },
    });
  }

  private refused(error: unknown): void {
    if (error instanceof UiApiError && error.code === 'invitation_invalid') {
      this.invitation.set(null);
      this.invalid.set(invalidInvitation);
      return;
    }
    this.error.set(
      error instanceof UiApiError
        ? (Object.values(error.errors)[0]?.[0] ?? error.title)
        : 'The vault could not complete the request.',
    );
  }
}
