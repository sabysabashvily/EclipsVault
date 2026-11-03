import { HttpClient } from '@angular/common/http';
import {
  afterNextRender,
  Component,
  DOCUMENT,
  ElementRef,
  inject,
  Injector,
  output,
  signal,
  viewChild,
  DestroyRef,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, Subscription } from 'rxjs';
import { PAGE_LOCATION } from '../shell/command-palette';
import { signInUrl, StepUpProof, UiApiError } from '../ui-api/ui-api-errors';
import type { components } from '../ui-api/schema.generated';

@Component({
  selector: 'app-step-up-dialog',
  template: `
    <dialog
      #dialog
      class="modal verification-dialog"
      tabindex="-1"
      [attr.aria-labelledby]="titleId"
      (cancel)="cancel($event)"
      (keydown)="trapFocus($event)"
    >
      <h2 [id]="titleId">Confirm it's you</h2>
      @if (proof() === 'code') {
        <p>Enter an authenticator code to continue.</p>
        <form (submit)="verify($event, code.value)">
          <div class="field">
            <label [for]="codeId">Authenticator code</label>
            <input
              #code
              [id]="codeId"
              type="text"
              inputmode="numeric"
              autocomplete="one-time-code"
              required
              pattern="[0-9]{6}"
              maxlength="6"
              [disabled]="busy()"
            />
          </div>
          <p class="field-error" role="alert">{{ error() }}</p>
          <div class="button-row">
            <button type="button" class="button" (click)="finish(false)">Cancel</button>
            <button type="submit" class="button primary" [disabled]="busy()">
              Verify and continue
            </button>
          </div>
        </form>
      } @else {
        <p>Sign in again to continue.</p>
        <p class="field-error" role="alert">{{ error() }}</p>
        <div class="button-row">
          <button type="button" class="button" (click)="finish(false)">Cancel</button>
          <button type="button" class="button primary" [disabled]="busy()" (click)="signInAgain()">
            Sign in again
          </button>
        </div>
      }
    </dialog>
  `,
})
export class StepUpDialog {
  private static nextId = 0;
  protected readonly titleId = `step-up-title-${StepUpDialog.nextId++}`;
  protected readonly codeId = `${this.titleId}-code`;
  protected readonly proof = signal<StepUpProof>('code');
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  readonly decision = output<boolean>();
  private readonly document = inject(DOCUMENT);
  protected readonly signIn = signInUrl(this.document.location);
  private readonly http = inject(HttpClient);
  private readonly location = inject(PAGE_LOCATION);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private opener: Element | null = null;
  private verification: Subscription | undefined;

  open(proof: StepUpProof): void {
    if (this.dialog().nativeElement.open) return;
    const active = this.document.activeElement;
    this.opener = active?.closest('details')?.querySelector('summary') ?? active;
    this.proof.set(proof);
    this.error.set('');
    afterNextRender(
      () => {
        const dialog = this.dialog().nativeElement;
        const code = dialog.querySelector('input');
        if (code) code.value = '';
        dialog.showModal();
        dialog.querySelector<HTMLElement>('input, button')?.focus();
      },
      { injector: this.injector },
    );
  }

  protected verify(event: Event, code: string): void {
    event.preventDefault();
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    const request: components['schemas']['StepUpRequest'] = { code };
    this.verification = this.http
      .post<void>('/api/ui/step-up', request)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => this.finish(true),
        error: (error: unknown) =>
          this.error.set(
            error instanceof UiApiError ? error.title : 'Could not verify the code. Try again.',
          ),
      });
  }

  protected signInAgain(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.verification = this.http
      .post('/Account/Logout', null, { responseType: 'text' })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => {
          this.finish(false);
          this.location.assign(this.signIn);
        },
        error: () => this.error.set('Could not sign out. Reload the page to try again.'),
      });
  }

  protected cancel(event: Event): void {
    event.preventDefault();
    this.finish(false);
  }

  protected trapFocus(event: KeyboardEvent): void {
    if (event.key !== 'Tab') return;
    const fields = [
      ...this.dialog().nativeElement.querySelectorAll<HTMLElement>(
        'button:not([disabled]), input:not([disabled]), a[href]',
      ),
    ];
    const current = fields.findIndex((field) => field === this.document.activeElement);
    event.preventDefault();
    fields.at((current + (event.shiftKey ? -1 : 1) + fields.length) % fields.length)?.focus();
  }

  protected finish(verified: boolean): void {
    this.verification?.unsubscribe();
    const dialog = this.dialog().nativeElement;
    const code = dialog.querySelector('input');
    if (code) code.value = '';
    dialog.close();
    this.error.set('');
    if (this.opener instanceof HTMLElement && this.opener.isConnected) this.opener.focus();
    this.opener = null;
    this.decision.emit(verified);
  }
}
