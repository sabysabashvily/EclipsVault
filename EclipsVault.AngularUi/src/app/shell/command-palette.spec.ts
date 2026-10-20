import { HttpHeaders, HttpResponse } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { appConfig } from '../app.config';
import {
  CommandPalette,
  commandChord,
  PAGE_LOCATION,
  paletteCommands,
  secretsFrom,
} from './command-palette';

const hit = {
  id: '6d1b2c55-0000-4000-8000-000000000001',
  name: 'Phoenix_Db_Password',
  project: 'PHOENIX',
  environment: 'Development',
  sensitivity: 1,
  sensitivityName: 'Internal',
  url: '/Secrets/Details/6d1b2c55-0000-4000-8000-000000000001',
};
const json = { headers: { 'Content-Type': 'application/json; charset=utf-8' } };

/** The sidebar's links in its order, as sidebar.spec.ts pins them, then the account menu's Profile. */
const standardPages = [
  ['Overview', '/'],
  ['Secrets', '/Secrets'],
  ['New secret', '/Secrets/Create'],
  ['Dynamic secrets', '/DynamicSecrets'],
  ['Shared with me', '/Secrets/SharedWithMe'],
  ['Shared by me', '/Secrets/SharedByMe'],
  ['My access', '/Access'],
  ['Access requests', '/AccessRequests'],
  ['Security checkup', '/Security'],
  ['Sign-in history', '/SignIns'],
  ['Signed-in devices', '/Sessions'],
  ['My activity', '/Activity'],
  ['Your data', '/Data'],
];
const adminPages = [
  ['Users', '/Admin/Users'],
  ['Networks', '/Admin/Networks'],
  ['Service accounts', '/ServiceAccounts'],
  ['Audit log', '/Audit'],
  ['Notifications', '/Notifications'],
  ['Encryption keys', '/Encryption'],
  ['License', '/License'],
];

function sidebarElement(pages = [...standardPages, ['Profile', '/Profile']]): HTMLElement {
  const sidebar = document.createElement('aside');
  const nav = document.createElement('nav');
  nav.className = 'sidenav';
  sidebar.append(nav);
  for (const [label, url] of pages) {
    const link = document.createElement('a');
    link.textContent = label;
    link.href = url;
    if (url === '/Access') link.setAttribute('data-command-angular', '');
    nav.append(link);
  }
  return sidebar;
}

describe('paletteCommands', () => {
  it('offers exactly the sidebar links, in order, then Profile from the account menu', () => {
    for (const expected of [standardPages, [...standardPages, ...adminPages]]) {
      const pages = paletteCommands(sidebarElement([...expected, ['Profile', '/Profile']]));
      expect(pages.map(({ label, url }) => [label, url])).toEqual([
        ...expected,
        ['Profile', '/Profile'],
      ]);
      expect(pages.every((page) => page.kind === 'Go to')).toBe(true);
      expect(pages.filter((page) => page.angular).map((page) => page.url)).toEqual(['/Access']);
    }
  });

  it('never offers a page the server did not authorize', () => {
    const labels = paletteCommands(sidebarElement()).map((page) => page.label);
    for (const admin of [
      'Users',
      'Networks',
      'Service accounts',
      'Audit log',
      'Notifications',
      'Encryption keys',
      'License',
    ]) {
      expect(labels).not.toContain(admin);
    }
    expect(paletteCommands(sidebarElement([]))).toEqual([]);
    expect(
      paletteCommands(sidebarElement([['My access', '/Access']])).map((page) => page.label),
    ).toEqual(['My access']);
  });
});

describe('commandChord', () => {
  it('shows ⌘K on Apple platforms and Ctrl K everywhere else, as the Legacy UI does', () => {
    expect(commandChord('MacIntel')).toBe('⌘K');
    expect(commandChord('iPad')).toBe('⌘K');
    expect(commandChord('Win32')).toBe('Ctrl K');
    expect(commandChord('Linux x86_64')).toBe('Ctrl K');
  });
});

describe('secretsFrom', () => {
  it('rejects a redirected response even when its destination answers valid JSON', () => {
    const response = new HttpResponse({
      body: JSON.stringify([hit]),
      headers: new HttpHeaders(json.headers),
      redirected: true,
    });
    expect(() => secretsFrom(response, 'https://vault.example')).toThrow(/redirected/);
  });
});

