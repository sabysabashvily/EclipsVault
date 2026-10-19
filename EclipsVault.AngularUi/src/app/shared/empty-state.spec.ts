import { TestBed } from '@angular/core/testing';
import { EmptyState } from './empty-state';

describe('EmptyState', () => {
  it('renders the Legacy partial: a div holding the icon, the heading and the message', async () => {
    const fixture = TestBed.createComponent(EmptyState);
    fixture.componentRef.setInput('icon', 'clipboard');
    fixture.componentRef.setInput('heading', 'No activity yet');
    fixture.componentRef.setInput('message', 'Each action is recorded here.');
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;

    expect(host.classList.length).toBe(0);
    expect([...host.children].map((child) => `${child.tagName}.${child.className}`)).toEqual(['DIV.empty-state']);
    const state = host.firstElementChild!;
    expect([...state.children].map((child) => child.tagName)).toEqual(['DIV', 'H3', 'P']);
    expect(state.querySelector(':scope > .empty-icon > svg')).not.toBeNull();
    expect(state.querySelector('h3')?.textContent).toBe('No activity yet');
    expect(state.querySelector('p')?.textContent).toBe('Each action is recorded here.');
  });
});
