import { httpResource } from '@angular/common/http';
import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { Subscription, takeUntil } from 'rxjs';
import { StatusLine } from '../shared/status-line';
import { passwordStrength } from './password-strength';
import { ProfileSession } from './profile-session';
import type { components } from '../ui-api/schema.generated';
import { PreparedOperation, Profile, VerifiedOperation, refusalReason } from './profile-types';
import { VerificationDialog } from './verification-dialog';

@Component({
  selector: 'app-change-password-page',
  imports: [StatusLine, VerificationDialog],
  providers: [ProfileSession],
  templateUrl: './change-password-page.html',
})
export class ChangePasswordPage {
  protected readonly session = inject(ProfileSession);
  protected readonly profile = httpResource<Profile>(() => '/api/ui/profile');
  private readonly verification = viewChild.required(VerificationDialog);
  protected readonly currentPassword = signal('');
  protected readonly newPassword = signal('');
  protected readonly confirmation = signal('');
  protected readonly shown = signal(false);
  protected readonly awaitingProof = signal(false);
  protected readonly breach = signal<boolean | null>(null);
  protected readonly strength = computed(() => passwordStrength(this.newPassword()));
  protected readonly refusalReason = refusalReason;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private preview: Subscription | undefined;

  constructor() {
    this.session.onClear(() => this.clear());
  }

  protected candidateChanged(value: string): void {
    clearTimeout(this.timer);
    this.preview?.unsubscribe();
    this.newPassword.set(value);
    this.breach.set(null);
    if (value.length < 12 || value.length > 256) return;
    this.timer = setTimeout(() => {
      this.preview = this.session.http
        .post<components['schemas']['PasswordCheckResponse']>('/api/ui/passwords/check-breached', {
          password: value,
        })
        .pipe(takeUntil(this.session.ended))
        .subscribe({
          next: (result) => this.breach.set(result.compromised),
          error: () => undefined,
        });
    }, 400);
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (
      this.awaitingProof() ||
      !this.profile.hasValue() ||
      !this.profile.value().changes.changePassword.available
    )
      return;
    const opener = (event as SubmitEvent).submitter;
    const candidate = this.newPassword();
    let message = '';
    if (candidate.length < 12 || candidate.length > 256)
      message = 'Your new password must be 12–256 characters.';
    else if (candidate !== this.confirmation()) message = 'The passwords do not match.';
    else if (candidate === this.currentPassword())
      message = 'Choose a different password from your current one.';
    else if (this.breach())
      message = 'This password has appeared in known data breaches — choose a different one.';
    if (message) {
      this.session.status.set({ message, error: true });
      return;
    }
    this.session.write(
      this.session.http.post<PreparedOperation>('/api/ui/profile/operations', {
        kind: 'ChangePassword',
        newPassword: candidate,
      }),
      (operation) => {
        const current = this.currentPassword();
        this.clear();
        this.awaitingProof.set(true);
        this.verification().open(
          operation,
          'Change your password. Any other signed-in devices will be signed out.',
          { currentPassword: current, needsPassword: true, opener },
        );
      },
    );
  }

  protected commit(result: VerifiedOperation): void {
    this.awaitingProof.set(false);
    this.session.write(
      this.session.http.post<void>('/api/ui/profile/password', { operationId: result.operationId }),
      () => {
        this.clear();
        this.session.refreshed();
        this.profile.reload();
        this.session.status.set({
          message: 'Your password was changed. Any other signed-in devices have been signed out.',
          error: false,
        });
      },
    );
  }

  protected clear(): void {
    clearTimeout(this.timer);
    this.preview?.unsubscribe();
    this.currentPassword.set('');
    this.newPassword.set('');
    this.confirmation.set('');
    this.breach.set(null);
    this.shown.set(false);
    this.awaitingProof.set(false);
  }
}
