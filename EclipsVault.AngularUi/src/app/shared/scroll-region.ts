import {
  afterNextRender,
  DestroyRef,
  Directive,
  DOCUMENT,
  ElementRef,
  inject,
  signal,
} from '@angular/core';

@Directive({
  selector: '[appScrollRegion]',
  host: {
    '[class.has-overflow]': 'overflowing()',
    '[attr.tabindex]': 'overflowing() ? 0 : null',
  },
})
export class ScrollRegion {
  protected readonly overflowing = signal(false);

  constructor() {
    const region = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    const browser = inject(DOCUMENT).defaultView;
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      const table = region.querySelector('table');
      if (!browser?.ResizeObserver || !table) return;

      // Intrinsic table width changes with data and fonts, even when the region keeps its width.
      const observer = new browser.ResizeObserver(() => {
        this.overflowing.set(region.scrollWidth > region.clientWidth);
      });
      observer.observe(region);
      observer.observe(table);
      destroyRef.onDestroy(() => observer.disconnect());
    });
  }
}
