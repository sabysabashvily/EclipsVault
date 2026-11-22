import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { components } from '../ui-api/schema.generated';
import { SignInFlow, SignInFocus } from './sign-in-flow';

type Schemas = components['schemas'];

@Component({
  selector: 'app-totp-page',
  imports: [RouterLink, SignInFocus],
  host: { class: 'auth-page' },
  template: `
    <section class="auth-card">
      <div class="auth-step">
        <div class="step-track" aria-hidden="true">
          <span class="step done"></span><span class="step active"></span>
        </div>
        <span class="auth-step-label">Step 2 of 2</span>
      </div>
      <h1>Two-factor verification</h1>
      <p class="auth-lede">Enter the 6-digit code from your authenticator app.</p>

      <form (submit)="verify($event)">
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
            <label for="Code">Authenticator code</label>
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

          <button type="submit" class="button primary wide">Verify and sign in</button>
        </fieldset>
      </form>

      <p class="footnote auth-alt-action">
        <span>Lost your authenticator?</span>
        <a routerLink="/Account/RecoveryCode" [queryParams]="flow.query()">Use a recovery code</a>
      </p>
    </section>
  `,
})
export class TotpPage {
  private readonly http = inject(HttpClient);
  protected readonly flow = inject(SignInFlow);
  protected readonly code = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected verify(event: Event): void {
    event.preventDefault();
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    const request: Schemas['AuthenticatorCodeRequest'] = {
      code: this.code(),
      returnUrl: this.flow.returnUrl(),
    };
    this.http.post<Schemas['SignInRedirectResponse']>('/api/ui/auth/totp', request).subscribe({
      next: (result) => this.flow.leave(result.redirect),
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(this.flow.refused(error) ?? '');
      },
    });
  }
}
