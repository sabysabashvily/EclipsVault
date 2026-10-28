import {
  Component,
  DOCUMENT,
  DestroyRef,
  ElementRef,
  afterNextRender,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { Title } from '@angular/platform-browser';

/**
 * The one showing of a new recovery-code set. The codes live only in the Profile page's memory, so
 * this view has nothing to restore after a refresh or a direct visit.
 */
@Component({
  selector: 'app-recovery-codes',
  templateUrl: './recovery-codes.html',
})
export class RecoveryCodes {
  readonly codes = input.required<readonly string[]>();
  readonly done = output<void>();
  private readonly document = inject(DOCUMENT);
  private readonly heading = viewChild.required<ElementRef<HTMLHeadingElement>>('heading');

  constructor() {
    const title = inject(Title);
    const profileTitle = title.getTitle();
    title.setTitle('Recovery codes — EclipsVault');
    inject(DestroyRef).onDestroy(() => title.setTitle(profileTitle));
    // The verification dialog's opener is gone with the Profile cards; announce the codes instead.
    afterNextRender(() => this.heading().nativeElement.focus());
  }

  protected print(): void {
    this.document.defaultView?.print();
  }
}
