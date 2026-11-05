import { HttpClient, httpResource } from '@angular/common/http';
import { Component, ElementRef, effect, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { PAGE_LOCATION } from '../shell/command-palette';
import { StatusLine } from '../shared/status-line';
import { StepUpDialog } from '../shared/step-up-dialog';
import type { components } from '../ui-api/schema.generated';
import { UiApiError } from '../ui-api/ui-api-errors';
import { AdministratorWrites } from '../shared/administrator-writes';

@Component({
  selector: 'app-edit-user-page',
  imports: [RouterLink, StatusLine, StepUpDialog],
  providers: [AdministratorWrites],
  templateUrl: './edit-user-page.html',
})
export class EditUserPage {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly location = inject(PAGE_LOCATION);
  private readonly title = inject(Title);
  private readonly submitButton = viewChild.required<ElementRef<HTMLButtonElement>>('submitButton');
  private readonly stepUp = viewChild.required(StepUpDialog);
  private readonly userId = inject(ActivatedRoute).snapshot.paramMap.get('id');
  protected readonly writes = inject(AdministratorWrites);
  protected readonly account = httpResource<components['schemas']['UserDetailsResponse']>(
    () => `/api/ui/users/${this.userId}`,
  );
  protected readonly clearance = signal(1);
  protected readonly project = signal('');

  constructor() {
    effect(() => {
      if (this.account.hasValue()) {
        const account = this.account.value();
        this.clearance.set(Number(account.clearance));
        this.project.set(account.project);
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
    this.writes.send(
      () => this.http.patch<void>(`/api/ui/users/${account.userId}/role`, request),
      () => {
        if (account.isYou) this.location.assign('/Admin/Users');
        else void this.router.navigateByUrl('/Admin/Users');
      },
      (proof) => this.stepUp().open(proof),
    );
  }

  protected verified(verified: boolean): void {
    this.writes.verified(verified, this.submitButton().nativeElement);
  }
}
