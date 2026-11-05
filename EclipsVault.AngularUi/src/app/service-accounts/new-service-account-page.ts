import { HttpClient, httpResource } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AdministratorWrites } from '../shared/administrator-writes';
import { StatusLine } from '../shared/status-line';
import type { components } from '../ui-api/schema.generated';
import { ServiceAccountNotices } from './service-account-notices';

const standardClearance = 1;

@Component({
  selector: 'app-new-service-account-page',
  imports: [RouterLink, StatusLine],
  providers: [AdministratorWrites],
  templateUrl: './new-service-account-page.html',
})
export class NewServiceAccountPage {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly notices = inject(ServiceAccountNotices);
  protected readonly writes = inject(AdministratorWrites);
  protected readonly metadata = httpResource<components['schemas']['NewServiceAccountResponse']>(
    () => '/api/ui/service-accounts/new',
  );
  protected readonly name = signal('');
  protected readonly project = signal('');
  protected readonly clearance = signal(standardClearance);

  protected error(field: string): string {
    return this.writes.errors()[field]?.[0] ?? '';
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (!this.metadata.hasValue()) return;
    const request: components['schemas']['CreateServiceAccountUiRequest'] = {
      name: this.name(),
      clearance: this.clearance(),
      project: this.project(),
    };
    this.writes.send(
      () =>
        this.http.post<components['schemas']['CreatedServiceAccountResponse']>(
          '/api/ui/service-accounts',
          request,
        ),
      (created) => {
        this.notices.createdId = created.serviceAccountId;
        void this.router.navigateByUrl(`/ServiceAccounts/Details/${created.serviceAccountId}`);
      },
      () => undefined,
    );
  }
}
