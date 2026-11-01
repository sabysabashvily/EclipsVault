import { formatDate } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, inject, LOCALE_ID } from '@angular/core';
import { EmptyState } from '../shared/empty-state';
import type { components, paths } from '../ui-api/schema.generated';

type SharedWithMe =
  paths['/api/ui/shared-with-me']['get']['responses'][200]['content']['application/json'];
type ReceivedShare = components['schemas']['ReceivedShareResponse'];

@Component({
  selector: 'app-shared-with-me-page',
  imports: [EmptyState],
  templateUrl: './shared-with-me-page.html',
})
export class SharedWithMePage {
  private readonly locale = inject(LOCALE_ID);
  protected readonly shares = httpResource<SharedWithMe>(() => '/api/ui/shared-with-me');

  // Formatted here so the cell holds only this text, as Legacy's did; a template expression this
  // long is wrapped onto its own lines, and the cell would gain the spaces around it.
  protected accessUntil(share: ReceivedShare): string {
    return share.accessUntilUtc
      ? formatDate(share.accessUntilUtc, 'yyyy-MM-dd HH:mm', this.locale, 'UTC')
      : 'no expiry';
  }
}
