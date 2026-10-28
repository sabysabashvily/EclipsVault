import { DatePipe } from '@angular/common';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { Observable, switchMap } from 'rxjs';
import { UiApiError } from '../ui-api/ui-api-errors';
import { CredentialChange } from './credential-change';
import { PasskeyCreation } from './passkey-creation';
import { ProfileSession } from './profile-session';
import {
  PasskeyOptions,
  Profile,
  ProfilePasskey,
  RegisteredPasskey,
  refusalReason,
} from './profile-types';

@Component({
  selector: 'app-passkeys-panel',
  imports: [DatePipe],
  templateUrl: './passkeys-panel.html',
})
export class PasskeysPanel {
  readonly profile = input.required<Profile>();
  readonly changes = input.required<CredentialChange>();
  readonly changed = output<void>();
  private readonly session = inject(ProfileSession);
  protected readonly creation = inject(PasskeyCreation);
  protected readonly nickname = signal('');
  protected readonly prompting = signal(false);
  protected readonly locked = computed(() => this.session.busy() || this.session.blocked());
  protected readonly refusalReason = refusalReason;

  protected add(event: Event): void {
    const nickname = this.nickname().trim();
    this.changes().start<RegisteredPasskey>(
      {
        change: { kind: 'AddPasskey', passkeyNickname: nickname },
        intent: `${nickname ? `Add the passkey “${nickname}”.` : 'Add a passkey.'} Any other signed-in devices will be signed out.`,
        commit: (operationId) => this.register(operationId),
        committed: () => {
          this.nickname.set('');
          this.saved('Passkey added. You can now sign in with it.');
        },
        reconcileAfterFailure: () => this.changed.emit(),
      },
      event.currentTarget as Element,
    );
  }

  protected remove(key: ProfilePasskey, event: Event): void {
    this.changes().start<void>(
      {
        confirmation: 'Remove this passkey? You will no longer be able to sign in with it.',
        change: { kind: 'RemovePasskey', passkeyId: key.id },
        intent: `Remove the passkey “${key.nickname}”. Any other signed-in devices will be signed out.`,
        commit: (operationId) =>
          this.session.http.delete<void>(`/api/ui/profile/passkeys/${key.id}`, {
            body: { operationId },
          }),
        committed: () =>
          this.saved('Passkey removed. Any other signed-in devices have been signed out.'),
        reconcileAfterFailure: () => this.changed.emit(),
      },
      event.currentTarget as Element,
    );
  }

  private register(operationId: string): Observable<RegisteredPasskey> {
    return this.session.http
      .post<PasskeyOptions>('/api/ui/profile/passkeys/registration/begin', { operationId })
      .pipe(
        switchMap((options) => this.prompt(options.publicKey)),
        switchMap((credential) =>
          this.session.http.post<RegisteredPasskey>(
            '/api/ui/profile/passkeys/registration/complete',
            { operationId, credential },
          ),
        ),
      );
  }

  /** Leaving the page or losing the session unsubscribes, which also closes the browser's prompt. */
  private prompt(options: unknown): Observable<unknown> {
    return new Observable((subscriber) => {
      const abort = new AbortController();
      this.prompting.set(true);
      this.creation.create(options, abort.signal).then(
        (credential) => {
          this.prompting.set(false);
          subscriber.next(credential);
          subscriber.complete();
        },
        (error: unknown) => {
          this.prompting.set(false);
          subscriber.error(promptFailure(error));
        },
      );
      return () => {
        this.prompting.set(false);
        abort.abort();
      };
    });
  }

  private saved(message: string): void {
    this.session.refreshed();
    this.changed.emit();
    this.session.status.set({ message, error: false });
  }
}

/** A dismissed prompt is cancellation, not a failed proof, so it says so instead of an error. */
function promptFailure(error: unknown): UiApiError {
  if (error instanceof DOMException && ['NotAllowedError', 'AbortError'].includes(error.name))
    return new UiApiError(400, 'cancelled', 'The passkey prompt was dismissed or timed out.');
  return new UiApiError(
    400,
    'passkey_failed',
    error instanceof DOMException && error.message
      ? error.message
      : 'Something went wrong with the passkey.',
  );
}
