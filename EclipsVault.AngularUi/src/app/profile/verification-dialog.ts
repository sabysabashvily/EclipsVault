import { HttpClient } from '@angular/common/http';
import {
  Component,
  DOCUMENT,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { Subscription, firstValueFrom } from 'rxjs';
import { StatusLine } from '../shared/status-line';
import { UiApiError } from '../ui-api/ui-api-errors';
import { PasskeyProof } from './passkey-proof';
import { ProfileSession } from './profile-session';
import type { PreparedOperation, ProofMethod, VerifiedOperation } from './profile-types';

@Component({
  selector: 'app-verification-dialog',
  imports: [StatusLine],
  templateUrl: './verification-dialog.html',
})
export class VerificationDialog {
  readonly verified = output<VerifiedOperation>();
  readonly cancelled = output<void>();
  private readonly http = inject(HttpClient);
  private readonly session = inject(ProfileSession);
  private readonly passkey = inject(PasskeyProof);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly cancelButton = viewChild.required<ElementRef<HTMLButtonElement>>('cancelButton');
  private operation: PreparedOperation | null = null;
  private opener: Element | null = null;
  private request: Subscription | undefined;
  private prompt: AbortController | undefined;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private attempt = 0;
  private cancelOperation = true;
  protected readonly intent = signal('');
  protected readonly methods = signal<ProofMethod[]>([]);
  protected readonly method = signal<ProofMethod>('PasswordAndAuthenticatorCode');
  protected readonly password = signal('');
  protected readonly code = signal('');
  protected readonly needsPassword = signal(false);
  protected readonly busy = signal(false);
  protected readonly expired = signal(false);
  protected readonly message = signal('');

  constructor() {
    this.session.onClear(() => this.close());
  }

  open(
    operation: PreparedOperation,
    intent: string,
    options: {
      currentPassword?: string;
      needsPassword?: boolean;
      cancelOperation?: boolean;
      opener?: Element | null;
    } = {},
  ): void {
    this.close();
    this.opener = options.opener ?? this.document.activeElement;
    this.operation = operation;
    this.intent.set(intent);
    this.methods.set(operation.methods);
    this.method.set(operation.methods[0]);
    this.password.set(options.currentPassword ?? '');
    this.needsPassword.set(options.needsPassword ?? false);
    this.cancelOperation = options.cancelOperation ?? true;
    this.expired.set(false);
    this.message.set('');
    this.timer = setTimeout(
      () => {
        this.stopProof();
        this.password.set('');
        this.code.set('');
        this.expired.set(true);
        this.message.set('This change was not verified in time. Cancel and start it again.');
      },
      Math.max(0, Date.parse(operation.expiresAtUtc) - Date.now()),
    );
    afterNextRender(
      () => {
        if (!this.operation) return;
        this.dialog().nativeElement.showModal();
        this.cancelButton().nativeElement.focus();
      },
      { injector: this.injector },
    );
  }

  close(restoreFocus = false): void {
    this.stopProof();
    clearTimeout(this.timer);
    this.operation = null;
    this.password.set('');
    this.code.set('');
    const dialog = this.dialog().nativeElement;
    if (dialog.open) dialog.close();
    const opener = this.opener;
    this.opener = null;
    if (!restoreFocus || !opener) return;
    afterNextRender(
      () => {
        if (!this.operation && opener instanceof HTMLElement && opener.isConnected) opener.focus();
      },
      { injector: this.injector },
    );
  }

  protected cancel(event?: Event): void {
    event?.preventDefault();
    const operation = this.operation;
    this.close(true);
    if (operation && this.cancelOperation)
      this.http
        .delete(`/api/ui/profile/operations/${operation.operationId}`)
        .subscribe({ error: () => undefined });
    this.cancelled.emit();
  }

  protected selectMethod(value: string): void {
    const selected = this.methods().find((method) => method === value);
    if (!selected || this.busy()) return;
    this.method.set(selected);
    this.password.set('');
    this.code.set('');
    this.message.set('');
  }

  protected submit(event: Event): void {
    event.preventDefault();
    const operation = this.operation;
    if (!operation || this.busy() || this.expired()) return;
    this.busy.set(true);
    this.message.set('');
    if (this.method() === 'Passkey') {
      void this.verifyPasskey(operation);
      return;
    }
    this.request = this.http
      .post<VerifiedOperation>(`/api/ui/profile/operations/${operation.operationId}/verify`, {
        method: this.method(),
        currentPassword: this.password(),
        code: this.code(),
      })
      .subscribe({
        next: (result) => this.finish(result),
        error: (error: unknown) => this.failed(error),
      });
  }

  private async verifyPasskey(operation: PreparedOperation): Promise<void> {
    const attempt = ++this.attempt;
    this.prompt = new AbortController();
    try {
      const options = await firstValueFrom(
        this.http.post<{ publicKey: unknown }>(
          `/api/ui/profile/operations/${operation.operationId}/passkey/begin`,
          null,
        ),
      );
      if (attempt !== this.attempt) return;
      const credential = await this.passkey.get(options.publicKey, this.prompt.signal);
      if (attempt !== this.attempt) return;
      this.request = this.http
        .post<VerifiedOperation>(
          `/api/ui/profile/operations/${operation.operationId}/passkey/complete`,
          {
            credential,
            currentPassword: this.needsPassword() ? this.password() : null,
          },
        )
        .subscribe({
          next: (result) => this.finish(result),
          error: (error: unknown) => this.failed(error),
        });
    } catch (error) {
      if (attempt !== this.attempt) return;
      this.failed(
        error instanceof UiApiError
          ? error
          : new UiApiError(
              400,
              'cancelled',
              'The passkey prompt was cancelled. No other verification method was attempted.',
            ),
      );
    }
  }

  private finish(result: VerifiedOperation): void {
    this.close(true);
    if (Date.parse(result.commitByUtc) <= Date.now()) {
      this.cancelled.emit();
      this.session.failed(
        new UiApiError(
          409,
          'verification_expired',
          'Your verification expired. Start the change again.',
        ),
      );
      return;
    }
    this.verified.emit(result);
  }

  private failed(error: unknown): void {
    this.busy.set(false);
    this.password.set('');
    this.code.set('');
    this.message.set(
      error instanceof UiApiError
        ? error.title
        : 'The verification response was lost. Cancel and start again.',
    );
  }

  private stopProof(): void {
    this.attempt++;
    this.prompt?.abort();
    this.request?.unsubscribe();
    this.busy.set(false);
  }

  protected trapFocus(event: KeyboardEvent): void {
    if (event.key !== 'Tab') return;
    const controls = [
      ...this.dialog().nativeElement.querySelectorAll<HTMLElement>(
        'button:not(:disabled), input:not(:disabled), select:not(:disabled)',
      ),
    ];
    const first = controls[0];
    const last = controls[controls.length - 1];
    if (event.shiftKey && this.document.activeElement === first) {
      event.preventDefault();
      last?.focus();
    }
    if (!event.shiftKey && this.document.activeElement === last) {
      event.preventDefault();
      first?.focus();
    }
  }
}
