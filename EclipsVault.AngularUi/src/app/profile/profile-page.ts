import { ActivatedRoute, RouterLink } from '@angular/router';
import { httpResource } from '@angular/common/http';
import { DestroyRef, Component, inject, signal, viewChild } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ConfirmationDialog } from '../shared/confirmation-dialog';
import { PAGE_LOCATION } from '../shell/command-palette';
import { StatusLine } from '../shared/status-line';
import { UiApiError } from '../ui-api/ui-api-errors';
import { CredentialChange } from './credential-change';
import { EmailChange } from './email-change';
import { PasskeysPanel } from './passkeys-panel';
import { ProfileWrites } from './profile-writes';
import { NewRecoveryCodes, Profile, refusalReason } from './profile-types';
import { RecoveryCodes } from './recovery-codes';

/** The server's upload limit, checked here so an oversized file is refused before it is sent. */
const maxAvatarBytes = 4 * 1024 * 1024;

@Component({
  selector: 'app-profile-page',
  imports: [
    RouterLink,
    ConfirmationDialog,
    StatusLine,
    EmailChange,
    CredentialChange,
    PasskeysPanel,
    RecoveryCodes,
  ],
  providers: [ProfileWrites],
  templateUrl: './profile-page.html',
})
export class ProfilePage {
  protected readonly session = inject(ProfileWrites);
  private readonly location = inject(PAGE_LOCATION);
  protected readonly profile = httpResource<Profile>(() => '/api/ui/profile');
  protected readonly refusalReason = refusalReason;
  private readonly confirmation = viewChild.required(ConfirmationDialog);
  private readonly changes = viewChild.required(CredentialChange);
  private action: 'avatar' | 'sessions' | null = null;
  protected readonly confirming = signal(false);
  /** The only copy of a new recovery-code set, dropped on Done and whenever the page is left. */
  protected readonly recoveryCodes = signal<readonly string[] | null>(null);

  constructor() {
    if (inject(ActivatedRoute).snapshot.queryParamMap.get('recoveryCodes') === 'display-once') {
      this.session.status.set({
        message: 'Recovery codes are shown only once, right after you generate them.',
        error: false,
      });
    }
    this.session.onClear(() => this.recoveryCodes.set(null), inject(DestroyRef));
  }

  protected saveName(event: Event, displayName: string): void {
    event.preventDefault();
    this.session.write(this.session.http.patch('/api/ui/profile/details', { displayName }), () => {
      this.session.refreshed();
      this.profile.reload();
      this.session.status.set({ message: 'Your profile was updated.', error: false });
    });
  }

  protected upload(input: HTMLInputElement): void {
    const avatar = input.files?.[0];
    input.value = '';
    if (!avatar) return;
    if (avatar.size > maxAvatarBytes) {
      this.session.status.set({ message: 'Images must be 4 MB or smaller.', error: true });
      return;
    }
    const body = new FormData();
    body.append('avatar', avatar);
    this.session.write(this.session.http.post('/api/ui/profile/avatar', body), () =>
      this.avatarSaved('Your profile picture was updated.'),
    );
  }

  protected ask(action: 'avatar' | 'sessions'): void {
    if (this.session.busy() || this.session.blocked() || this.confirming()) return;
    this.action = action;
    this.confirming.set(true);
    this.confirmation().open(
      action === 'avatar'
        ? 'Remove your profile picture and go back to the generated avatar?'
        : "Sign out of all sessions now? You'll need to sign in again.",
    );
  }

  protected decide(confirmed: boolean): void {
    const action = this.action;
    this.action = null;
    this.confirming.set(false);
    if (!confirmed) return;
    if (action === 'sessions') this.session.signOutEverywhere();
    if (action === 'avatar')
      this.session.write(this.session.http.delete('/api/ui/profile/avatar'), () =>
        this.avatarSaved('Your profile picture was removed.'),
      );
  }

  /** Reset ends every session, this one included, so sign-in decides whether to enroll again. */
  protected resetAuthenticator(event: Event): void {
    this.changes().start<void>(
      {
        confirmation:
          "Reset your authenticator? You'll be signed out and set up a new one at next sign-in.",
        change: { kind: 'ResetAuthenticator' },
        intent:
          'Reset your authenticator and recovery codes. Every session, including this one, will be signed out.',
        commit: (operationId) =>
          this.session.http.post<void>('/api/ui/profile/authenticator/reset', { operationId }),
        committed: () => {
          this.session.clear();
          this.location.assign('/Account/Login');
        },
        reconcileAfterFailure: () => this.profile.reload(),
      },
      event.currentTarget as Element,
    );
  }

  protected replaceRecoveryCodes(event: Event): void {
    this.changes().start<NewRecoveryCodes>(
      {
        confirmation:
          'Generate a new set of recovery codes? Any codes you already have will immediately stop working.',
        change: { kind: 'ReplaceRecoveryCodes' },
        intent:
          'Generate new recovery codes. Your current codes stop working, and any other signed-in devices will be signed out.',
        lostReply:
          'The response was lost, so no new codes can be shown. If a new set was generated, your old codes no longer work. Check your account or sign in, then deliberately generate a new set.',
        commit: (operationId) =>
          this.session.http
            .post<NewRecoveryCodes>('/api/ui/profile/recovery-codes', { operationId })
            .pipe(
              catchError((error: unknown) => throwError(() => explainRecoveryCodeFailure(error))),
            ),
        committed: ({ codes }) => {
          this.recoveryCodes.set(codes);
          this.session.refreshed();
          this.profile.reload();
        },
        reconcileAfterFailure: () => this.profile.reload(),
      },
      event.currentTarget as Element,
    );
  }

  protected recoveryCodesSaved(): void {
    this.recoveryCodes.set(null);
  }

  private avatarSaved(message: string): void {
    this.session.refreshed();
    this.profile.reload();
    this.session.status.set({ message, error: false });
  }
}

/** A set saved without a session to show it on stays unseen; only a deliberate new set shows codes. */
function explainRecoveryCodeFailure(error: unknown): unknown {
  return error instanceof UiApiError && error.code === 'changed_sign_in_required'
    ? new UiApiError(
        error.status,
        error.code,
        'New recovery codes were generated. Sign in and deliberately generate a new set to see codes.',
      )
    : error;
}
