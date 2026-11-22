import { HttpClient, httpResource } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { CurrentAccount } from '../account/current-account';
import { StatusLine } from '../shared/status-line';
import type { components } from '../ui-api/schema.generated';
import { UiApiError } from '../ui-api/ui-api-errors';

type Eligibility = components['schemas']['AccessRequestEligibilityResponse'];
type Filing = components['schemas']['AccessRequestFilingRequest'];

const guid = /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i;

/** Where a refused caller lands, as Legacy's Denied page: the policy's reasons and, for a secret, a request form. */
@Component({
  selector: 'app-denied-page',
  imports: [RouterLink, StatusLine],
  templateUrl: './denied-page.html',
})
export class DeniedPage {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly me = inject(CurrentAccount).me;
  /** The policy's reasons arrive only in the navigation that brought the caller here, never from the address. */
  protected readonly reasons: readonly string[];
  protected readonly secretId: string | null;
  protected readonly eligibility = httpResource<Eligibility>(() =>
    this.secretId ? `/api/ui/access-requests/eligibility?secretId=${this.secretId}` : undefined,
  );
  protected readonly managesNetworks = computed(
    () => this.me.hasValue() && this.me.value().areas.includes('networks'),
  );
  protected readonly reason = signal('');
  protected readonly busy = signal(false);
  protected readonly failure = signal('');

  constructor() {
    const navigation = this.router.currentNavigation();
    const state: unknown = navigation?.extras.state?.['reasons'];
    // A reload or Back restores navigation state too; only the navigation from a refusal carries reasons.
    this.reasons =
      navigation?.trigger === 'imperative' &&
      navigation.previousNavigation !== null &&
      Array.isArray(state) &&
      state.every((reason) => typeof reason === 'string')
        ? state
        : [];
    const secretId = inject(ActivatedRoute).snapshot.queryParamMap.get('secretId');
    this.secretId = secretId && guid.test(secretId) ? secretId : null;
  }

  protected submit(event: Event): void {
    event.preventDefault();
    if (this.busy() || !this.secretId) return;
    this.busy.set(true);
    this.failure.set('');
    const filing: Filing = { secretId: this.secretId, reason: this.reason() };
    this.http
      .post<void>('/api/ui/access-requests', filing)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => void this.router.navigateByUrl('/AccessRequests?filed=1'),
        error: (error: unknown) => {
          // Access requests says why a request was not filed.
          if (error instanceof UiApiError && error.status === 409) {
            void this.router.navigateByUrl(`/AccessRequests?refused=${error.code}`);
          } else {
            this.failure.set(
              error instanceof UiApiError
                ? error.title
                : 'The vault could not complete the request.',
            );
          }
        },
      });
  }
}
