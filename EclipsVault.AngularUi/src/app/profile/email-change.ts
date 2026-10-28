import { DestroyRef, Component, inject, input, output, signal, viewChild } from '@angular/core';
import { ProfileSession } from './profile-session';
import { VerificationDialog } from './verification-dialog';
import type { PendingEmail, PreparedOperation, Profile, VerifiedOperation } from './profile-types';

@Component({
  selector: 'app-email-change',
  imports: [VerificationDialog],
  templateUrl: './email-change.html',
})
export class EmailChange {
  readonly profile = input.required<Profile>();
  readonly changed = output<void>();
  protected readonly session = inject(ProfileSession);
  private readonly verification = viewChild.required(VerificationDialog);
  protected readonly address = signal('');
  protected readonly code = signal('');
  protected readonly awaitingProof = signal(false);
  private action: 'request' | 'resend' | 'confirm' | null = null;

  constructor() {
    this.session.onClear(() => this.clear(), inject(DestroyRef));
  }

  protected start(event: Event): void {
    event.preventDefault();
    if (!this.profile().changes.changeEmail.available || this.awaitingProof()) return;
    const opener = (event as SubmitEvent).submitter;
    const address = this.address().trim();
    this.session.write(
      this.session.http.post<PreparedOperation>('/api/ui/profile/operations', {
        kind: 'ChangeEmail',
        newEmail: address,
      }),
      (operation) => {
        this.action = 'request';
        this.awaitingProof.set(true);
        this.verification().open(
          operation,
          `Change email to ${address}. Your current email stays active until you confirm the new mailbox.`,
          { opener },
        );
      },
      () => this.changed.emit(),
    );
  }

  protected reverify(action: 'resend' | 'confirm'): void {
    const pending = this.profile().pendingEmail;
    if (!pending || this.session.busy() || this.awaitingProof()) return;
    this.action = action;
    this.awaitingProof.set(true);
    this.verification().open(
      {
        operationId: pending.operationId,
        kind: 'ChangeEmail',
        expiresAtUtc: pending.expiresAtUtc,
        methods: this.profile().verificationMethods,
      },
      `Change email to ${pending.address}.`,
      { cancelOperation: false },
    );
  }

  protected verified(result: VerifiedOperation): void {
    const action = this.action;
    this.action = null;
    this.awaitingProof.set(false);
    if (action === 'confirm') {
      // Re-verification authorizes the frozen address; the user deliberately submits the mailbox code afterwards.
      this.session.status.set({
        message: 'Account verified. Enter the mailbox code and confirm your email.',
        error: false,
      });
      return;
    }
    if (!action) return;
    this.session.write(
      this.session.http.post<PendingEmail>(`/api/ui/profile/email/${action}`, {
        operationId: result.operationId,
      }),
      () => {
        this.address.set('');
        this.code.set('');
        this.session.status.set({
          message: 'A confirmation code was sent. Your current email is still active.',
          error: false,
        });
        this.changed.emit();
      },
      () => this.changed.emit(),
    );
  }

  protected confirm(event: Event): void {
    event.preventDefault();
    const pending = this.profile().pendingEmail;
    if (!pending || this.awaitingProof()) return;
    const code = this.code();
    this.code.set('');
    this.session.write(
      this.session.http.post<void>('/api/ui/profile/email/confirm', {
        operationId: pending.operationId,
        code,
      }),
      () => {
        this.session.refreshed();
        this.session.status.set({
          message: 'Your email was changed. Any other signed-in devices have been signed out.',
          error: false,
        });
        this.changed.emit();
      },
      () => this.changed.emit(),
    );
  }

  protected cancelPending(): void {
    const pending = this.profile().pendingEmail;
    if (!pending || this.awaitingProof()) return;
    this.code.set('');
    this.session.write(
      this.session.http.delete<void>('/api/ui/profile/email/pending', {
        body: { operationId: pending.operationId },
      }),
      () => {
        this.session.status.set({
          message: 'Email change cancelled. Your current email is still active.',
          error: false,
        });
        this.changed.emit();
      },
      () => this.changed.emit(),
    );
  }

  protected clear(): void {
    this.action = null;
    this.awaitingProof.set(false);
    this.address.set('');
    this.code.set('');
  }
}
