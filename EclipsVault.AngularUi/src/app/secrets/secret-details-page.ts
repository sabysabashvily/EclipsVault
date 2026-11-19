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
import { Observable } from 'rxjs';
import { AdministratorWrites } from '../shared/administrator-writes';
import { ConfirmationDialog } from '../shared/confirmation-dialog';
import { relativeTime } from '../shared/relative-time';
import { StatusLine } from '../shared/status-line';
import { StepUpDialog } from '../shared/step-up-dialog';
import type { components } from '../ui-api/schema.generated';
import { UiApiError } from '../ui-api/ui-api-errors';

type Managed = components['schemas']['ManagedSecretResponse'];
type Version = components['schemas']['SecretVersionResponse'];
type Share = components['schemas']['SecretShareResponse'];
type Revealed = components['schemas']['RevealedValueResponse'];
/** The form whose fields a refusal's field messages belong to. */
type Form = 'rotate' | 'managed' | 'share';

/** How long Legacy's Copy button read "Copied ✓", in milliseconds. */
const copiedFor = 1_600;

/** One secret: its value, metadata, sharing and version history, as Legacy's Details page. */
@Component({
  selector: 'app-secret-details-page',
  imports: [DatePipe, RouterLink, StatusLine, StepUpDialog, ConfirmationDialog],
  providers: [AdministratorWrites],
  templateUrl: './secret-details-page.html',
})
export class SecretDetailsPage {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly title = inject(Title);
  private readonly parameters = toSignal(inject(ActivatedRoute).paramMap);
  private readonly id = computed(() => this.parameters()?.get('id'));
  private readonly path = computed(() => `/api/ui/secrets/${this.id()}`);
  private readonly stepUp = viewChild.required(StepUpDialog);
  private readonly confirmation = viewChild.required(ConfirmationDialog);
  private readonly revealedCode = viewChild<ElementRef<HTMLElement>>('revealedCode');
  private opener: HTMLButtonElement | null = null;
  private awaitingConfirmation: (() => void) | null = null;
  private stored: boolean;
  private readonly sentFrom = signal<Form | null>(null);
  protected readonly writes = inject(AdministratorWrites);
  protected readonly details = httpResource<components['schemas']['SecretDetailsResponse']>(() =>
    this.id() ? this.path() : undefined,
  );
  protected readonly failure = computed(() => {
    const error = this.details.error();
    if (!error) return null;
    return error instanceof UiApiError ? error.code : 'unknown';
  });
  protected readonly locked = computed(
    () => this.writes.busy() || this.writes.awaitingProof() || this.writes.blocked(),
  );
  /** A decrypted value lives only here: never in storage, the address or a cache. */
  protected readonly revealed = signal<Revealed | null>(null);
  protected readonly copyLabel = signal('Copy');
  protected readonly newValue = signal('');
  protected readonly changeNote = signal('');
  protected readonly renewDays = signal('');
  protected readonly managedRenewDays = signal('');
  protected readonly recipient = signal('');
  protected readonly shareDays = signal('0');

  constructor() {
    const navigation = this.router.currentNavigation();
    // A reload or Back restores navigation state too; only the navigation from New secret announces.
    this.stored =
      navigation?.trigger === 'imperative' &&
      navigation.previousNavigation !== null &&
      navigation.extras.state?.['stored'] === true;
    this.writes.onClear(() => {
      this.revealed.set(null);
      // The page may be leaving before the next render removes the value from the document.
      const code = this.revealedCode()?.nativeElement;
      if (code) code.textContent = '';
      this.awaitingConfirmation = null;
    });
    effect(() => {
      this.id();
      untracked(() => this.writes.clear());
    });
    effect(() => {
      if (!this.details.hasValue()) return;
      const secret = this.details.value();
      this.title.setTitle(`${secret.name} — EclipsVault`);
      if (this.stored) {
        this.stored = false;
        this.writes.status.set({
          message: `Secret '${secret.name}' was envelope-encrypted and stored.`,
          error: false,
        });
      }
    });
    effect(() => {
      const error = this.details.error();
      // The reasons travel in navigation memory only, so a reload of Denied shows its generic text.
      // Replacing this page's entry keeps Back from opening the refused secret again.
      if (error instanceof UiApiError && error.code === 'access_denied')
        void this.router.navigateByUrl(`/Account/Denied?secretId=${this.id()}`, {
          state: { reasons: error.reasons },
          replaceUrl: true,
        });
    });
  }

  protected error(form: Form, field: string): string {
    return this.sentFrom() === form ? (this.writes.errors()[field]?.[0] ?? '') : '';
  }

  /** Legacy's licence note under a managed secret; none when licensed or the evaluation is unreadable. */
  protected licenceNote(managed: Managed): 'none' | 'not-started' | 'evaluating' | 'ended' {
    if (managed.licensed || !managed.evaluationKnown) return 'none';
    if (managed.evaluationDaysRemaining === null) return 'not-started';
    return Number(managed.evaluationDaysRemaining) > 0 ? 'evaluating' : 'ended';
  }

  protected daysLeft(managed: Managed): string {
    const days = Number(managed.evaluationDaysRemaining);
    return `${days} ${days === 1 ? 'day' : 'days'} left`;
  }

  protected archived(version: Version): string {
    return `archived ${relativeTime(version.archivedAtUtc, new Date())} · by ${version.archivedBy}`;
  }

