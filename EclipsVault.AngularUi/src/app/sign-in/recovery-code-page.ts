import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import type { components } from '../ui-api/schema.generated';
import { SignInFlow, SignInFocus } from './sign-in-flow';

type Schemas = components['schemas'];

@Component({
  selector: 'app-recovery-code-page',
  imports: [RouterLink, SignInFocus],
  host: { class: 'auth-page' },
  template: `
    <section class="auth-card">
      <div class="auth-step">
        <div class="step-track" aria-hidden="true">
          <span class="step done"></span>
          <span class="step" [class.done]="signedIn()" [class.active]="!signedIn()"></span>
        </div>
        <span class="auth-step-label">{{ signedIn() ? 'Signed in' : 'Step 2 of 2' }}</span>
      </div>
      <h1>Use a recovery code</h1>
      <div role="status" aria-live="polite" aria-atomic="true">
        @if (signedIn(); as result) {
          <!-- Said here, before leaving: the vault has already issued the session cookie (ADR-0032). -->
          <div class="auth-notice auth-notice-info">
            <span class="auth-notice-icon auth-notice-icon-info" aria-hidden="true">
              <svg viewBox="0 0 24 24">
                <circle cx="12" cy="12" r="9" />
                <path d="M12 16v-4M12 8h.01" />
              </svg>
            </span>
            <p id="recovery-code-outcome" class="flash-info">
              You signed in with a recovery code — that code is now used up.
              {{ remaining(result.codesRemaining) }} Generate a new set from your profile if you're
              running low.
            </p>
          </div>
        }
      </div>
      @if (signedIn(); as result) {
        <button
          type="button"
          class="button primary wide"
          aria-describedby="recovery-code-outcome"
          appSignInFocus
          (click)="flow.leave(result.redirect)"
        >
          Continue
        </button>
      } @else {
        <p class="auth-lede">
          Enter one of the single-use recovery codes you saved when you set up MFA. Each code works
          only once.
        </p>

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
              <label for="Code">Recovery code</label>
              <input
                id="Code"
                name="Code"
                class="code-input code-input-recovery"
                autocomplete="one-time-code"
                autocapitalize="characters"
                spellcheck="false"
                maxlength="16"
                placeholder="XXXXX-XXXXX"
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
          <span>Have your authenticator?</span>
          <a routerLink="/Account/Totp" [queryParams]="flow.query()"
            >Enter a 6-digit code instead</a
          >
        </p>
      }
    </section>
  `,
})
export class RecoveryCodePage {
  private readonly http = inject(HttpClient);
  protected readonly flow = inject(SignInFlow);
  protected readonly code = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly signedIn = signal<Schemas['RecoveryCodeSignInResponse'] | null>(null);

  protected remaining(count: number | string): string {
    const left = Number(count);
    return `You have ${left} recovery ${left === 1 ? 'code' : 'codes'} left.`;
  }

  protected verify(event: Event): void {
    event.preventDefault();
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    const request: Schemas['RecoveryCodeSignInRequest'] = {
      code: this.code(),
      returnUrl: this.flow.returnUrl(),
    };
    this.http
      .post<Schemas['RecoveryCodeSignInResponse']>('/api/ui/auth/recovery-code', request)
      .subscribe({
        next: (result) => this.signedIn.set(result),
        error: (error: unknown) => {
          this.busy.set(false);
          this.error.set(this.flow.refused(error) ?? '');
        },
      });
  }
}
