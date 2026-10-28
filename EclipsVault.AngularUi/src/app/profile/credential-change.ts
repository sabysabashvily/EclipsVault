import { DestroyRef, Component, inject, viewChild } from '@angular/core';
import { Observable } from 'rxjs';
import { ConfirmationDialog } from '../shared/confirmation-dialog';
import { ProfileWrites } from './profile-writes';
import type { PrepareChange, PreparedOperation, VerifiedOperation } from './profile-types';
import { VerificationDialog } from './verification-dialog';

/** One verified security change: what the server freezes, and the request that commits it. */
export interface CredentialChangeFlow<T> {
  /** Asked first in the shared destructive confirmation; an additive change has none. */
  readonly confirmation?: string;
  readonly change: PrepareChange;
  /** The frozen intent the verification dialog shows. */
  readonly intent: string;
  /** What to say when the commit's reply never arrived. */
  readonly lostReply?: string;
  commit(operationId: string): Observable<T>;
  committed(result: T): void;
  /** Rereads what the account can change after a refusal, because the page's copy may be stale. */
  reconcileAfterFailure(): void;
}

/**
 * Runs each Profile security change through confirmation, server preparation, account verification
 * and its single commit. The dialogs never nest, and a commit is never replayed.
 */
@Component({
  selector: 'app-credential-change',
  imports: [ConfirmationDialog, VerificationDialog],
  template: `
    <app-confirmation-dialog (decision)="decide($event)" />
    <app-verification-dialog
      idPrefix="credential-verification"
      (verified)="commit($event)"
      (cancelled)="end()"
    />
  `,
})
export class CredentialChange {
  private readonly session = inject(ProfileWrites);
  private readonly confirmation = viewChild.required(ConfirmationDialog);
  private readonly verification = viewChild.required(VerificationDialog);
  private flow: CredentialChangeFlow<unknown> | null = null;
  private opener: Element | null = null;
  private active = false;

  constructor() {
    this.session.onClear(() => this.end(), inject(DestroyRef));
  }

  start<T>(flow: CredentialChangeFlow<T>, opener: Element | null): void {
    if (this.active || this.session.busy() || this.session.blocked()) return;
    this.flow = flow;
    this.opener = opener;
    this.active = true;
    if (flow.confirmation) this.confirmation().open(flow.confirmation);
    else this.prepare();
  }

  protected decide(confirmed: boolean): void {
    if (confirmed) this.prepare();
    else this.end();
  }

  protected commit(verified: VerifiedOperation): void {
    const flow = this.flow;
    this.end();
    if (!flow) return;
    this.session.write(
      flow.commit(verified.operationId),
      (result) => flow.committed(result),
      () => flow.reconcileAfterFailure(),
      flow.lostReply,
    );
  }

  protected end(): void {
    this.flow = null;
    this.opener = null;
    this.active = false;
  }

  private prepare(): void {
    const flow = this.flow;
    if (!flow) return;
    this.session.write(
      this.session.http.post<PreparedOperation>('/api/ui/profile/operations', flow.change),
      (operation) => this.verification().open(operation, flow.intent, { opener: this.opener }),
      () => {
        this.end();
        flow.reconcileAfterFailure();
      },
    );
  }
}
