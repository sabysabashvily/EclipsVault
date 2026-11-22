import { ScrollRegion } from '../shared/scroll-region';
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
type Account = components['schemas']['ServiceAccountDetailsResponse'];
type Confirmation = { kind: 'delete' } | { kind: 'revoke'; key: Key };
const keyStatuses: Record<Key['status'], { label: string; tone: string }> = {
  active: { label: 'Active', tone: 'tone-ok' },
  expired: { label: 'Expired', tone: 'tone-warn' },
  revoked: { label: 'Revoked', tone: 'tone-danger' },
};

/** The key is shown once, with only its ends visible. Copy reads the value the page still holds. */
function maskedKey(apiKey: string): string {
  const shown = 4;
  if (apiKey.length <= shown * 2) return '•'.repeat(apiKey.length);
  return `${apiKey.slice(0, shown)}…${apiKey.slice(-shown)}`;
}

function expiryChip(days: number): string {
  if (!Number.isInteger(days) || days < 0) return 'Expiry not set';
  if (days === 0) return 'Does not expire';
  return `Expires in ${days} ${days === 1 ? 'day' : 'days'}`;
}

@Component({
  selector: 'app-service-account-page',
  imports: [ScrollRegion, DatePipe, RouterLink, StatusLine, StepUpDialog, ConfirmationDialog],
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
  private readonly confirmation = viewChild.required<ConfirmationDialog>('decisions');
  private readonly issuance = viewChild.required<ConfirmationDialog>('issuance');
  private readonly issueButton = viewChild<ElementRef<HTMLButtonElement>>('issueButton');
  private readonly tokenBox = viewChild<ElementRef<HTMLElement>>('tokenBox');
  private opener: HTMLButtonElement | null = null;
  private pendingConfirmation: Confirmation | null = null;
  protected readonly writes = inject(AdministratorWrites);
  protected readonly account = httpResource<components['schemas']['ServiceAccountDetailsResponse']>(
    () => (this.id() ? `/api/ui/service-accounts/${this.id()}` : undefined),
  );
  protected readonly issued = signal<string | null>(null);
  /** Set only when the clipboard refuses the key, so it can be selected by hand. */
  protected readonly revealed = signal(false);
  protected readonly expiry = signal(0);
  protected readonly ceiling = signal<number | null>(null);
  protected readonly projectScope = signal('');
  protected readonly metadataOnly = signal(false);
  protected readonly allowedRanges = signal('');
  protected readonly keyStatuses = keyStatuses;
  protected readonly maskedKey = maskedKey;
  /** The issue dialog's chips, one per field, from the values the form would send. */
  protected readonly issueChips = computed(() => {
    const account: Account | null = this.account.hasValue() ? this.account.value() : null;
    const ceiling = this.ceiling();
    const ceilingLabel = account?.ceilings.find(
      (option) => Number(option.value) === ceiling,
    )?.label;
    const scope = this.projectScope().trim();
    const ranges = this.allowedRanges().trim();
    return [
      expiryChip(this.expiry()),
      ceilingLabel ? `At most ${ceilingLabel}` : 'No clearance limit',
      scope ? `Project ${scope}` : `Account project ${account?.project ?? ''}`.trim(),
      this.metadataOnly() ? 'Metadata only' : 'Can read values',
      ranges || 'Any network',
    ];
  });

  constructor() {
    this.writes.onClear(() => {
      this.issued.set(null);
      this.revealed.set(false);
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

  protected askIssue(event: Event): void {
    event.preventDefault();
    if (this.writes.busy() || this.writes.awaitingProof() || this.writes.blocked()) return;
    this.issuance().open('It is shown once and is not stored.', {
      title: 'Issue a key',
      confirm: 'Issue key',
      tone: 'primary',
    });
  }

  protected confirmedIssue(confirmed: boolean): void {
    if (!confirmed) return;
    const opener = this.issueButton()?.nativeElement;
    if (!opener) return;
    this.issue(opener);
  }

  private issue(opener: HTMLButtonElement): void {
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
        this.revealed.set(false);
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
    const apiKey = this.issued();
    if (!apiKey) return;
    try {
      await navigator.clipboard.writeText(apiKey);
    } catch {
      // A newer key replaced this one while the clipboard was deciding; never reveal that one.
      if (this.issued() !== apiKey) return;
      // The mask is not something a person can paste. Show the key once, in the same place.
      this.revealed.set(true);
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
