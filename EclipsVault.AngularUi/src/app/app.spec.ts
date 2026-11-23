import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Me } from './account/me';
import { App } from './app';
import { appConfig } from './app.config';
import { APPLE_PLATFORM } from './shell/shortcuts';

const me: Me = {
  username: 'vault-admin',
  displayName: 'Vault Administrator',
  clearance: 'Top Secret',
  project: 'GLOBAL',
  areas: ['access', 'profile', 'license'],
  avatarUrl: '/Profile/Avatar?v=0',
  licenseNotice: null,
};

describe('App shell', () => {
  let backend: HttpTestingController;

  async function render(
    answer: (request: ReturnType<HttpTestingController['expectOne']>) => void,
    apple = false,
  ) {
    TestBed.configureTestingModule({
      providers: [
        ...appConfig.providers,
        provideHttpClientTesting(),
        { provide: APPLE_PLATFORM, useValue: apple },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(App);
    // Not whenStable(): it would wait for the very request this test answers.
    fixture.detectChanges();
    answer(backend.expectOne('/api/ui/me'));
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  afterEach(() => {
    localStorage.removeItem('eclipsvault.sidebar.collapsed');
    localStorage.removeItem('eclipsvault.sidebar.peek');
    delete document.documentElement.dataset['theme'];
    vi.restoreAllMocks();
    backend.verify();
  });

  it('shows the problem code instead of a blank sidebar when /api/ui/me fails', async () => {
    const shell = await render((request) =>
      request.flush(
        { status: 503, code: 'audit_unavailable', title: 'The audit trail is unavailable.' },
        { status: 503, statusText: 'Service Unavailable' },
      ),
    );

    const alert = shell.querySelector('.sidebar [role="alert"]');
    expect(alert?.textContent).toContain('audit_unavailable');
    expect(shell.querySelector('.sidenav')).toBeNull();
    expect(shell.querySelector('.account-menu')).toBeNull();
    expect(shell.querySelector('.search-pill')).toBeNull();
  });

  it('opens the command palette from the search pill at the top of the page, outside the sidebar', async () => {
    const shell = await render((request) => request.flush(me));
    const trigger = shell.querySelector<HTMLButtonElement>('.content > .topbar > .search-pill');
    expect(trigger?.getAttribute('aria-label')).toBe('Search secrets or jump to a page');
    expect(trigger?.querySelector('kbd')?.textContent).toMatch(/^(⌘K|Ctrl K)$/);

    // The palette must read what was rendered, including the account-menu link's current label/URL.
    const profile = shell.querySelector<HTMLAnchorElement>('.account-menu-panel a')!;
    profile.textContent = 'Account details';
    profile.href = '/Profile/Index';
    shell.querySelector('.sidenav a[href="/License"]')!.remove();
    trigger!.click();
    await TestBed.inject(ApplicationRef).whenStable();

    const dialog = shell.querySelector('[role="dialog"]');
    expect(dialog?.closest('.sidebar')).toBeNull();
    expect(document.activeElement).toBe(dialog?.querySelector('.palette-input'));
    expect([...shell.querySelectorAll('.palette-label')].map((label) => label.textContent)).toEqual(
      ['My access', 'Account details'],
    );

    profile.remove();
    dialog!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    trigger!.click();
    await TestBed.inject(ApplicationRef).whenStable();
    expect([...shell.querySelectorAll('.palette-label')].map((label) => label.textContent)).toEqual(
      ['My access'],
    );
  });

  it('renders the account menu and no licence notice when the server sends none', async () => {
    const shell = await render((request) => request.flush(me));

    expect(
      shell.querySelector('.sidebar-footer .account-menu .user-name')?.textContent?.trim(),
    ).toBe('Vault Administrator');
    expect(shell.querySelector('.notice')).toBeNull();
    expect(shell.textContent).not.toContain('unlicensed');
  });

  it('renders the licence notice the server sends, linking to the licence page', async () => {
    const shell = await render((request) =>
      request.flush({
        ...me,
        licenseNotice: {
          title: 'Running unlicensed.',
          text: 'No license key is installed. This changes nothing.',
        },
      }),
    );

    const notice = shell.querySelector('.content .notice.notice-warn');
    expect(notice?.querySelector('strong')?.textContent).toBe('Running unlicensed.');
    expect(notice?.textContent).toContain('No license key is installed. This changes nothing.');
    expect(notice?.querySelector('a')?.getAttribute('href')).toBe('/License');
  });

  it('collapses and expands the desktop sidebar, persisting the preference across loads', async () => {
    const shell = await render((request) => request.flush(me));
    const sidebar = shell.querySelector<HTMLElement>('aside.sidebar')!;
    const toggle = () => shell.querySelector<HTMLButtonElement>('.sidebar-toggle')!;

    expect(sidebar.classList).not.toContain('is-collapsed');
    expect(toggle().closest('.sidebar-header')).not.toBeNull();
    expect(toggle().getAttribute('aria-expanded')).toBe('true');
    expect(toggle().getAttribute('aria-label')).toBe('Collapse sidebar');
    expect(toggle().getAttribute('title')).toBe(
      'Collapse sidebar (Ctrl B) · Peek on hover (Ctrl .)',
    );

    toggle().click();
    await TestBed.inject(ApplicationRef).whenStable();

    // The rail keeps its expand control at its foot, above the account.
    expect(sidebar.classList).toContain('is-collapsed');
    expect(toggle().closest('.sidebar-footer')).not.toBeNull();
    expect(toggle().getAttribute('aria-expanded')).toBe('false');
    expect(toggle().getAttribute('aria-label')).toBe('Expand sidebar');
    expect(toggle().getAttribute('title')).toBe('Expand sidebar (Ctrl B) · Peek on hover (Ctrl .)');
    expect(localStorage.getItem('eclipsvault.sidebar.collapsed')).toBe('1');

    TestBed.resetTestingModule();
    const reloaded = await render((request) => request.flush(me));
    const reloadedSidebar = reloaded.querySelector<HTMLElement>('aside.sidebar')!;
    const reloadedToggle = () => reloaded.querySelector<HTMLButtonElement>('.sidebar-toggle')!;

    expect(reloadedSidebar.classList).toContain('is-collapsed');
    expect(reloadedToggle().getAttribute('aria-expanded')).toBe('false');

    reloadedToggle().click();
    await TestBed.inject(ApplicationRef).whenStable();

    expect(reloadedSidebar.classList).not.toContain('is-collapsed');
    expect(reloadedToggle().getAttribute('aria-expanded')).toBe('true');
    expect(localStorage.getItem('eclipsvault.sidebar.collapsed')).toBe('0');
  });

  it('keeps focus on the sidebar toggle as it moves between the header and the rail', async () => {
    const shell = await render((request) => request.flush(me));
    document.body.append(shell);
    try {
      const toggle = () => shell.querySelector<HTMLButtonElement>('.sidebar-toggle')!;
      toggle().focus();
      toggle().click();
      await TestBed.inject(ApplicationRef).whenStable();
      expect(document.activeElement).toBe(toggle());
      expect(toggle().closest('.sidebar-footer')).not.toBeNull();

      toggle().click();
      await TestBed.inject(ApplicationRef).whenStable();
      expect(document.activeElement).toBe(toggle());
      expect(toggle().closest('.sidebar-header')).not.toBeNull();
    } finally {
      shell.remove();
    }
  });

  it('opens a rail group as a flyout beside it and keeps the rail collapsed', async () => {
    localStorage.setItem('eclipsvault.sidebar.collapsed', '1');
    const shell = await render((request) => request.flush(me));
    const sidebar = shell.querySelector<HTMLElement>('aside.sidebar')!;
    const administration = shell.querySelector<HTMLDetailsElement>(
      'details.nav-group[aria-label="Administration"]',
    )!;

    administration.querySelector<HTMLElement>('summary')!.click();
    await TestBed.inject(ApplicationRef).whenStable();

    expect(sidebar.classList).toContain('is-collapsed');
    expect(administration.open).toBe(true);
    expect(localStorage.getItem('eclipsvault.sidebar.collapsed')).toBe('1');
  });

  it('switches the theme directly from the rail while the account stays closed', async () => {
    localStorage.setItem('eclipsvault.sidebar.collapsed', '1');
    document.documentElement.dataset['theme'] = 'dark';
    const shell = await render((request) => request.flush(me));
    const theme = shell.querySelector<HTMLButtonElement>('button[aria-label="Switch to light"]')!;
    const account = shell.querySelector<HTMLDetailsElement>('details.account-menu')!;

    theme.click();
    await TestBed.inject(ApplicationRef).whenStable();

    expect(document.documentElement.dataset['theme']).toBe('light');
    expect(theme.getAttribute('aria-label')).toBe('Switch to dark');
    expect(account.open).toBe(false);

    account.querySelector('summary')!.click();
    const actions = [...account.querySelectorAll('[role="menuitem"]')].map((item) =>
      item.textContent?.trim(),
    );
    expect(actions).toEqual(['Profile', 'Sign out']);
  });

  describe('keyboard shortcuts', () => {
    async function press(target: EventTarget, init: KeyboardEventInit) {
      const event = new KeyboardEvent('keydown', { bubbles: true, cancelable: true, ...init });
      target.dispatchEvent(event);
      await TestBed.inject(ApplicationRef).whenStable();
      return event;
    }

    it('toggles compact mode on Ctrl B off Apple platforms, and ignores ⌘B there', async () => {
      const shell = await render((request) => request.flush(me));
      const sidebar = shell.querySelector<HTMLElement>('aside.sidebar')!;

      expect((await press(document.body, { key: 'b', metaKey: true })).defaultPrevented).toBe(
        false,
      );
      expect(sidebar.classList).not.toContain('is-collapsed');

      expect((await press(document.body, { key: 'b', ctrlKey: true })).defaultPrevented).toBe(true);
      expect(sidebar.classList).toContain('is-collapsed');
      expect(localStorage.getItem('eclipsvault.sidebar.collapsed')).toBe('1');

      await press(document.body, { key: 'b', ctrlKey: true });
      expect(sidebar.classList).not.toContain('is-collapsed');
    });

    it.each([
      { tag: 'input', editable: 'false' },
      { tag: 'textarea', editable: 'false' },
      { tag: 'div', editable: 'true' },
    ])('leaves Ctrl B and Ctrl . to the editor while typing in $tag', async ({ tag, editable }) => {
      const shell = await render((request) => request.flush(me));
      const sidebar = shell.querySelector<HTMLElement>('aside.sidebar')!;
      const target = document.createElement(tag);
      target.setAttribute('contenteditable', editable);
      document.body.append(target);
      try {
        expect((await press(target, { key: 'b', ctrlKey: true })).defaultPrevented).toBe(false);
        expect((await press(target, { key: '.', ctrlKey: true })).defaultPrevented).toBe(false);
        expect(sidebar.classList).not.toContain('is-collapsed');
        expect(localStorage.getItem('eclipsvault.sidebar.peek')).toBeNull();
      } finally {
        target.remove();
      }
    });

    it('takes the Mac sidebar shortcuts with Command instead of Control', async () => {
      const shell = await render((request) => request.flush(me), true);
      const sidebar = shell.querySelector<HTMLElement>('aside.sidebar')!;

      expect((await press(document.body, { key: 'b', ctrlKey: true })).defaultPrevented).toBe(
        false,
      );
      expect(sidebar.classList).not.toContain('is-collapsed');
      await press(document.body, { key: 'b', metaKey: true });
      expect(sidebar.classList).toContain('is-collapsed');

      await press(document.body, { key: '.', metaKey: true });
      sidebar.dispatchEvent(new PointerEvent('pointerenter', { pointerType: 'mouse' }));
      await TestBed.inject(ApplicationRef).whenStable();
      expect(sidebar.classList).toContain('is-peek');
    });

    it('leaves Ctrl B to the browser below 1200px, where the sidebar is a drawer', async () => {
      const viewport = stubViewport(true);
      try {
        await render((request) => request.flush(me));
        expect((await press(document.body, { key: 'b', ctrlKey: true })).defaultPrevented).toBe(
          false,
        );
        expect(localStorage.getItem('eclipsvault.sidebar.collapsed')).toBeNull();
      } finally {
        viewport.restore();
      }
    });

    it('turns peek on hover on and off with Ctrl ., remembering it, and shows the full sidebar over the page only while hovered', async () => {
      localStorage.setItem('eclipsvault.sidebar.collapsed', '1');
      const shell = await render((request) => request.flush(me));
      const sidebar = shell.querySelector<HTMLElement>('aside.sidebar')!;
      const status = shell.querySelector('[role="status"].visually-hidden')!;
      const hover = async (type: 'pointerenter' | 'pointerleave') => {
        sidebar.dispatchEvent(new PointerEvent(type, { pointerType: 'mouse' }));
        await TestBed.inject(ApplicationRef).whenStable();
      };

      // Off by default: hovering the rail changes nothing.
      await hover('pointerenter');
      expect(sidebar.classList).toContain('is-collapsed');
      await hover('pointerleave');

      await press(document.body, { key: '.', ctrlKey: true });
      expect(localStorage.getItem('eclipsvault.sidebar.peek')).toBe('1');
      expect(status.textContent).toContain('Peek on hover is on');

      await hover('pointerenter');
      expect(sidebar.classList).not.toContain('is-collapsed');
      expect(sidebar.classList).toContain('is-peek');
      // Peeking previews the rail; the saved preference stays collapsed.
      expect(localStorage.getItem('eclipsvault.sidebar.collapsed')).toBe('1');
      expect(shell.querySelector('.sidebar-toggle')?.getAttribute('aria-expanded')).toBe('false');

      await hover('pointerleave');
      expect(sidebar.classList).toContain('is-collapsed');
      expect(sidebar.classList).not.toContain('is-peek');

      TestBed.resetTestingModule();
      const reloaded = await render((request) => request.flush(me));
      const reloadedSidebar = reloaded.querySelector<HTMLElement>('aside.sidebar')!;
      reloadedSidebar.dispatchEvent(new PointerEvent('pointerenter', { pointerType: 'mouse' }));
      await TestBed.inject(ApplicationRef).whenStable();
      expect(reloadedSidebar.classList).toContain('is-peek');

      await press(document.body, { key: '.', ctrlKey: true });
      expect(localStorage.getItem('eclipsvault.sidebar.peek')).toBe('0');
      expect(reloadedSidebar.classList).not.toContain('is-peek');
      expect(reloadedSidebar.classList).toContain('is-collapsed');
    });

    it('does not peek while the sidebar is expanded', async () => {
      localStorage.setItem('eclipsvault.sidebar.peek', '1');
      const shell = await render((request) => request.flush(me));
      const sidebar = shell.querySelector<HTMLElement>('aside.sidebar')!;
      sidebar.dispatchEvent(new PointerEvent('pointerenter', { pointerType: 'mouse' }));
      await TestBed.inject(ApplicationRef).whenStable();
      expect(sidebar.classList).not.toContain('is-peek');
    });

    it('returns focus to the group when ending a peek hides its focused page', async () => {
      localStorage.setItem('eclipsvault.sidebar.collapsed', '1');
      localStorage.setItem('eclipsvault.sidebar.peek', '1');
      const shell = await render((request) => request.flush(me));
      const sidebar = shell.querySelector<HTMLElement>('aside.sidebar')!;
      const administration = sidebar.querySelector('details[aria-label="Administration"]')!;
      const trigger = administration.querySelector('summary')!;
      const license = administration.querySelector<HTMLAnchorElement>('a[href="/License"]')!;

      sidebar.dispatchEvent(new PointerEvent('pointerenter', { pointerType: 'mouse' }));
      await TestBed.inject(ApplicationRef).whenStable();
      trigger.click();
      await TestBed.inject(ApplicationRef).whenStable();
      license.focus();
      expect(document.activeElement).toBe(license);

      sidebar.dispatchEvent(new PointerEvent('pointerleave', { pointerType: 'mouse' }));
      await TestBed.inject(ApplicationRef).whenStable();
      expect(sidebar.classList).toContain('is-collapsed');
      expect(document.activeElement).toBe(trigger);
    });
  });

  it('stays usable when localStorage throws while reading or saving sidebar state', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('Storage disabled', 'SecurityError');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('Storage disabled', 'SecurityError');
    });

    const shell = await render((request) => request.flush(me));
    const sidebar = shell.querySelector<HTMLElement>('aside.sidebar')!;
    const toggle = shell.querySelector<HTMLButtonElement>('.sidebar-toggle')!;

    expect(sidebar.classList).not.toContain('is-collapsed');
    toggle.click();
    await TestBed.inject(ApplicationRef).whenStable();
    expect(sidebar.classList).toContain('is-collapsed');
  });

  /** jsdom has no matchMedia, so the shell reads this stand-in for the 1199px query. */
  function stubViewport(compact: boolean) {
    const listeners = new Set<(event: MediaQueryListEvent) => void>();
    const mediaQuery = {
      matches: compact,
      media: '(max-width: 1199px)',
      addEventListener: (_: string, listener: (event: MediaQueryListEvent) => void) =>
        listeners.add(listener),
      removeEventListener: (_: string, listener: (event: MediaQueryListEvent) => void) =>
        listeners.delete(listener),
    } as unknown as MediaQueryList;
    const view = document.defaultView!;
    Object.defineProperty(view, 'matchMedia', { configurable: true, value: () => mediaQuery });
    // Each resize renders a fresh drawer, and jsdom's dialog has neither showModal nor close.
    const dialog = HTMLDialogElement.prototype as Partial<HTMLDialogElement>;
    dialog.showModal = function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    };
    dialog.close = function (this: HTMLDialogElement) {
      this.removeAttribute('open');
    };
    return {
      async resize(matches: boolean) {
        for (const listener of listeners) {
          listener({ matches } as MediaQueryListEvent);
        }
        await TestBed.inject(ApplicationRef).whenStable();
      },
      restore() {
        delete (view as Partial<Window>).matchMedia;
        delete dialog.showModal;
        delete dialog.close;
      },
    };
  }

  it('moves focus to the replacement navigation toggle when resizing across 1200px', async () => {
    const viewport = stubViewport(false);
    const shell = await render((request) => request.flush(me));
    document.body.append(shell);
    try {
      const desktopToggle = shell.querySelector<HTMLButtonElement>('.sidebar-toggle')!;
      desktopToggle.focus();
      expect(document.activeElement).toBe(desktopToggle);

      await viewport.resize(true);

      const mobileToggle = shell.querySelector<HTMLButtonElement>('.navigation-toggle')!;
      expect(document.activeElement).toBe(mobileToggle);

      mobileToggle.click();
      await TestBed.inject(ApplicationRef).whenStable();

      await viewport.resize(false);

      expect(document.activeElement).toBe(shell.querySelector('.sidebar-toggle'));
    } finally {
      shell.remove();
      viewport.restore();
    }
  });

  it('hands focus from a closed drawer opener or the top-bar logo to the desktop toggle at 1200px', async () => {
    const viewport = stubViewport(true);
    const shell = await render((request) => request.flush(me));
    document.body.append(shell);
    try {
      for (const selector of ['.navigation-toggle', '.topbar-brand']) {
        shell.querySelector<HTMLElement>(selector)!.focus();
        expect(document.activeElement).toBe(shell.querySelector(selector));

        await viewport.resize(false);

        expect(document.activeElement).toBe(shell.querySelector('.sidebar-toggle'));
        await viewport.resize(true);
      }
    } finally {
      shell.remove();
      viewport.restore();
    }
  });

  it('leaves focus alone when the focused control survives the resize', async () => {
    const viewport = stubViewport(true);
    const shell = await render((request) => request.flush(me));
    document.body.append(shell);
    try {
      const search = shell.querySelector<HTMLButtonElement>('.search-pill')!;
      search.focus();

      await viewport.resize(false);

      expect(document.activeElement).toBe(search);
    } finally {
      shell.remove();
      viewport.restore();
    }
  });
});
