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

@Component({
  selector: 'app-confirmation-dialog',
  template: `
    <dialog
      #dialog
      class="modal confirmation-dialog"
      role="alertdialog"
      [attr.aria-labelledby]="messageId"
      (cancel)="cancel($event)"
    >
      <p [id]="messageId">{{ message() }}</p>
      <div class="button-row">
        <button #cancelButton type="button" class="button" (click)="finish(false)">Cancel</button>
        <button type="button" class="button danger" (click)="finish(true)">Continue</button>
      </div>
    </dialog>
  `,
})
export class ConfirmationDialog {
  private static nextId = 0;
  protected readonly messageId = `confirmation-message-${ConfirmationDialog.nextId++}`;
  protected readonly message = signal('');
  readonly decision = output<boolean>();

  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly cancelButton = viewChild.required<ElementRef<HTMLButtonElement>>('cancelButton');
  private opener: Element | null = null;

  open(message: string): void {
    if (this.dialog().nativeElement.open) return;
    this.opener = this.document.activeElement;
    this.message.set(message);
    // Render its accessible name before opening. The browser then makes the rest of the page inert.
    afterNextRender(
      () => {
        this.dialog().nativeElement.showModal();
        this.cancelButton().nativeElement.focus();
      },
      { injector: this.injector },
    );
  }

  protected cancel(event: Event): void {
    event.preventDefault();
    this.finish(false);
  }

  protected finish(confirmed: boolean): void {
    this.dialog().nativeElement.close();
    if (this.opener instanceof HTMLElement && this.opener.isConnected) this.opener.focus();
    this.opener = null;
    this.decision.emit(confirmed);
  }
}
