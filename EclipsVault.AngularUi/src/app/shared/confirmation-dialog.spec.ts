import { TestBed } from '@angular/core/testing';
import { ConfirmationDialog } from './confirmation-dialog';

describe('ConfirmationDialog', () => {
  async function render() {
    const opener = document.createElement('button');
    opener.textContent = 'Sign out';
    document.body.append(opener);
    const fixture = TestBed.createComponent(ConfirmationDialog);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const dialog = host.querySelector('dialog')!;
    // JSDOM has no modal-dialog implementation; the real browser pins its focus trap and Escape key.
    dialog.showModal = () => dialog.setAttribute('open', '');
    dialog.close = () => dialog.removeAttribute('open');
    const decision = vi.fn();
    fixture.componentInstance.decision.subscribe(decision);
    opener.focus();
    fixture.componentInstance.open('Sign out this device? It loses access on its next request.');
    await fixture.whenStable();
    return { fixture, dialog, opener, decision };
  }

  afterEach(() => document.body.querySelectorAll('button').forEach((button) => button.remove()));

  it('announces the confirmation text and puts focus on Cancel', async () => {
    const { dialog } = await render();
    const message = document.getElementById(dialog.getAttribute('aria-labelledby')!);

    expect(dialog.open).toBe(true);
    expect(dialog.getAttribute('role')).toBe('alertdialog');
    expect(message?.textContent).toBe('Sign out this device? It loses access on its next request.');
    expect(document.activeElement?.textContent).toBe('Cancel');
  });

  it('Cancel closes the dialog, returns focus to its opener and declines the action', async () => {
    const { dialog, opener, decision, fixture } = await render();
    dialog.querySelector<HTMLButtonElement>('button')!.click();
    await fixture.whenStable();

    expect(dialog.open).toBe(false);
    expect(document.activeElement).toBe(opener);
    expect(decision).toHaveBeenCalledExactlyOnceWith(false);
  });

  it('Tab and Shift+Tab cycle between Cancel and Continue', async () => {
    const { dialog } = await render();
    const backwards = new KeyboardEvent('keydown', {
      key: 'Tab',
      shiftKey: true,
      bubbles: true,
      cancelable: true,
    });
    dialog.dispatchEvent(backwards);

    expect(backwards.defaultPrevented).toBe(true);
    expect(document.activeElement?.textContent?.trim()).toBe('Continue');
    dialog.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }),
    );
    expect(document.activeElement?.textContent?.trim()).toBe('Cancel');
  });

  it('a native Escape cancellation returns focus and declines the action', async () => {
    const { dialog, opener, decision, fixture } = await render();
    const event = new Event('cancel', { cancelable: true });
    dialog.dispatchEvent(event);
    await fixture.whenStable();

    expect(event.defaultPrevented).toBe(true);
    expect(dialog.open).toBe(false);
    expect(document.activeElement).toBe(opener);
    expect(decision).toHaveBeenCalledExactlyOnceWith(false);
  });

  it('Continue confirms once and returns focus to its opener', async () => {
    const { dialog, opener, decision, fixture } = await render();
    dialog.querySelector<HTMLButtonElement>('button.danger')!.click();
    await fixture.whenStable();

    expect(dialog.open).toBe(false);
    expect(document.activeElement).toBe(opener);
    expect(decision).toHaveBeenCalledExactlyOnceWith(true);
  });
});
