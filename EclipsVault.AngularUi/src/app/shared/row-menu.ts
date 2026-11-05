import {
  afterNextRender,
  Component,
  DOCUMENT,
  ElementRef,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';

@Component({
  selector: 'app-row-menu',
  host: { '(document:click)': 'outside($event)' },
  template: `
    <details #menu class="row-menu" (toggle)="expanded.set(menu.open)">
      <summary
        #trigger
        class="button small"
        aria-haspopup="menu"
        [attr.aria-expanded]="expanded()"
        (keydown)="fromTrigger($event)"
      >
        More
      </summary>
      <div
        #panel
        class="row-menu-panel"
        role="menu"
        tabindex="-1"
        aria-label="More actions"
        (keydown)="navigate($event)"
        (click)="close()"
      >
        <ng-content />
      </div>
    </details>
  `,
})
export class RowMenu {
  protected readonly expanded = signal(false);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly menu = viewChild.required<ElementRef<HTMLDetailsElement>>('menu');
  private readonly trigger = viewChild.required<ElementRef<HTMLElement>>('trigger');
  private readonly panel = viewChild.required<ElementRef<HTMLElement>>('panel');

  protected fromTrigger(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.close();
      return;
    }
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return;
    event.preventDefault();
    this.menu().nativeElement.open = true;
    afterNextRender(
      () =>
        this.items()
          .at(event.key === 'ArrowDown' ? 0 : -1)
          ?.focus(),
      { injector: this.injector },
    );
  }

  protected navigate(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.close();
      this.trigger().nativeElement.focus();
      return;
    }
    if (event.key === 'Tab') {
      this.close();
      return;
    }
    const items = this.items();
    const current = items.findIndex((item) => item === this.document.activeElement);
    let next: number;
    switch (event.key) {
      case 'ArrowDown':
        next = (current + 1) % items.length;
        break;
      case 'ArrowUp':
        next = (current - 1 + items.length) % items.length;
        break;
      case 'Home':
        next = 0;
        break;
      case 'End':
        next = items.length - 1;
        break;
      default:
        return;
    }
    event.preventDefault();
    items.at(next)?.focus();
  }

  protected outside(event: Event): void {
    if (event.target instanceof Node && !this.host.nativeElement.contains(event.target))
      this.close();
  }

  protected close(): void {
    this.menu().nativeElement.open = false;
  }

  private items(): HTMLButtonElement[] {
    return [
      ...this.panel().nativeElement.querySelectorAll<HTMLButtonElement>(
        'button[role="menuitem"]:not([disabled])',
      ),
    ];
  }
}
