import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component } from '@angular/core';
import { EmptyState } from '../shared/empty-state';
import type { paths } from '../ui-api/schema.generated';

type SharedWithMe =
  paths['/api/ui/shared-with-me']['get']['responses'][200]['content']['application/json'];

@Component({
  selector: 'app-shared-with-me-page',
  imports: [DatePipe, EmptyState],
  templateUrl: './shared-with-me-page.html',
})
export class SharedWithMePage {
  protected readonly shares = httpResource<SharedWithMe>(() => '/api/ui/shared-with-me');
}
