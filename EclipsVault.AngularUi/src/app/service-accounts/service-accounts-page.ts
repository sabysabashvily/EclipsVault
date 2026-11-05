import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { StatusLine } from '../shared/status-line';
import type { components } from '../ui-api/schema.generated';
import { ServiceAccountNotices } from './service-account-notices';

@Component({
  selector: 'app-service-accounts-page',
  imports: [DatePipe, RouterLink, StatusLine],
  templateUrl: './service-accounts-page.html',
})
export class ServiceAccountsPage {
  protected readonly accounts = httpResource<components['schemas']['ServiceAccountsResponse']>(
    () => '/api/ui/service-accounts',
  );
  protected readonly message: string;

  constructor() {
    const notices = inject(ServiceAccountNotices);
    this.message = notices.deleted ? 'Service account and its keys deleted.' : '';
    notices.deleted = false;
  }
}
