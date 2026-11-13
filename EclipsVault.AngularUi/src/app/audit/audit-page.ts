import { HttpClient, httpResource } from '@angular/common/http';
import { Component, DOCUMENT, computed, inject, signal, viewChild } from '@angular/core';
import { AdministratorWrites } from '../shared/administrator-writes';
import { attachmentName, saveAttachment } from '../shared/attachment';
import { relativeTime, utcTimestamp } from '../shared/relative-time';
import { StatusLine } from '../shared/status-line';
import { StepUpDialog } from '../shared/step-up-dialog';
import { TableFilter } from '../shared/table-filter';
import type { components } from '../ui-api/schema.generated';

type Checkpoint = components['schemas']['AuditCheckpointResponse'];
type Verification = components['schemas']['AuditVerificationResponse'];

const path = '/api/ui/audit';

/** Opening the page records a view; Verify and Checkpoint answer in place, so they record none. */
@Component({
  selector: 'app-audit-page',
  imports: [StatusLine, StepUpDialog, TableFilter],
  providers: [AdministratorWrites],
  templateUrl: './audit-page.html',
})
export class AuditPage {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly stepUp = viewChild.required(StepUpDialog);
  private opener: HTMLButtonElement | null = null;
  private readonly created = signal<Checkpoint | null>(null);
  protected readonly writes = inject(AdministratorWrites);
  protected readonly audit = httpResource<components['schemas']['AuditResponse']>(() => path);
  protected readonly query = signal('');
  protected readonly verification = signal<Verification | null>(null);
  protected readonly latestCheckpoint = computed(
    () => this.created() ?? (this.audit.hasValue() ? this.audit.value().latestCheckpoint : null),
  );
  protected readonly entries = computed(() => {
    if (!this.audit.hasValue()) return [];
    const now = new Date();
    return this.audit.value().entries.map((entry) => ({
      ...entry,
      ago: relativeTime(entry.timestampUtc, now),
      exact: utcTimestamp(entry.timestampUtc),
    }));
  });
  protected readonly filtered = computed(() => {
    const query = this.query().trim().toLowerCase();
    return this.entries().filter((entry) =>
      [entry.ago, entry.actionLabel, entry.resource, entry.actor, entry.sourceIp, entry.details]
        .join(' ')
        .toLowerCase()
        .includes(query),
    );
  });

  protected verify(): void {
    this.writes.send(
      () => {
        // A refused verification must not leave the previous outcome on screen.
        this.verification.set(null);
        return this.http.post<Verification>(`${path}/verify`, null);
      },
      (verification) => this.verification.set(verification),
      () => undefined,
    );
  }

  protected createCheckpoint(): void {
    this.writes.send(
      () => this.http.post<Checkpoint>(`${path}/checkpoints`, null),
      (checkpoint) => {
        this.created.set(checkpoint);
        this.writes.status.set({
          message: `Signed checkpoint created at sequence ${checkpoint.sequence} with key ${checkpoint.signingKeyId}.`,
          error: false,
        });
      },
      () => undefined,
    );
  }

  protected export(opener: HTMLButtonElement): void {
    this.opener = opener;
    this.writes.send(
      () => this.http.post(`${path}/export`, null, { observe: 'response', responseType: 'blob' }),
      (response) => {
        if (response.body)
          saveAttachment(
            this.document,
            response.body,
            attachmentName(response.headers.get('Content-Disposition')),
          );
      },
      (proof) => this.stepUp().open(proof),
    );
  }

  protected verified(verified: boolean): void {
    if (this.opener) this.writes.verified(verified, this.opener);
  }

  /** Legacy's `yyyy-MM-dd HH:mm 'UTC'`. */
  protected minute(timestampUtc: string): string {
    return `${new Date(timestampUtc).toISOString().slice(0, 16).replace('T', ' ')} UTC`;
  }
}
