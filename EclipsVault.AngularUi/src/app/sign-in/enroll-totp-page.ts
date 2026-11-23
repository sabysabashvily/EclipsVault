import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import type { components } from '../ui-api/schema.generated';
import { SignInFlow, SignInFocus } from './sign-in-flow';

type Schemas = components['schemas'];

@Component({
  selector: 'app-enroll-totp-page',
  imports: [SignInFocus],
  host: { class: 'auth-page' },
  template: `
    <section class="auth-card">
      <div class="auth-step">
        <div class="step-track" aria-hidden="true">
          <span class="step done"></span><span class="step active"></span>
        </div>
        <span class="auth-step-label">Step 2 of 2</span>
      </div>
      <h1>Set up two-factor authentication</h1>
      <p class="auth-lede">
        Two-factor sign-in is required. Add this account to your authenticator app and confirm with
        a 6-digit code.
      </p>

      <div role="status" aria-live="polite" aria-atomic="true">
        @if (loading()) {
          <div class="totp-secret">
            <p class="auth-loading">Preparing your authenticator key…</p>
          </div>
        } @else if (enrollment()) {
          <p class="visually-hidden">Authenticator QR code and setup key are ready.</p>
        }
      </div>

      @if (enrollment(); as enrollment) {
        <div class="totp-secret">
          <p class="totp-label">Scan with your authenticator app</p>
          <img
            class="totp-qr"
            [src]="enrollment.qrCodeDataUri"
            alt="QR code containing the authenticator enrollment secret"
          />
          <p class="totp-manual-label muted">Can't scan it? Enter this setup key manually:</p>
          <code class="secret-code">{{ enrollment.secretBase32 }}</code>
        </div>
      }

      <form (submit)="confirm($event)">
        <fieldset [disabled]="busy()">
          @if (error()) {
            <div class="validation-summary" role="alert">
              <span class="auth-notice-icon auth-notice-icon-danger" aria-hidden="true">
                <svg viewBox="0 0 24 24">
                  <circle cx="12" cy="12" r="9" />
                  <path d="M12 8v4M12 16h.01" />
                </svg>
              </span>
              <ul>
                <li>{{ error() }}</li>
              </ul>
            </div>
          }

          <div class="field">
            <label for="Code">Confirmation code</label>
            <input
              id="Code"
              name="Code"
              class="code-input"
              inputmode="numeric"
              autocomplete="one-time-code"
              maxlength="6"
              placeholder="000000"
              required
              appSignInFocus
              [value]="code()"
              (input)="code.set($any($event.target).value)"
            />
          </div>

          <button type="submit" class="button primary wide">Confirm enrollment</button>
        </fieldset>
      </form>
    </section>
  `,
})
export class EnrollTotpPage {
  private readonly http = inject(HttpClient);
  private readonly flow = inject(SignInFlow);
  protected readonly loading = signal(true);
  protected readonly enrollment = signal<Schemas['TotpEnrollmentResponse'] | null>(null);
  protected readonly code = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  constructor() {
    // A POST: beginning an enrollment chooses a new secret for this pending sign-in.
    this.http.post<Schemas['TotpEnrollmentResponse']>('/api/ui/auth/enrollment', null).subscribe({
      next: (enrollment) => {
        this.loading.set(false);
        this.enrollment.set(enrollment);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.error.set(this.flow.refused(error) ?? '');
      },
    });
  }

  protected confirm(event: Event): void {
    event.preventDefault();
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    const request: Schemas['AuthenticatorCodeRequest'] = {
      code: this.code(),
      returnUrl: this.flow.returnUrl(),
    };
    this.http
      .post<Schemas['SignInRedirectResponse']>('/api/ui/auth/enrollment/confirm', request)
      .subscribe({
        next: (result) => this.flow.leave(result.redirect),
        error: (error: unknown) => {
          this.busy.set(false);
          this.error.set(this.flow.refused(error) ?? '');
        },
      });
  }
}
