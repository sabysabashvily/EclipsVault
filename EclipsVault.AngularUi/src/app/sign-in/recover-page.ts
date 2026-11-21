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
      <h1>Recover access</h1>
      @if (recovered(); as result) {
        <p class="flash flash-success" role="status">
          {{
            result.blockLifted
              ? 'The intrusion-defence block on your network was lifted and your session restored.'
              : 'Access restored. Your network was not blocked.'
          }}
        </p>
        <button type="button" class="button primary wide" appSignInFocus (click)="flow.leave('/')">
          Continue to the vault
        </button>
      } @else {
        <p class="muted">
          Administrator break-glass recovery. Enter all of your credentials in one step — if they
          verify and your account holds TopSecret clearance, the intrusion-defence block on your
          network is lifted and your session restored. Every attempt is audited.
        </p>

        <form (submit)="recover($event)">
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
                required
                [value]="code()"
                (input)="code.set($any($event.target).value)"
              />
            </div>

            <button type="submit" class="button primary wide">Verify and restore access</button>
          </fieldset>
        </form>

        <p class="footnote">
          Not an administrator? Ask one to lift the block from Admin → Networks, or restart the
          application in development.
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
