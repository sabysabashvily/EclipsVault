import { httpResource } from '@angular/common/http';
import {
  afterNextRender,
  Component,
  ElementRef,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';
import { ScrollRegion } from '../shared/scroll-region';
import type { paths } from '../ui-api/schema.generated';

type Access = paths['/api/ui/access']['get']['responses'][200]['content']['application/json'];
type Row = Access['rows'][number];
type Cell = Row['cells'][number];

/** A live, self-scoped explanation. The server makes every access decision. */
@Component({
  selector: 'app-access-page',
  imports: [ScrollRegion],
  templateUrl: './access-page.html',
  // Keep the spaces between an address and its badge, and a badge and the window hours.
  preserveWhitespaces: true,
})
export class AccessPage {
  protected readonly access = httpResource<Access>(() => '/api/ui/access');
  /** The denied cell whose reason the side panel explains. */
  protected readonly selected = signal<{ row: Row; cell: Cell } | null>(null);
  private readonly panel = viewChild.required<ElementRef<HTMLDialogElement>>('panel');
  private readonly injector = inject(Injector);
  private opener: HTMLElement | null = null;

  protected windowRange(access: Access): string {
    return `${String(access.windowStartHour).padStart(2, '0')}:00–${String(access.windowEndHour).padStart(2, '0')}:00 ${access.windowZone}`;
  }

  protected explain(row: Row, cell: Cell, event: Event): void {
    this.opener = event.currentTarget as HTMLElement;
    this.selected.set({ row, cell });
    // Open once the panel holds this cell's content, so focus lands on its close button.
    afterNextRender(() => this.panel().nativeElement.showModal(), { injector: this.injector });
  }

  /** Escape, Close or the close circle: focus goes back to the cell that opened the panel. */
  protected closed(): void {
    this.opener?.focus();
    this.opener = null;
  }
}