  protected reveal(opener: HTMLButtonElement, version: Version | null = null): void {
    const path = version
      ? `${this.path()}/versions/${version.versionId}/reveal`
      : `${this.path()}/reveal`;
    this.send(
      opener,
      () => this.http.post<Revealed>(path, null),
      (revealed) => {
        this.copyLabel.set('Copy');
        this.revealed.set(revealed);
      },
    );
  }

  protected async copy(): Promise<void> {
    const revealed = this.revealed();
    if (!revealed) return;
    try {
      await navigator.clipboard.writeText(revealed.value);
      this.copyLabel.set('Copied ✓');
      setTimeout(() => this.copyLabel.set('Copy'), copiedFor);
    } catch {
      this.copyLabel.set('Copy failed');
    }
  }

  protected askRotate(event: Event, rotation: HTMLDetailsElement, opener: HTMLButtonElement): void {
    event.preventDefault();
    // An empty renewal keeps the current expiry; the browser refuses a fractional or out-of-range one.
    const renewTtlDays = this.renewDays().trim() === '' ? null : Number(this.renewDays());
    const request: components['schemas']['RotateSecretUiRequest'] = {
      newValue: this.newValue(),
      changeNote: this.changeNote(),
      renewTtlDays,
    };
    this.ask(
      `Rotate '${this.details.value()?.name}'? The current value is archived to history and replaced.`,
      () =>
        this.send(
          opener,
          () => this.http.post<void>(`${this.path()}/rotate`, request),
          () => {
            this.newValue.set('');
            this.changeNote.set('');
            this.renewDays.set('');
            rotation.open = false;
            this.changed(
              renewTtlDays === null
                ? 'Secret rotated. The previous value was archived to version history.'
                : `Secret rotated and renewed for ${renewTtlDays} more day(s). The previous value was archived to version history.`,
            );
          },
          'rotate',
        ),
    );
  }

  protected askRotateManaged(event: Event, managed: Managed, opener: HTMLButtonElement): void {
    event.preventDefault();
    const request: components['schemas']['RotateManagedUiRequest'] = {
      renewTtlDays: this.managedRenewDays().trim() === '' ? null : Number(this.managedRenewDays()),
    };
    this.ask(
      `Rotate the real password of '${managed.principal}'? Anything still using the old password stops working immediately.`,
      () =>
        this.send(
          opener,
          () => this.http.post<void>(`${this.path()}/rotate-managed`, request),
          () => {
            this.managedRenewDays.set('');
            this.changed(
              'Rotated upstream. The vault generated a new password, changed the real credential, and stored it — the previous value was archived to version history.',
            );
          },
          'managed',
        ),
    );
  }

  protected askRestore(version: Version, opener: HTMLButtonElement): void {
    this.ask(
      `Restore version ${version.versionNumber} as the live value? The current value will be archived first.`,
      () =>
        this.send(
          opener,
          () => this.http.post<void>(`${this.path()}/versions/${version.versionId}/restore`, null),
          () =>
            this.changed(
              'Secret reverted to the selected version. The value it replaced was archived.',
            ),
        ),
    );
  }

  protected share(event: Event, opener: HTMLButtonElement): void {
    event.preventDefault();
    const name = this.details.value()?.name;
    const request: components['schemas']['ShareSecretUiRequest'] = {
      recipient: this.recipient(),
      // An emptied field means no expiry, as its 0 does.
      ttlDays: Number(this.shareDays()),
    };
    this.send(
      opener,
      () => this.http.post<void>(`${this.path()}/shares`, request),
      () => {
        this.recipient.set('');
        this.shareDays.set('0');
        this.changed(`Access to '${name}' was shared with ${request.recipient}.`);
      },
      'share',
    );
  }

  protected askRevoke(share: Share, opener: HTMLButtonElement): void {
    this.ask(`Revoke ${share.recipient}'s access to '${this.details.value()?.name}'?`, () =>
      this.send(
        opener,
        () => this.http.delete<void>(`${this.path()}/shares/${share.shareId}`),
        () => this.changed('Access revoked.'),
      ),
    );
  }

  protected askDelete(opener: HTMLButtonElement): void {
    this.ask(
      `Delete '${this.details.value()?.name}' and its entire version history permanently? This cannot be undone and will be audited.`,
      () =>
        this.send(
          opener,
          () => this.http.delete<void>(this.path()),
          () => {
            this.writes.clear();
            void this.router.navigateByUrl('/Secrets?deleted=1');
          },
        ),
    );
  }

  protected confirmed(confirmed: boolean): void {
    const action = this.awaitingConfirmation;
    this.awaitingConfirmation = null;
    if (confirmed) action?.();
  }

  protected verified(verified: boolean): void {
    if (this.opener) this.writes.verified(verified, this.opener);
  }

  private ask(message: string, action: () => void): void {
    if (this.locked()) return;
    this.awaitingConfirmation = action;
    this.confirmation().open(message);
  }

  /** Sends a request once, and once more after fresh proof when the server asks for it. */
  private send<T>(
    opener: HTMLButtonElement,
    request: () => Observable<T>,
    saved: (value: T) => void,
    form: Form | null = null,
  ): void {
    this.opener = opener;
    this.sentFrom.set(form);
    this.writes.send(request, saved, (proof) => this.stepUp().open(proof));
  }

  /** Legacy showed the page afresh after a change, so a revealed value does not outlive it. */
  private changed(message: string): void {
    this.revealed.set(null);
    this.writes.status.set({ message, error: false });
    this.details.reload();
  }
}
