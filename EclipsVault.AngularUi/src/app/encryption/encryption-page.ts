import { HttpClient, httpResource } from '@angular/common/http';
import { Component, computed, inject, viewChild } from '@angular/core';
import { finalize } from 'rxjs';
import { AdministratorWrites } from '../shared/administrator-writes';
import { ConfirmationDialog } from '../shared/confirmation-dialog';
import { StatusLine } from '../shared/status-line';
import type { components } from '../ui-api/schema.generated';

type Rotation = components['schemas']['RotationResponse'];
type KeyStatus = components['schemas']['KekResponse']['status'];

const path = '/api/ui/encryption';

@Component({
  selector: 'app-encryption-page',
  imports: [StatusLine, ConfirmationDialog],
  providers: [AdministratorWrites],
  templateUrl: './encryption-page.html',
})
export class EncryptionPage {
  private readonly http = inject(HttpClient);
  private readonly confirmation = viewChild.required(ConfirmationDialog);
  protected readonly writes = inject(AdministratorWrites);
  protected readonly encryption = httpResource<components['schemas']['EncryptionResponse']>(
    () => path,
  );
  protected readonly badges: Record<KeyStatus, { tone: string; label: string }> = {
    current: { tone: 'ok', label: 'current' },
    retired: { tone: 'warn', label: 'retired' },
    not_loaded: { tone: 'danger', label: 'not loaded' },
  };
  protected readonly awaitingRotation = computed(() =>
    this.encryption.hasValue() ? Number(this.encryption.value().awaitingRotation) : 0,
  );
  protected readonly someKeyIsNotLoaded = computed(
    () =>
      this.encryption.hasValue() &&
      this.encryption.value().keys.some((key) => key.status === 'not_loaded'),
  );

  protected askToRotate(): void {
    this.confirmation().open(
      "Re-wrap every secret's data key under the current KEK now? This is safe and can be repeated.",
    );
  }

  protected confirmed(confirmed: boolean): void {
    if (!confirmed) return;
    this.writes.send(
      // Reads the status again after any outcome: a refused rotation may still have moved rows.
      () =>
        this.http
          .post<Rotation>(`${path}/rotate`, null)
          .pipe(finalize(() => this.encryption.reload())),
      (rotation) => this.writes.status.set({ message: outcomeOf(rotation), error: false }),
      () => undefined,
    );
  }
}

function outcomeOf(rotation: Rotation): string {
  const moved = Number(rotation.secretsRewrapped) + Number(rotation.versionsRewrapped);
  const result =
    moved === 0
      ? `Everything is already wrapped under the current KEK (${rotation.currentKekId}).`
      : `Rotated ${rotation.secretsRewrapped} secret(s) and ${rotation.versionsRewrapped} version(s) to KEK ${rotation.currentKekId}.`;
  const onMissingKeys = Number(rotation.onMissingKeys);
  return onMissingKeys > 0
    ? `${result} ${onMissingKeys} item(s) remain on keys that are not loaded.`
    : result;
}
