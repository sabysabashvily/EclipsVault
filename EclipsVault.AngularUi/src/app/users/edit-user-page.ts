import { HttpClient, httpResource } from '@angular/common/http';
import { Component, ElementRef, effect, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { PAGE_LOCATION } from '../shell/command-palette';
import { ConfirmationDialog } from '../shared/confirmation-dialog';
import { StatusLine } from '../shared/status-line';
import { StepUpDialog } from '../shared/step-up-dialog';
import type { components } from '../ui-api/schema.generated';
import { UiApiError } from '../ui-api/ui-api-errors';
import { AdministratorWrites } from '../shared/administrator-writes';
import { accountStatus, resendOutcome, resendQuestion } from './account-status';

type Details = components['schemas']['UserDetailsResponse'];

@Component({
  selector: 'app-edit-user-page',
  imports: [RouterLink, ConfirmationDialog, StatusLine, StepUpDialog],
  providers: [AdministratorWrites],
  templateUrl: './edit-user-page.html',
})
export class EditUserPage {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly location = inject(PAGE_LOCATION);
  private readonly title = inject(Title);
  private readonly submitButton = viewChild.required<ElementRef<HTMLButtonElement>>('submitButton');
  private readonly confirmation = viewChild.required(ConfirmationDialog);
  private readonly stepUp = viewChild.required(StepUpDialog);
  private readonly userId = inject(ActivatedRoute).snapshot.paramMap.get('id');
  /** The invitation write the open confirmation is about, and the button focus returns to. */
  private awaiting: { write: () => void; opener: HTMLButtonElement } | null = null;
  private opener: HTMLButtonElement | null = null;
  protected readonly writes = inject(AdministratorWrites);
  protected readonly accountStatus = accountStatus;
  protected readonly account = httpResource<Details>(() => `/api/ui/users/${this.userId}`);
  protected readonly clearance = signal(1);
  protected readonly project = signal('');
  protected readonly address = signal('');

  constructor() {
    effect(() => {
      if (this.account.hasValue()) {
        const account = this.account.value();
        this.clearance.set(Number(account.clearance));
        this.project.set(account.project);
        this.address.set(account.email);
        this.title.setTitle(`Edit ${account.username} — EclipsVault`);
      }
    });
  }

  protected loadError(): string {
    const error = this.account.error();
    return error instanceof UiApiError && error.code === 'user_not_found'
      ? 'User not found.'
      : 'Could not load user. Try again.';
  }

  protected error(field: string): string {
    return this.writes.errors()[field]?.[0] ?? '';
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (!this.account.hasValue()) return;
    const account = this.account.value();
    const request: components['schemas']['UserRoleRequest'] = {
      clearance: this.clearance(),
      project: this.project(),
    };
    this.opener = this.submitButton().nativeElement;
    this.writes.send(
      () => this.http.patch<void>(`/api/ui/users/${account.userId}/role`, request),
      () => {
        if (account.isYou) this.location.assign('/Admin/Users');
        else void this.router.navigateByUrl('/Admin/Users');
      },
      (proof) => this.stepUp().open(proof),
    );
  }

  protected askResend(opener: HTMLButtonElement): void {
    if (!this.account.hasValue()) return;
    const account = this.account.value();
    const question = resendQuestion(account);
    this.awaiting = {
      opener,
      write: () =>
        this.writes.send(
          () =>
            this.http.post<components['schemas']['InvitationResponse']>(
              `/api/ui/users/${account.userId}/invitation`,
              null,
            ),
          (sent) => {
            this.writes.status.set(resendOutcome(sent, account.email));
            this.account.reload();
          },
          (proof) => this.stepUp().open(proof),
        ),
    };
    this.confirmation().open(question.message, question.options);
  }

  /** Replacing the address sends nothing: the link already sent dies, and Resend mails the new one. */
  protected askReplace(event: Event, opener: HTMLButtonElement): void {
    event.preventDefault();
    if (!this.account.hasValue()) return;
    const account = this.account.value();
    const email = this.address().trim();
    const request: components['schemas']['ReplaceInvitationAddressRequest'] = { email };
    this.awaiting = {
      opener,
      write: () =>
        this.writes.send(
          () =>
            this.http.post<components['schemas']['InvitationAddressResponse']>(
              `/api/ui/users/${account.userId}/invitation-address`,
              request,
            ),
          (replaced) => {
            this.writes.status.set({
              message: `The invitation address is now ${replaced.email}. Resend the invitation to send a link there.`,
              error: false,
            });
            this.account.reload();
          },
          (proof) => this.stepUp().open(proof),
        ),
    };
    this.confirmation().open(
      `Use ${email} for ${account.username}? The link already sent stops working. Resend to send a new one there.`,
      {
        title: 'Replace the invitation address?',
        confirm: 'Replace address',
        tone: 'primary',
        note: 'Saved to the audit log',
      },
    );
  }

  protected confirmed(confirmed: boolean): void {
    const pending = this.awaiting;
    this.awaiting = null;
    if (!pending) return;
    this.opener = pending.opener;
    if (confirmed) pending.write();
  }

  protected verified(verified: boolean): void {
    this.writes.verified(verified, this.opener ?? this.submitButton().nativeElement);
  }
}
