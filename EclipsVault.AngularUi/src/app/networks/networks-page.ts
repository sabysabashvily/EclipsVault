import { DatePipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { Observable, finalize } from 'rxjs';
import { AdministratorWrites } from '../shared/administrator-writes';
import { ConfirmationDialog } from '../shared/confirmation-dialog';
import { StatusLine } from '../shared/status-line';
import { StepUpDialog } from '../shared/step-up-dialog';
import type { components } from '../ui-api/schema.generated';

type TrustedNetwork = components['schemas']['TrustedNetworkResponse'];
type Block = components['schemas']['NetworkBlockResponse'];
type TrustedRange = components['schemas']['TrustedRangeResponse'];
type Confirmation = { kind: 'remove'; network: TrustedNetwork } | { kind: 'unblock'; block: Block };

const path = '/api/ui/networks';

@Component({
  selector: 'app-networks-page',
  imports: [DatePipe, StatusLine, StepUpDialog, ConfirmationDialog],
  providers: [AdministratorWrites],
  templateUrl: './networks-page.html',
})
export class NetworksPage {
  private readonly http = inject(HttpClient);
  private readonly stepUp = viewChild.required(StepUpDialog);
  private readonly confirmation = viewChild.required(ConfirmationDialog);
  private opener: HTMLButtonElement | null = null;
  private pendingConfirmation: Confirmation | null = null;
  protected readonly writes = inject(AdministratorWrites);
  protected readonly networks = httpResource<components['schemas']['NetworksResponse']>(() => path);
  protected readonly range = signal('');
  protected readonly label = signal('');
  protected readonly writesClosed = computed(
    () => this.writes.busy() || this.writes.awaitingProof() || this.writes.blocked(),
  );

  constructor() {
    this.writes.onClear(() => (this.pendingConfirmation = null));
  }

  protected trustCurrentAddress(opener: HTMLButtonElement): void {
    this.opener = opener;
    this.writes.send(
      () => this.reloading(this.http.post<TrustedRange>(`${path}/trust-current-address`, null)),
      (trusted) =>
        this.writes.status.set({
          message: `Your current address ${trusted.range} is now trusted. Access rules apply immediately.`,
          error: false,
        }),
      (proof) => this.stepUp().open(proof),
    );
  }

  protected add(event: Event, opener: HTMLButtonElement): void {
    event.preventDefault();
    this.opener = opener;
    const request: components['schemas']['AddNetworkRequest'] = {
      range: this.range(),
      label: this.label(),
    };
    this.writes.send(
      () => this.reloading(this.http.post<TrustedRange>(path, request)),
      (added) => {
        this.range.set('');
        this.label.set('');
        this.writes.status.set({ message: `Trusted network ${added.range} added.`, error: false });
      },
      (proof) => this.stepUp().open(proof),
    );
  }

  protected verified(verified: boolean): void {
    if (this.opener) this.writes.verified(verified, this.opener);
  }

  protected askRemove(network: TrustedNetwork): void {
    if (this.writes.busy() || this.writes.blocked()) return;
    this.pendingConfirmation = { kind: 'remove', network };
    this.confirmation().open(
      `Stop trusting ${network.range}? Requests from it will fail the network rule for Confidential+ secrets.`,
    );
  }

  protected askUnblock(block: Block, opener: HTMLButtonElement): void {
    if (this.writes.busy() || this.writes.blocked()) return;
    this.opener = opener;
    this.pendingConfirmation = { kind: 'unblock', block };
    this.confirmation().open(`Lift the intrusion-defence block on ${block.range}?`);
  }

  protected confirmed(confirmed: boolean): void {
    const pending = this.pendingConfirmation;
    this.pendingConfirmation = null;
    if (!confirmed || !pending) return;
    if (pending.kind === 'remove') {
      this.writes.send(
        () => this.reloading(this.http.delete<void>(`${path}/${pending.network.networkId}`)),
        () => this.writes.status.set({ message: 'Trusted network removed.', error: false }),
        () => undefined,
      );
      return;
    }
    this.writes.send(
      () =>
        this.reloading(this.http.post<void>(`${path}/blocks/lift`, { range: pending.block.range })),
      () =>
        this.writes.status.set({
          message: `Block on ${pending.block.range} lifted.`,
          error: false,
        }),
      (proof) => this.stepUp().open(proof),
    );
  }

  /** Reads the page again after every write, so a refused write still shows what is saved. */
  private reloading<T>(request: Observable<T>): Observable<T> {
    return request.pipe(finalize(() => this.networks.reload()));
  }
}
