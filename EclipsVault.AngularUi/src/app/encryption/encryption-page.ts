import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AdministratorWrites } from '../shared/administrator-writes';
import { ConfirmationDialog } from '../shared/confirmation-dialog';
import { StatusLine } from '../shared/status-line';
import type { components } from '../ui-api/schema.generated';

type Encryption = components['schemas']['EncryptionResponse'];
type Rotation = components['schemas']['RotationResponse'];
type Key = Encryption['keys'][number];

const path = '/api/ui/encryption';

/** "1 item", "5 items"; and the same for secrets and versions. */
export function count(n: number, one = 'item', many = 'items'): string {
  return `${n} ${n === 1 ? one : many}`;
}

/** A key's status pill and the line under its id that says what it is for. */
export function roleOf(key: Key): { tone: string; label: string; role: string } {
  if (key.status === 'current')
    return { tone: 'ok', label: 'Current', role: 'Wraps every new secret' };
  if (key.status === 'not_loaded')
    return {
      tone: 'danger',
      label: 'Not loaded',
      role: "Not on this server · its items can't be read",
    };
  return Number(key.secrets) + Number(key.versions) > 0
    ? {
        tone: 'warn',
        label: 'Retired',
        role: 'Loaded so its items can be read until they are re-wrapped',
      }
    : {
        tone: 'muted',
        label: 'Retired',
        role: 'Wraps no secrets · the Data Protection key ring may still need it',
      };
}

/** What the page says about the vault's keys: the title's verdict and the line under it. */
export function stateOf(encryption: Encryption, busy: boolean, refusal: string | undefined) {
  const waiting = Number(encryption.awaitingRotation);
  const missing = Number(encryption.onMissingKeys);
  if (busy)
    return {
      verdict: { tone: 'info', label: 'Re-wrapping' },
      context: `Re-wrapping ${count(waiting)} under the current master key.`,
    };
  if (missing > 0)
    return {
      verdict: { tone: 'danger', label: 'Action needed' },
      context: `${count(missing)} use a master key this server doesn't have, so they can't be read. ${waiting} more ${waiting === 1 ? 'is' : 'are'} waiting to be re-wrapped.`,
    };
  if (refusal === 'audit_unavailable')
    return {
      verdict: { tone: 'danger', label: 'Stopped partway' },
      context: `The last re-wrap stopped partway. ${count(waiting)} still use the retired master key.`,
    };
  if (waiting > 0)
    return {
      verdict: { tone: 'warn', label: 'Re-wrap pending' },
      context: `${count(waiting)} still use the retired master key. Re-wrap them under the current key; keep the retired key loaded while the Data Protection key ring needs it.`,
    };
  return {
    verdict: { tone: 'ok', label: 'All current' },
    context: "Every secret's data key is wrapped by the current master key. Nothing to re-wrap.",
  };
}

@Component({
  selector: 'app-encryption-page',
  imports: [RouterLink, StatusLine, ConfirmationDialog],
  providers: [AdministratorWrites],
  templateUrl: './encryption-page.html',
})
export class EncryptionPage {
  private readonly http = inject(HttpClient);
  private readonly confirmation = viewChild.required(ConfirmationDialog);
  protected readonly writes = inject(AdministratorWrites);
  protected readonly encryption = httpResource<Encryption>(() => path);
  protected readonly count = count;
  protected readonly roleOf = roleOf;
  /** The last re-wrap that succeeded, until the next one starts. */
  protected readonly lastRotation = signal<Rotation | null>(null);

  /** The refusals this page words itself; any other error stays on the status line. */
  protected readonly refusal = computed(() => {
    const code = this.writes.status().code;
    return code === 'audit_unavailable' || code === 'rotation_running' ? code : undefined;
  });

  protected readonly view = computed(() => {
    if (!this.encryption.hasValue()) return null;
    const encryption = this.encryption.value();
    const total = (keys: Key[]) =>
      keys.reduce(
        (sum, key) => ({
          secrets: sum.secrets + Number(key.secrets),
          versions: sum.versions + Number(key.versions),
        }),
        { secrets: 0, versions: 0 },
      );
    const current = total(encryption.keys.filter((key) => key.status === 'current'));
    const waiting = Number(encryption.awaitingRotation);
    const missing = Number(encryption.onMissingKeys);
    return {
      ...encryption,
      ...stateOf(encryption, this.writes.busy(), this.refusal()),
      waiting,
      missing,
      retired: total(encryption.keys.filter((key) => key.status === 'retired')),
      onCurrent: current.secrets + current.versions,
      missingKeys: encryption.keys.filter((key) => key.status === 'not_loaded'),
    };
  });

  protected askToRotate(): void {
    const model = this.view();
    if (!model) return;
    const facts: [string, string][] = [
      ['Current key', model.currentKekId],
      [
        'To re-wrap',
        `${count(model.retired.secrets, 'secret', 'secrets')} and ${count(model.retired.versions, 'version', 'versions')}`,
      ],
    ];
    if (model.missing > 0) facts.push(['Skipped, key not loaded', count(model.missing)]);
    this.confirmation().open(
      "Each item's data key is wrapped again by the current master key. Secret values are not decrypted or re-encrypted, and it is safe to run again.",
      {
        title: `Re-wrap ${count(model.waiting)}?`,
        confirm: `Re-wrap ${count(model.waiting)}`,
        tone: 'primary',
        facts,
        note: 'Saved to the audit log',
      },
    );
  }

  protected confirmed(confirmed: boolean): void {
    if (!confirmed) return;
    this.lastRotation.set(null);
    this.writes.send(
      // Reads the status again after any outcome: a refused rotation may still have moved rows.
      () =>
        this.http
          .post<Rotation>(`${path}/rotate`, null)
          .pipe(finalize(() => this.encryption.reload())),
      (rotation) => this.lastRotation.set(rotation),
      () => undefined,
    );
  }
}
