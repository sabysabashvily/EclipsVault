import { DatePipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import {
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AdministratorWrites } from '../shared/administrator-writes';
import { ConfirmationDialog } from '../shared/confirmation-dialog';
import { StatusLine } from '../shared/status-line';
import { StepUpDialog } from '../shared/step-up-dialog';
import type { components } from '../ui-api/schema.generated';
import { UiApiError } from '../ui-api/ui-api-errors';
import { ServiceAccountNotices } from './service-account-notices';

type Key = components['schemas']['ServiceAccountKeyResponse'];
type Confirmation = { kind: 'delete' } | { kind: 'revoke'; key: Key };
const keyStatuses: Record<Key['status'], { label: string; tone: string }> = {
  active: { label: 'Active', tone: 'tone-ok' },
  expired: { label: 'Expired', tone: 'tone-warn' },
  revoked: { label: 'Revoked', tone: 'tone-danger' },
};

@Component({
  selector: 'app-service-account-page',
  imports: [DatePipe, RouterLink, StatusLine, StepUpDialog, ConfirmationDialog],
  providers: [AdministratorWrites],
  templateUrl: './service-account-page.html',
})
export class ServiceAccountPage {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly title = inject(Title);
  private readonly notices = inject(ServiceAccountNotices);
  private readonly parameters = toSignal(inject(ActivatedRoute).paramMap);
  private readonly id = computed(() => this.parameters()?.get('id'));
  private readonly stepUp = viewChild.required(StepUpDialog);
  private readonly confirmation = viewChild.required(ConfirmationDialog);
  private readonly tokenBox = viewChild<ElementRef<HTMLElement>>('tokenBox');
  private opener: HTMLButtonElement | null = null;
  private pendingConfirmation: Confirmation | null = null;
  protected readonly writes = inject(AdministratorWrites);
  protected readonly account = httpResource<components['schemas']['ServiceAccountDetailsResponse']>(
    () => (this.id() ? `/api/ui/service-accounts/${this.id()}` : undefined),
  );
  protected readonly issued = signal<string | null>(null);
  protected readonly expiry = signal(0);
  protected readonly ceiling = signal<number | null>(null);
  protected readonly projectScope = signal('');
  protected readonly metadataOnly = signal(false);
  protected readonly allowedRanges = signal('');
  protected readonly keyStatuses = keyStatuses;

  constructor() {
    this.writes.onClear(() => {
      this.issued.set(null);
      const box = this.tokenBox()?.nativeElement;
      if (box) box.textContent = '';
      this.pendingConfirmation = null;
    });
    effect(() => {
      this.id();
      untracked(() => this.writes.clear());
    });
    effect(() => {
      if (!this.account.hasValue()) return;
      const account = this.account.value();
      this.title.setTitle(`${account.name} — EclipsVault`);
      if (this.notices.createdId === account.serviceAccountId) {
        this.notices.createdId = null;
        this.writes.status.set({
          message: `Service account '${account.name}' created. Issue an API key to let it authenticate.`,
          error: false,
        });
      }
    });
  }

  protected loadError(): string {
    const error = this.account.error();
    return error instanceof UiApiError && error.code === 'service_account_not_found'
      ? 'Service account not found.'
      : 'Could not load service account. Try again.';
  }
  protected error(field: string): string {
    return this.writes.errors()[field]?.[0] ?? '';
  }

  protected issue(event: Event, opener: HTMLButtonElement): void {
    event.preventDefault();
    this.opener = opener;
    const request: components['schemas']['IssueServiceAccountKeyRequest'] = {
      expiresInDays: this.expiry(),
      clearanceCeiling: this.ceiling(),
      projectScope: this.projectScope(),
      metadataOnly: this.metadataOnly(),
      allowedRanges: this.allowedRanges(),
    };
    const path = `/api/ui/service-accounts/${this.id()}/keys`;
    this.writes.send(
      () => this.http.post<components['schemas']['IssuedServiceAccountKeyResponse']>(path, request),
      (key) => {
        this.issued.set(key.apiKey);
        this.saved('API key issued. Copy it now — it will not be shown again.');
      },
      (proof) => this.stepUp().open(proof),
    );
  }

  protected toggle(opener: HTMLButtonElement): void {
    if (!this.account.hasValue()) return;
    this.opener = opener;
    const enabled = !this.account.value().enabled;
    const path = `/api/ui/service-accounts/${this.id()}/${enabled ? 'enable' : 'disable'}`;
    this.writes.send(
      () => this.http.post<void>(path, null),
      () =>
        this.saved(
          enabled
            ? 'Service account enabled.'
            : 'Service account disabled — all of its keys are now rejected.',
        ),
      (proof) => this.stepUp().open(proof),
    );
  }

  protected verified(verified: boolean): void {
    if (this.opener) this.writes.verified(verified, this.opener);
  }

  protected askDelete(): void {
    if (!this.account.hasValue() || this.writes.busy() || this.writes.blocked()) return;
    this.pendingConfirmation = { kind: 'delete' };
    this.confirmation().open(
      `Delete service account '${this.account.value().name}' and all its API keys? This cannot be undone.`,
    );
  }
  protected askRevoke(key: Key): void {
    if (this.writes.busy() || this.writes.blocked()) return;
    this.pendingConfirmation = { kind: 'revoke', key };
    this.confirmation().open(
      `Revoke key ${key.prefix}…? Any caller using it will stop working immediately.`,
    );
  }
  protected confirmed(confirmed: boolean): void {
    const pending = this.pendingConfirmation;
    this.pendingConfirmation = null;
    if (!confirmed || !pending) return;
    const path = `/api/ui/service-accounts/${this.id()}`;
    this.writes.send(
      () =>
        this.http.delete<void>(
          pending.kind === 'revoke' ? `${path}/keys/${pending.key.keyId}` : path,
        ),
      () => {
        if (pending.kind === 'delete') {
          this.notices.deleted = true;
          this.writes.clear();
          void this.router.navigateByUrl('/ServiceAccounts');
        } else this.saved('API key revoked.');
      },
      () => undefined,
    );
  }
  protected async copy(): Promise<void> {
    const visible = this.tokenBox()?.nativeElement.textContent;
    if (!visible) return;
    try {
      await navigator.clipboard.writeText(visible);
    } catch {
      this.writes.status.set({
        message: 'Could not copy the key. Select it and copy it now.',
        error: true,
      });
    }
  }
  private saved(message: string): void {
    this.writes.status.set({ message, error: false });
    this.account.reload();
  }
}
