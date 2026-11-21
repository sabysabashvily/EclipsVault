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
} from '@angular/core';

/** What a decision dialog says beyond its message. Every field is optional; the defaults keep the
 *  plain "Cancel / Continue" confirmation that pages without a title still use. */
export interface DecisionOptions {
  title?: string;
  /** The confirm button's label: a verb and its object, such as "Revoke access". */
  confirm?: string;
  tone?: 'danger' | 'primary';
  /** Label and value rows that name exactly what the decision touches. */
  facts?: readonly (readonly [string, string])[];
  /** Said beside the buttons, such as "Saved to the audit log". */
  note?: string;
}

const focusable = 'button, input, select, textarea, a[href], [tabindex]:not([tabindex="-1"])';

@Component({
  selector: 'app-confirmation-dialog',
  template: `
    <dialog
      #dialog
      class="modal confirmation-dialog"
      role="alertdialog"
      [attr.aria-labelledby]="options().title ? titleId : messageId"
      [attr.aria-describedby]="options().title ? messageId : null"
      (cancel)="cancel($event)"
      (keydown)="trapFocus($event)"
    >
      @if (options().title; as title) {
        <h2 class="dialog-title" [id]="titleId">{{ title }}</h2>
      }
      <p [id]="messageId">{{ message() }}</p>
      @if (options().facts?.length) {
        <dl class="facts">
          @for (fact of options().facts; track $index) {
            <div>
              <dt>{{ fact[0] }}</dt>
              <dd>{{ fact[1] }}</dd>
            </div>
          }
        </dl>
      }
      <div #extra class="dialog-extra"><ng-content /></div>
      <div class="button-row">
        @if (options().note; as note) {
          <p class="dialog-note">{{ note }}</p>
        }
        <button #cancelButton type="button" class="button" (click)="finish(false)">Cancel</button>
        <button
          type="button"
          class="button"
          [class.danger]="(options().tone ?? 'danger') === 'danger'"
          [class.primary]="options().tone === 'primary'"
          (click)="finish(true)"
        >
          {{ options().confirm ?? 'Continue' }}
        </button>
      </div>
    </dialog>
  `,
})
export class ConfirmationDialog {
  private static nextId = 0;
  private readonly id = ConfirmationDialog.nextId++;
  protected readonly messageId = `confirmation-message-${this.id}`;
  protected readonly titleId = `confirmation-title-${this.id}`;
  protected readonly message = signal('');
  protected readonly options = signal<DecisionOptions>({});
  readonly decision = output<boolean>();

  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly extra = viewChild.required<ElementRef<HTMLElement>>('extra');
  private readonly cancelButton = viewChild.required<ElementRef<HTMLButtonElement>>('cancelButton');
  private opener: Element | null = null;

  open(message: string, options: DecisionOptions = {}): void {
    if (this.dialog().nativeElement.open) return;
    const active = this.document.activeElement;
    this.opener = active?.closest('details')?.querySelector('summary') ?? active;
    this.message.set(message);
    this.options.set(options);
    // Render its accessible name before opening. The browser then makes the rest of the page inert.
    afterNextRender(
      () => {
        this.dialog().nativeElement.showModal();
        // A field the page projected in is what the user came to fill; otherwise the safe choice.
        const field = this.extra().nativeElement.querySelector<HTMLElement>(focusable);
        (field ?? this.cancelButton().nativeElement).focus();
      },
      { injector: this.injector },
    );
  }

  protected cancel(event: Event): void {
    event.preventDefault();
    this.finish(false);
  }

  protected trapFocus(event: KeyboardEvent): void {
    if (event.key !== 'Tab') return;
    // A native dialog can tab into browser chrome; keep its controls in one focus cycle.
    event.preventDefault();
    const controls = [
      ...this.dialog().nativeElement.querySelectorAll<HTMLElement>(focusable),
    ].filter((control) => !control.matches(':disabled'));
    const at = controls.indexOf(this.document.activeElement as HTMLElement);
    const step = event.shiftKey ? -1 : 1;
    const next = at < 0 ? (event.shiftKey ? controls.length - 1 : 0) : at + step;
    controls[(next + controls.length) % controls.length]?.focus();
  }

  protected finish(confirmed: boolean): void {
    this.dialog().nativeElement.close();
    if (this.opener instanceof HTMLElement && this.opener.isConnected) this.opener.focus();
    this.opener = null;
    this.decision.emit(confirmed);
  }
}
