import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import type { components } from '../ui-api/schema.generated';
import { SignInFlow, SignInFocus } from './sign-in-flow';

type Schemas = components['schemas'];

/**
 * Break-glass recovery, the one sign-in page a blocked network can load. It makes no call but the
 * recovery itself, and says what happened only from that call's answer (ADR-0032).
 */
@Component({
  selector: 'app-recover-page',
  imports: [SignInFocus],
  host: { class: 'auth-page' },
  template: `
    <section class="auth-card">
      <span class="auth-context-pill">
        <svg
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.8"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <path d="M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6z" />
          <path d="M12 8v4M12 16h.01" />
        </svg>
        <span>Break-glass recovery</span>
      </span>
      <h1>Recover access</h1>
      <div role="status" aria-live="polite" aria-atomic="true">
        @if (recovered(); as result) {
          <div class="auth-notice auth-notice-ok">
            <span class="auth-notice-icon auth-notice-icon-ok" aria-hidden="true">
              <svg viewBox="0 0 24 24">
                <circle cx="12" cy="12" r="9" />
                <path d="m9 12 2 2 4-4" />
              </svg>
            </span>
            <p id="recover-outcome" class="flash-success">
              {{
                result.blockLifted
                  ? 'The intrusion-defence block on your network was lifted and your session restored.'
                  : 'Access restored. Your network was not blocked.'
              }}
            </p>
          </div>
        }
      </div>
      @if (recovered(); as result) {
        <button
          type="button"
          class="button primary wide"
          aria-describedby="recover-outcome"
          appSignInFocus
          (click)="flow.leave('/')"
        >
          Continue to the vault
        </button>
      } @else {
        <p class="auth-lede">
          Administrator break-glass recovery. Enter all of your credentials in one step. If they
          verify and your account holds Top Secret clearance, the intrusion-defence block on your
          network is lifted and your session restored.
        </p>

        <form (submit)="recover($event)">
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
              <label for="Username">Username</label>
              <input
                id="Username"
                name="Username"
                autocomplete="username"
                maxlength="64"
                required
                appSignInFocus
                [value]="username()"
                (input)="username.set($any($event.target).value)"
              />
            </div>

            <div class="field">
              <label for="Password">Password</label>
              <input
                id="Password"
                name="Password"
                type="password"
                autocomplete="current-password"
                maxlength="256"
                required
                [value]="password()"
                (input)="password.set($any($event.target).value)"
              />
            </div>

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
                [value]="code()"
                (input)="code.set($any($event.target).value)"
              />
            </div>

            <button type="submit" class="button primary wide">Verify and restore access</button>
            <p class="auth-commit-note muted">
              Recovery and any lifted block are saved to the audit log.
            </p>
          </fieldset>
        </form>

        <p class="footnote">
          Not an administrator? Ask one to lift the block from Administration → Networks.
        </p>
      }
    </section>
  `,
})
export class RecoverPage {
  private readonly http = inject(HttpClient);
  protected readonly flow = inject(SignInFlow);
  protected readonly username = signal('');
  protected readonly password = signal('');
  protected readonly code = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly recovered = signal<Schemas['RecoverResponse'] | null>(null);

  protected recover(event: Event): void {
    event.preventDefault();
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    const request: Schemas['RecoverRequest'] = {
      username: this.username(),
      password: this.password(),
      code: this.code(),
    };
    this.http.post<Schemas['RecoverResponse']>('/api/ui/auth/recover', request).subscribe({
      next: (result) => this.recovered.set(result),
      // The vault's own sentence: a recorded sign-in whose lift did not settle says so, and claims
      // no access restored.
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(this.flow.refused(error) ?? '');
      },
    });
  }
}