describe('CommandPalette', () => {
  const assign = vi.fn();
  let backend: HttpTestingController;
  let opener: HTMLButtonElement;

  async function render() {
    TestBed.configureTestingModule({
      providers: [
        ...appConfig.providers,
        provideHttpClientTesting(),
        { provide: PAGE_LOCATION, useValue: { assign } },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(CommandPalette);
    fixture.componentRef.setInput('sidebar', sidebarElement());
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const stable = () => fixture.whenStable();
    const input = () => host.querySelector<HTMLInputElement>('.palette-input');
    const press = async (key: string, init: KeyboardEventInit = {}) => {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
      (input() ?? document.body).dispatchEvent(event);
      await stable();
      return event;
    };
    const type = async (text: string) => {
      input()!.value = text;
      input()!.dispatchEvent(new Event('input'));
      await stable();
    };
    const items = () =>
      [...host.querySelectorAll('.palette-item')].map((item) => ({
        kind: item.querySelector('.palette-kind')?.textContent?.trim(),
        label: item.querySelector('.palette-label')?.textContent?.trim(),
        active:
          item.classList.contains('is-active') && item.getAttribute('aria-selected') === 'true',
      }));
    const open = async () => {
      opener.focus();
      await press('k', { metaKey: true });
    };
    return { host, fixture, input, press, type, items, open, stable };
  }

  beforeEach(() => {
    // jsdom lays nothing out, so it has no scrollIntoView for the highlight to call.
    Element.prototype.scrollIntoView = vi.fn();
    assign.mockReset();
    opener = document.createElement('button');
    document.body.append(opener);
  });

  afterEach(() => {
    backend.verify();
    opener.remove();
  });

  // Real time: Angular's zoneless scheduler runs on the same timers that fake timers would freeze.
  const pause = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

  /** Lets the debounce elapse and returns the one search request it sent. */
  async function searched(stable: () => Promise<void>, q: string): Promise<TestRequest> {
    await pause(200);
    await stable();
    return backend.expectOne(
      (request) => request.url === '/Secrets/Search' && request.params.get('q') === q,
    );
  }

  it('opens on ⌘K or Ctrl K as a modal dialog listing the sidebar pages, focused on its field', async () => {
    const { host, input, press, items } = await render();
    expect(host.querySelector('[role="dialog"]')).toBeNull();

    await press('k', { ctrlKey: true });

    const dialog = host.querySelector('[role="dialog"]');
    expect(dialog?.getAttribute('aria-modal')).toBe('true');
    expect(document.activeElement).toBe(input());
    expect(items().map((item) => item.label)).toEqual([
      ...standardPages.map(([label]) => label),
      'Profile',
    ]);
    expect(items()[0].active).toBe(true);

    await press('K', { metaKey: true });
    expect(host.querySelector('[role="dialog"]')).toBeNull();
  });

  it('filters pages locally and never asks the server about a one-character query', async () => {
    const { open, type, items, stable } = await render();
    await open();

    await type('s');
    await pause(400);
    await stable();

    backend.expectNone(() => true);
    expect(items().every((item) => item.kind === 'Go to')).toBe(true);
    expect(items().map((item) => item.label)).toContain('Secrets');
  });

  it('debounces the search, shows the server answer as text before the matching pages, and never as markup', async () => {
    const { host, open, type, items, stable } = await render();
    await open();

    await type('sec');
    backend.expectNone(() => true);
    const request = await searched(stable, 'sec');
    expect(request.request.headers.get('Accept')).toBe('application/json');

    const hostile = {
      ...hit,
      name: '<img src=x data-probe="name">',
      project: '<b>P</b>',
      environment: 'Development',
    };
    request.flush(
      JSON.stringify([hostile, { ...hit, sensitivity: 4, sensitivityName: 'Top Secret' }]),
      json,
    );
    await stable();

    expect(items().slice(0, 2)).toEqual([
      { kind: 'Secret', label: '<img src=x data-probe="name">', active: true },
      { kind: 'Secret', label: 'Phoenix_Db_Password', active: false },
    ]);
    expect(
      items()
        .slice(2)
        .map((item) => item.label),
    ).toEqual(['Secrets', 'New secret', 'Dynamic secrets', 'Security checkup']);
    expect(host.querySelector('[data-probe], .palette-list b')).toBeNull();
    expect(host.querySelector('.palette-meta')?.textContent).toBe('<b>P</b> · Development');
    expect(
      [...host.querySelectorAll('.palette-item .badge')].map((badge) => [
        badge.className,
        badge.textContent?.trim(),
      ]),
    ).toEqual([
      ['badge sens-1', 'Internal'],
      ['badge sens-4', 'Top Secret'],
    ]);
  });

  it('cancels a stale search, so an older answer never replaces a newer one', async () => {
    const { open, type, items, stable } = await render();
    await open();

    await type('pho');
    const older = await searched(stable, 'pho');
    await type('phoe');
    const newer = await searched(stable, 'phoe');

    expect(older.cancelled).toBe(true);
    newer.flush(JSON.stringify([hit]), json);
    await stable();
    expect(items().map((item) => item.label)).toEqual(['Phoenix_Db_Password']);
  });

  it('cancels an in-flight search when the query shrinks below two characters or the palette closes', async () => {
    const { open, type, press, stable } = await render();
    await open();

    await type('ph');
    const shrunk = await searched(stable, 'ph');
    await type('p');
    expect(shrunk.cancelled).toBe(true);

    await type('ph');
    const closed = await searched(stable, 'ph');
    await press('Escape');
    expect(closed.cancelled).toBe(true);
  });

  it('reports a sign-in redirect, a non-JSON answer, an unexpected shape or a refusal, keeping the pages', async () => {
    const { host, open, type, items, stable } = await render();
    await open();
    const answers: ((request: TestRequest) => void)[] = [
      // Fetch follows the Legacy UI's sign-in redirect for an expired session to an HTML page.
      (request) =>
        request.flush('<!doctype html><title>Sign in</title>', {
          headers: { 'Content-Type': 'text/html; charset=utf-8' },
        }),
      (request) => request.flush('not json', json),
      (request) => request.flush(JSON.stringify({ secrets: [hit] }), json),
      (request) =>
        request.flush(JSON.stringify([{ ...hit, url: '//elsewhere.example/steal' }]), json),
      // The URL parser drops the tab, leaving //elsewhere.example: another origin.
      (request) =>
        request.flush(JSON.stringify([{ ...hit, url: '/\t/elsewhere.example/steal' }]), json),
      // No placeholder host may stand in for the vault's actual origin.
      (request) => request.flush(JSON.stringify([{ ...hit, url: '//vault.invalid/steal' }]), json),
      (request) => request.flush(JSON.stringify([{ ...hit, url: '/\\vault.invalid/steal' }]), json),
      (request) =>
        request.flush(JSON.stringify([{ ...hit, url: '/\n/vault.invalid/steal' }]), json),
      (request) => request.flush(JSON.stringify([{ ...hit, sensitivity: '1" onclick="x' }]), json),
      (request) => request.flush('', { status: 500, statusText: 'Internal Server Error' }),
    ];

    // A distinct query each time, so a failure shown earlier cannot stand in for this one.
    const queries = [
      'Secrets',
      'secrets',
      'SECRETS',
      'sEcrets',
      'seCrets',
      'secRets',
      'SeCrets',
      'SecRets',
      'SEcrets',
      'secrEts',
    ];
    for (const [i, answer] of answers.entries()) {
      await type(queries[i]);
      expect(host.querySelector('.palette-error')).toBeNull();
      answer(await searched(stable, queries[i]));
      await stable();

      expect(host.querySelector('.palette-error[role="alert"]')?.textContent).toContain(
        'Secret search failed',
      );
      expect(items().map((item) => [item.kind, item.label])).toEqual([
        ['Go to', 'Secrets'],
        ['Go to', 'Dynamic secrets'],
      ]);
    }

    await type('secreTs');
    (await searched(stable, 'secreTs')).flush(JSON.stringify([]), json);
    await stable();
    expect(host.querySelector('.palette-error')).toBeNull();
  });

  it('moves with the arrow keys, opens a Legacy UI page on Enter, and traps Tab in its field', async () => {
    const { open, press, items, input } = await render();
    await open();

    await press('ArrowUp');
    expect(items()[0].active).toBe(true);
    await press('ArrowDown');
    await press('ArrowDown');
    expect(items().findIndex((item) => item.active)).toBe(2);
    expect(input()?.getAttribute('aria-activedescendant')).toBe('palette-option-2');

    const tab = await press('Tab');
    expect(tab.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(input());

    await press('Enter');
    expect(assign).toHaveBeenCalledExactlyOnceWith('/Secrets/Create');
  });

  it('opens a page the Angular UI owns through the router, without a page load', async () => {
    const { open, type, press } = await render();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await open();

    await type('my access');
    await press('Enter');

    expect(navigate).toHaveBeenCalledExactlyOnceWith('/Access');
    expect(assign).not.toHaveBeenCalled();
  });

  it('closes on Escape and hands focus back to where it was', async () => {
    const { host, open, press } = await render();
    await open();
    expect(document.activeElement).not.toBe(opener);

    await press('Escape');

    expect(host.querySelector('[role="dialog"]')).toBeNull();
    expect(document.activeElement).toBe(opener);
  });
});
