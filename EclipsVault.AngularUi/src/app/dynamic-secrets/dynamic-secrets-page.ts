import { DatePipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import {
  afterNextRender,
  Component,
  computed,
  ElementRef,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';
import { Observable, finalize } from 'rxjs';
import { AdministratorWrites } from '../shared/administrator-writes';
import { ConfirmationDialog } from '../shared/confirmation-dialog';
import { EmptyState } from '../shared/empty-state';
import { ScrollRegion } from '../shared/scroll-region';
import { StatusLine } from '../shared/status-line';
import { StepUpDialog } from '../shared/step-up-dialog';
import type { components } from '../ui-api/schema.generated';

type Role = components['schemas']['DynamicRoleResponse'];
type Lease = components['schemas']['DynamicLeaseResponse'];
type Issued = components['schemas']['IssuedDynamicCredentialResponse'];
type Revoked = components['schemas']['RevokedLeaseResponse'];

const path = '/api/ui/dynamic-secrets';
const backendRefused = 'The backend refused to destroy the credential; it may still work.';
const backendNames: Record<Role['backend'], string> = {
  SqlServer: 'SQL Server',
  Postgres: 'PostgreSQL',
};

@Component({
  selector: 'app-dynamic-secrets-page',
  imports: [DatePipe, EmptyState, ScrollRegion, StatusLine, StepUpDialog, ConfirmationDialog],
  providers: [AdministratorWrites],
  templateUrl: './dynamic-secrets-page.html',
})
export class DynamicSecretsPage {
  private readonly http = inject(HttpClient);
  private readonly injector = inject(Injector);
  private readonly stepUp = viewChild.required(StepUpDialog);
  private readonly confirmation = viewChild.required(ConfirmationDialog);
  private readonly issuedPanel = viewChild<ElementRef<HTMLElement>>('issuedPanel');
  private opener: HTMLButtonElement | null = null;
  private pendingAction: (() => void) | null = null;
  protected readonly writes = inject(AdministratorWrites);
  protected readonly page = httpResource<components['schemas']['DynamicSecretsResponse']>(
    () => path,
  );
  /** The vault keeps no copy: this signal is the only place the password exists after issuing. */
  protected readonly issued = signal<Issued | null>(null);
  protected readonly writesClosed = computed(
    () => this.writes.busy() || this.writes.awaitingProof() || this.writes.blocked(),
  );
  protected readonly everyone = computed(
    () => this.page.hasValue() && this.page.value().leaseScope === 'everyone',
  );
  protected readonly verdict = computed<{ label: string; tone: 'danger' | 'ok' } | null>(() => {
    if (!this.page.hasValue()) return null;
    const leases = this.page.value().leases;
    if (leases.some((lease) => lease.status === 'RevocationFailed')) {
      return { label: 'Revocation failed', tone: 'danger' };
    }
    if (leases.some((lease) => lease.status === 'Active')) {
      return { label: 'Active leases', tone: 'ok' };
    }
    return null;
  });

  protected backendName(role: Role): string {
    return backendNames[role.backend];
  }

  constructor() {
    // Leaving the page, a lost session or a lost response forgets the credential with the pending write.
    this.writes.onClear(() => {
      this.issued.set(null);
      this.pendingAction = null;
    });
  }

  protected stepTtl(input: HTMLInputElement, role: Role, delta: -1 | 1): void {
    const raw = input.value.trim();
    const fallback = Number(role.defaultTtlMinutes);
    const parsed = raw === '' ? fallback : Number(raw);
    const base = Number.isFinite(parsed) ? Math.trunc(parsed) : fallback;
    const max = Math.max(1, Number(role.maxTtlMinutes));
    const next = Math.min(max, Math.max(1, base + delta));
    input.value = String(next);
  }

  protected askIssue(event: Event, role: Role, minutes: string, opener: HTMLButtonElement): void {
    event.preventDefault();
    if (this.writesClosed()) return;
    const ttlMinutes = minutes === '' ? null : Number(minutes);
    const lease =
      ttlMinutes === null ? `${role.defaultTtlMinutes} min (default)` : `${ttlMinutes} min`;
    this.pendingAction = () => this.issue(role, ttlMinutes, opener);
    this.confirmation().open(
      `Mint a live credential for '${role.name}' on ${this.backendName(role)}? The password is shown once and cannot be recovered.`,
      {
        title: 'Issue dynamic credential?',
        confirm: 'Issue credential',
        tone: 'primary',
        facts: [
          ['Role', role.name],
          ['Backend', this.backendName(role)],
          ['Lease', lease],
        ],
        note: 'Issuance saved to the audit log',
      },
    );
  }

  private issue(role: Role, ttlMinutes: number | null, opener: HTMLButtonElement): void {
    if (this.writesClosed()) return;
    this.opener = opener;
    this.issued.set(null);
    const request: components['schemas']['IssueDynamicCredentialRequest'] = {
      roleId: role.roleId,
      ttlMinutes,
    };
    const detail = `Confirming mints a live credential for '${role.name}' on ${this.backendName(role)}. The password is shown once. Issuing the credential is saved to the audit log.`;
    this.writes.send(
      () => this.reloading(this.http.post<Issued>(`${path}/issue`, request)),
      (issued) => {
        this.issued.set(issued);
        afterNextRender(() => this.issuedPanel()?.nativeElement.focus(), {
          injector: this.injector,
        });
      },
      (proof) => this.stepUp().open(proof, detail),
    );
  }

  protected verified(verified: boolean): void {
    if (this.opener) this.writes.verified(verified, this.opener);
  }

  protected askRevoke(lease: Lease): void {
    if (this.writesClosed()) return;
    this.pendingAction = () => this.revoke(lease);
    this.confirmation().open(
      `Hand back '${lease.credentialIdentity}'? It stops working immediately.`,
      {
        title: 'Revoke dynamic credential?',
        confirm: 'Revoke credential',
        facts: [
          ['Role', lease.roleName],
          ['Username', lease.credentialIdentity],
          ...(this.everyone() ? ([['Taken by', lease.holder]] as const) : []),
        ],
        note: 'Saved to the audit log',
      },
    );
  }

  protected confirmed(confirmed: boolean): void {
    const action = this.pendingAction;
    this.pendingAction = null;
    if (!confirmed || !action) return;
    action();
  }

  private revoke(lease: Lease): void {
    this.writes.send(
      () => this.reloading(this.http.post<Revoked>(`${path}/leases/${lease.leaseId}/revoke`, null)),
      (revoked) =>
        this.writes.status.set(
          revoked.status === 'Revoked'
            ? { message: 'Credential handed back — it no longer works.', error: false }
            : { message: revoked.revocationNote ?? backendRefused, error: true },
        ),
      () => undefined,
    );
  }

  protected days(remaining: number | string): string {
    return Number(remaining) === 1 ? 'day' : 'days';
  }

  protected positive(remaining: number | string | null): boolean {
    return Number(remaining) > 0;
  }

  /** Reads the page again after every write, so a refused write still shows what is saved. */
  private reloading<T>(request: Observable<T>): Observable<T> {
    return request.pipe(finalize(() => this.page.reload()));
  }
}
