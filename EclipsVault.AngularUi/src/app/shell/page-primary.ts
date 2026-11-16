import {
  DestroyRef,
  Directive,
  effect,
  inject,
  TemplateRef,
  untracked,
  ViewContainerRef,
} from '@angular/core';
import { PageActions } from './page-actions';

/** Renders the same action in its page header or the compact top bar, never in both. */
@Directive({ selector: '[appPagePrimary]' })
export class PagePrimary {
  constructor() {
    const actions = inject(PageActions);
    const template = inject<TemplateRef<unknown>>(TemplateRef);
    const container = inject(ViewContainerRef);
    actions.primary.set(template);
    effect(() => {
      const compact = actions.compact();
      untracked(() => {
        container.clear();
        if (!compact) container.createEmbeddedView(template);
      });
    });
    inject(DestroyRef).onDestroy(() => {
      if (actions.primary() === template) actions.primary.set(null);
    });
  }
}
