import { HttpClient, httpResource } from '@angular/common/http';
import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { StatusLine } from '../shared/status-line';
import { StepUpDialog } from '../shared/step-up-dialog';
import type { components } from '../ui-api/schema.generated';
import { AdministratorWrites } from '../shared/administrator-writes';

const standardClearance = 1;

type CreatedUser = components['schemas']['CreatedUserResponse'];

@Component({
  selector: 'app-new-user-page',
  imports: [RouterLink, StatusLine, StepUpDialog],
  providers: [AdministratorWrites],
  templateUrl: './new-user-page.html',
})
export class NewUserPage {
  private readonly http = inject(HttpClient);
  private readonly submitButton = viewChild.required<ElementRef<HTMLButtonElement>>('submitButton');
  private readonly stepUp = viewChild.required(StepUpDialog);
  protected readonly writes = inject(AdministratorWrites);
  protected readonly metadata = httpResource<components['schemas']['NewUserResponse']>(
    () => '/api/ui/users/new',
  );
  protected readonly firstName = signal('');
  protected readonly lastName = signal('');
  protected readonly username = signal('');
  protected readonly email = signal('');
  protected readonly project = signal('');
  protected readonly clearance = signal(standardClearance);
  protected readonly created = signal<CreatedUser | null>(null);

  protected error(field: string): string {
    return this.writes.errors()[field]?.[0] ?? '';
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (!this.metadata.hasValue()) return;
    const request: components['schemas']['CreateUserUiRequest'] = {
      firstName: this.firstName(),
      lastName: this.lastName(),
      username: this.username(),
      email: this.email(),
      clearance: this.clearance(),
      project: this.project(),
    };
    this.writes.send(
      () => this.http.post<CreatedUser>('/api/ui/users', request),
      (created) => {
        this.created.set(created);
        this.writes.status.set(
          created.invitation === 'sent'
            ? {
                message: `Invitation sent to ${created.email}. It expires in 72 hours.`,
                error: false,
              }
            : {
                message:
                  "The account was created, but the invitation could not be delivered. Resend it from the user's page.",
                error: true,
              },
        );
      },
      (proof) => this.stepUp().open(proof),
    );
  }

  protected verified(verified: boolean): void {
    this.writes.verified(verified, this.submitButton().nativeElement);
  }
}
