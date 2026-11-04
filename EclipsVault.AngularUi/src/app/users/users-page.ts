import { RouterLink } from '@angular/router';
import { HttpClient, httpResource } from '@angular/common/http';
import {
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ConfirmationDialog } from '../shared/confirmation-dialog';
import { RowMenu } from '../shared/row-menu';
import { StatusLine } from '../shared/status-line';
import { StepUpDialog } from '../shared/step-up-dialog';
import { TableFilter } from '../shared/table-filter';
import type { components } from '../ui-api/schema.generated';
import { UiApiError } from '../ui-api/ui-api-errors';

type User = components['schemas']['UserResponse'];
type Action = 'enable' | 'disable' | 'unlock' | 'reset-mfa' | 'sign-out' | 'delete';
interface Pending {
  action: Action;
  user: User;
}

const outcomes: Record<Action, string> = {
  enable: 'Account enabled.',
  disable: 'Account disabled and its sessions revoked.',
  unlock: 'Lockout cleared. The user can sign in again.',
  'reset-mfa': 'MFA was reset — the user will enroll a new authenticator at their next sign-in.',
  'sign-out': "All of the user's sessions were revoked.",
  delete: 'User deleted.',
};
const statuses: Record<User['status'], { label: string; tone: string }> = {
  active: { label: 'Active', tone: 'tone-ok' },
  locked: { label: 'Locked', tone: 'tone-warn' },
  disabled: { label: 'Disabled', tone: 'tone-danger' },
};

@Component({
  selector: 'app-users-page',
  imports: [RouterLink, ConfirmationDialog, RowMenu, StatusLine, StepUpDialog, TableFilter],
  templateUrl: './users-page.html',
})
export class UsersPage {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);
  private readonly confirmation = viewChild.required(ConfirmationDialog);
  private readonly stepUp = viewChild.required(StepUpDialog);
  private awaitingConfirmation: Pending | null = null;
  private awaitingStepUp: Pending | null = null;
  protected readonly users = httpResource<components['schemas']['UsersResponse']>(
    () => '/api/ui/users',
  );
  protected readonly query = signal('');
  protected readonly busy = signal(false);
  protected readonly status = signal({ message: '', error: false });
  protected readonly statuses = statuses;
  protected readonly filtered = computed(() => {
    const accounts = this.users.hasValue() ? this.users.value().users : [];
    const query = this.query().trim().toLowerCase();
    return accounts.filter((account) =>
      [
        account.displayName,
        account.username,
        account.email,
        account.clearanceLabel,
        account.project,
        account.mfaEnrolled ? 'Enrolled' : 'Pending',
        statuses[account.status].label,
      ]
        .join(' ')
        .toLowerCase()
        .includes(query),
    );
  });

  constructor() {
    this.http
      .get<components['schemas']['UserNoticeResponse']>('/api/ui/users/notice')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (notice) => {
          if (notice.message) this.status.set({ message: notice.message, error: notice.error });
        },
        error: () =>
          this.status.set({
            message: 'Could not load the account notice. Reload the page to try again.',
            error: true,
          }),
      });
  }

  protected act(action: Action, user: User): void {
    if (this.busy()) return;
    switch (action) {
      case 'disable':
        this.ask(
          action,
          user,
          `Disable '${user.username}'? They will be signed out immediately and cannot sign in until re-enabled.`,
        );
        break;
      case 'reset-mfa':
        this.ask(
          action,
          user,
          `Reset MFA for '${user.username}'? They will re-enroll at next sign-in.`,
        );
        break;
      case 'sign-out':
        this.ask(action, user, `Revoke all active sessions for '${user.username}'?`);
        break;
      case 'delete':
        this.ask(action, user, `Delete user '${user.username}' permanently? This is audited.`);
        break;
      case 'enable':
      case 'unlock':
        this.send({ action, user }, true);
        break;
      default: {
        const exhaustive: never = action;
        return exhaustive;
      }
    }
  }

  private ask(action: Action, user: User, message: string): void {
    this.awaitingConfirmation = { action, user };
    this.confirmation().open(message);
  }

  protected confirmed(confirmed: boolean): void {
    const pending = this.awaitingConfirmation;
    this.awaitingConfirmation = null;
    if (confirmed && pending) this.send(pending, true);
  }

  protected steppedUp(verified: boolean): void {
    const pending = this.awaitingStepUp;
    this.awaitingStepUp = null;
    if (verified && pending) this.send(pending, false);
  }

  private send(pending: Pending, allowStepUp: boolean): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.status.set({ message: '', error: false });
    const path = `/api/ui/users/${pending.user.userId}`;
    const write =
      pending.action === 'delete'
        ? this.http.delete<void>(path)
        : this.http.post<void>(`${path}/${pending.action}`, null);
    write
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => {
          if (pending.action === 'sign-out' && pending.user.isYou) {
            this.document.location.assign('/Account/Login');
            return;
          }
          this.status.set({ message: outcomes[pending.action], error: false });
          this.users.reload();
        },
        error: (error: unknown) => {
          if (
            error instanceof UiApiError &&
            error.code === 'step_up_required' &&
            error.proof &&
            allowStepUp
          ) {
            this.awaitingStepUp = pending;
            this.stepUp().open(error.proof);
          } else {
            this.status.set({
              message:
                error instanceof UiApiError
                  ? error.title
                  : 'Could not update the account. Try again.',
              error: true,
            });
            if (
              pending.action === 'sign-out' &&
              pending.user.isYou &&
              error instanceof UiApiError &&
              error.code === 'revoked_unrecorded'
            ) {
              this.document.location.assign('/Account/Login');
              return;
            }
          }
          this.users.reload();
        },
      });
  }
}
