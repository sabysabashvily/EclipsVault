import { HttpClient, httpResource } from '@angular/common/http';
import { Component, DOCUMENT, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { StatusLine } from '../shared/status-line';
import type { paths } from '../ui-api/schema.generated';
import { UiApiError } from '../ui-api/ui-api-errors';

type YourData = paths['/api/ui/data']['get']['responses'][200]['content']['application/json'];

/**
 * The file name the server chose: `filename*` when present, since it carries non-ASCII names
 * exactly (RFC 6266), otherwise `filename`. Empty when there is neither, so the browser names it.
 */
export function attachmentName(disposition: string | null): string {
  const extended = /filename\*=UTF-8''([^;]+)/i.exec(disposition ?? '');
  if (extended) return decodeURIComponent(extended[1]);
  return /filename="?([^";]+)"?/i.exec(disposition ?? '')?.[1] ?? '';
}

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
            this.save(response.body, attachmentName(response.headers.get('Content-Disposition')));
        },
        error: (error: unknown) =>
          this.failure.set(
            error instanceof UiApiError ? error.title : 'Could not download your data. Try again.',
          ),
      });
  }

  private save(file: Blob, name: string): void {
    const url = URL.createObjectURL(file);
    const link = this.document.createElement('a');
    link.href = url;
    link.download = name;
    link.click();
    // The browser has taken the file; keeping the URL would keep a copy of the data in memory.
    URL.revokeObjectURL(url);
  }
}
