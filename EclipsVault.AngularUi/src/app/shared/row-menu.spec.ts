import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { RowMenu } from './row-menu';

@Component({
  imports: [RowMenu],
  template: `<app-row-menu
      ><a role="menuitem" href="/first">First</a
      ><button role="menuitem" disabled>Unavailable</button
      ><button role="menuitem">Last</button></app-row-menu
    ><button>Outside</button>`,
})
class MenuHost {}

describe('RowMenu', () => {
  it('supports arrow keys, Home, End and Escape over links and buttons, skipping disabled ones', async () => {
    const fixture = TestBed.createComponent(MenuHost);
    fixture.detectChanges();
    const page: HTMLElement = fixture.nativeElement;
    const trigger = page.querySelector('summary');
    const first = page.querySelector('a');
    const last = page.querySelectorAll('button')[1];
    if (!trigger || !first) throw new Error('No trigger or link');
    trigger.focus();
    trigger.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }),
    );
    await fixture.whenStable();
    expect(document.activeElement).toBe(first);
    first.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }),
    );
    expect(document.activeElement).toBe(last);
    last.dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true }));
    expect(document.activeElement).toBe(first);
    first.dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
    expect(document.activeElement).toBe(last);
    last.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(document.activeElement).toBe(trigger);
    expect(page.querySelector('details')?.open).toBe(false);
    trigger.click();
    expect(page.querySelector('details')?.open).toBe(true);
    trigger.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(page.querySelector('details')?.open).toBe(false);
    expect(document.activeElement).toBe(trigger);
  });
  it('opens at the last item with ArrowUp and closes on outside click', async () => {
    const fixture = TestBed.createComponent(MenuHost);
    fixture.detectChanges();
    const page: HTMLElement = fixture.nativeElement;
    const trigger = page.querySelector('summary');
    if (!trigger) throw new Error('No trigger');
    trigger.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true, cancelable: true }),
    );
    await fixture.whenStable();
    expect(document.activeElement?.textContent).toBe('Last');
    const outside = page.querySelectorAll('button')[2];
    outside.click();
    expect(page.querySelector('details')?.open).toBe(false);
  });
});
