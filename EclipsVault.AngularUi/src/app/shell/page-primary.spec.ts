import { NgTemplateOutlet } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { PageActions } from './page-actions';
import { PagePrimary } from './page-primary';

@Component({
  imports: [NgTemplateOutlet, PagePrimary],
  template: `
    <header>
      @if (actions.compact()) {
        <ng-container [ngTemplateOutlet]="actions.primary()" />
      }
    </header>
    <main>
      @if (visible()) {
        <button *appPagePrimary [disabled]="disabled()" (click)="clicks.set(clicks() + 1)">
          Download
        </button>
      }
    </main>
  `,
})
class ActionPage {
  readonly actions = inject(PageActions);
  readonly disabled = signal(false);
  readonly visible = signal(true);
  readonly clicks = signal(0);
}

describe('A page primary action', () => {
  it('moves to the compact top bar and back with one control and the same handler', async () => {
    const fixture = TestBed.createComponent(ActionPage);
    await fixture.whenStable();
    const page: HTMLElement = fixture.nativeElement;
    expect(page.querySelector('main button')?.textContent?.trim()).toBe('Download');

    fixture.componentInstance.actions.compact.set(true);
    await fixture.whenStable();
    expect(page.querySelectorAll('button')).toHaveLength(1);
    page.querySelector<HTMLButtonElement>('header button')!.click();
    expect(fixture.componentInstance.clicks()).toBe(1);

    fixture.componentInstance.actions.compact.set(false);
    await fixture.whenStable();
    expect(page.querySelectorAll('button')).toHaveLength(1);
    page.querySelector<HTMLButtonElement>('main button')!.click();
    expect(fixture.componentInstance.clicks()).toBe(2);
  });

  it('preserves the disabled state when it moves', async () => {
    const fixture = TestBed.createComponent(ActionPage);
    fixture.componentInstance.disabled.set(true);
    fixture.componentInstance.actions.compact.set(true);
    await fixture.whenStable();
    const page: HTMLElement = fixture.nativeElement;
    const button = page.querySelector<HTMLButtonElement>('header button')!;

    expect(button.disabled).toBe(true);
    button.click();
    expect(fixture.componentInstance.clicks()).toBe(0);
  });

  it('removes a conditional action from the top bar when the page removes it', async () => {
    const fixture = TestBed.createComponent(ActionPage);
    fixture.componentInstance.actions.compact.set(true);
    await fixture.whenStable();
    const page: HTMLElement = fixture.nativeElement;
    expect(page.querySelector('header button')?.textContent?.trim()).toBe('Download');

    fixture.componentInstance.visible.set(false);
    await fixture.whenStable();
    expect(page.querySelectorAll('button')).toHaveLength(0);
  });
});
