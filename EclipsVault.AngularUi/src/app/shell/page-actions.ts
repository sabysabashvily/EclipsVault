import { Injectable, signal, TemplateRef } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class PageActions {
  readonly compact = signal(false);
  readonly primary = signal<TemplateRef<unknown> | null>(null);
}
