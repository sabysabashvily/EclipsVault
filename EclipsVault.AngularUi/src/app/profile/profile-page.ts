import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, inject, signal, viewChild } from '@angular/core';
import { ConfirmationDialog } from '../shared/confirmation-dialog';
import { StatusLine } from '../shared/status-line';
import { EmailChange } from './email-change';
import { ProfileSession } from './profile-session';
import { Profile, refusalReason } from './profile-types';

@Component({
  selector: 'app-profile-page',
  imports: [DatePipe, ConfirmationDialog, StatusLine, EmailChange],
  providers: [ProfileSession],
  templateUrl: './profile-page.html',
})
export class ProfilePage {
  protected readonly session = inject(ProfileSession);
  protected readonly profile = httpResource<Profile>(() => '/api/ui/profile');
  protected readonly refusalReason = refusalReason;
  private readonly confirmation = viewChild.required(ConfirmationDialog);
  private action: 'avatar' | 'sessions' | null = null;
  protected readonly confirming = signal(false);

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
    if (avatar.size > 4 * 1024 * 1024) {
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

  private avatarSaved(message: string): void {
    this.session.refreshed();
    this.profile.reload();
    this.session.status.set({ message, error: false });
  }
}
