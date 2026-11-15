import { HttpClient, httpResource } from '@angular/common/http';
import { Component, DOCUMENT, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { attachmentName, saveAttachment } from '../shared/attachment';
import { StatusLine } from '../shared/status-line';
import type { paths } from '../ui-api/schema.generated';
import { UiApiError } from '../ui-api/ui-api-errors';

type YourData = paths['/api/ui/data']['get']['responses'][200]['content']['application/json'];

@Component({
  selector: 'app-data-page',
  imports: [StatusLine],
  templateUrl: './data-page.html',
})
export class DataPage {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly data = httpResource<YourData>(() => '/api/ui/data');
  protected readonly downloading = signal(false);
  protected readonly failure = signal('');

  protected download(): void {
    if (this.downloading()) return;
    this.downloading.set(true);
    this.failure.set('');
    // HttpClient adds the antiforgery header to this POST; the export is refused without it.
    this.http
      .post('/api/ui/data/export', null, { observe: 'response', responseType: 'blob' })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.downloading.set(false)),
      )
      .subscribe({
        next: (response) => {
          if (response.body)
            saveAttachment(
              this.document,
              response.body,
              attachmentName(response.headers.get('Content-Disposition')),
            );
        },
        error: (error: unknown) =>
          this.failure.set(
            error instanceof UiApiError ? error.title : 'Could not download your data. Try again.',
          ),
      });
  }
}
