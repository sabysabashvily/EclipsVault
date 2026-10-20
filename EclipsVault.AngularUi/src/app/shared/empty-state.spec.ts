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
    expect([...host.children].map((child) => `${child.tagName}.${child.className}`)).toEqual([
      'DIV.empty-state',
    ]);
    const state = host.firstElementChild!;
    expect([...state.children].map((child) => child.tagName)).toEqual(['DIV', 'H3', 'P']);
    expect(state.querySelector(':scope > .empty-icon > svg')).not.toBeNull();
    expect(state.querySelector('h3')?.textContent).toBe('No activity yet');
    expect(state.querySelector('p')?.textContent).toBe('Each action is recorded here.');
  });

  it.each([
    [
      'clipboard',
      [
        'path:d=M9 4h6a1 1 0 0 1 1 1v1h2a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h2V5a1 1 0 0 1 1-1Z',
        'path:d=M9 14l2 2 4-4',
      ],
    ],
    [
      'lock',
      [
        'rect:x=4,y=10,width=16,height=10,rx=2',
        'path:d=M8 10V7a4 4 0 0 1 8 0v3',
        'circle:cx=12,cy=15,r=1.4',
      ],
    ],
  ] as const)('draws the Legacy %s icon', async (icon, shapes) => {
    const fixture = TestBed.createComponent(EmptyState);
    fixture.componentRef.setInput('icon', icon);
    fixture.componentRef.setInput('heading', 'Heading');
    fixture.componentRef.setInput('message', 'Message');
    await fixture.whenStable();
    const svg = (fixture.nativeElement as HTMLElement).querySelector('.empty-icon > svg')!;

    expect(
      [...svg.children].map(
        (shape) =>
          `${shape.tagName}:${[...shape.attributes].map((a) => `${a.name}=${a.value}`).join(',')}`,
      ),
    ).toEqual(shapes);
  });
});
