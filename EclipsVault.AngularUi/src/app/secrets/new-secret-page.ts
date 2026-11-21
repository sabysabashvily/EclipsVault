import { HttpClient } from '@angular/common/http';
import { Component, DestroyRef, inject, linkedSignal, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { Router } from '@angular/router';
import { CurrentAccount } from '../account/current-account';
import type { components } from '../ui-api/schema.generated';
import { UiApiError } from '../ui-api/ui-api-errors';

type Request = components['schemas']['CreateSecretUiRequest'];

/** New secret, in the caller's own project by default. A stored secret opens on its details page. */
@Component({
  selector: 'app-new-secret-page',
  templateUrl: './new-secret-page.html',
})
export class NewSecretPage {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  private readonly me = inject(CurrentAccount).me;
  protected readonly environments = ['Development', 'Staging', 'Production'] as const;
  protected readonly sensitivities = [
    { value: 1, label: 'Internal' },
    { value: 2, label: 'Confidential' },
    { value: 3, label: 'Secret' },
    { value: 4, label: 'Top Secret' },
  ] as const;
  protected readonly name = signal('');
  protected readonly value = signal('');
  protected readonly project = linkedSignal(() =>
    this.me.hasValue() ? this.me.value().project : '',
  );
  protected readonly ttlDays = signal('0');
  protected readonly environment = signal<NonNullable<Request['environment']>>('Development');
  protected readonly sensitivity = signal(1);
  protected readonly busy = signal(false);
  protected readonly summary = signal('');
  protected readonly errors = signal<Readonly<Record<string, string[]>>>({});

  protected error(field: string): string {
    return this.errors()[field]?.[0] ?? '';
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (this.busy()) return;
    this.busy.set(true);
    this.summary.set('');
    this.errors.set({});
    const request: Request = {
      name: this.name(),
      value: this.value(),
      project: this.project(),
      environment: this.environment(),
      sensitivity: this.sensitivity(),
      // An empty field is no number of days: the server refuses it, as Legacy's form did.
      ttlDays: this.ttlDays().trim() === '' ? null : Number(this.ttlDays()),
    };
    this.http
      .post<components['schemas']['CreatedSecretResponse']>('/api/ui/secrets', request)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        // The details page announces the stored secret when this navigation's state says so.
        next: (created) =>
          void this.router.navigateByUrl(`/Secrets/Details/${created.secretId}`, {
            state: { stored: true },
          }),
        error: (error: unknown) => {
          if (error instanceof UiApiError && Object.keys(error.errors).length) {
            this.errors.set(error.errors);
          } else if (error instanceof UiApiError) {
            // A policy refusal reads its reasons as Legacy's form did; any other refusal reads its title.
            this.summary.set(error.reasons.length ? error.reasons.join(' ') : error.title);
          } else {
            this.summary.set('The vault could not complete the request.');
          }
        },
      });
  }
}
