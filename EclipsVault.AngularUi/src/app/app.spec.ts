import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Me } from './account/me';
import { App } from './app';
import { appConfig } from './app.config';

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

  async function render(answer: (request: ReturnType<HttpTestingController['expectOne']>) => void) {
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting()],
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
    const toggle = shell.querySelector<HTMLButtonElement>('.sidebar-toggle')!;

    expect(sidebar.classList).not.toContain('is-collapsed');
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(toggle.getAttribute('aria-label')).toBe('Collapse sidebar');

    toggle.click();
    await TestBed.inject(ApplicationRef).whenStable();

    expect(sidebar.classList).toContain('is-collapsed');
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(toggle.getAttribute('aria-label')).toBe('Expand sidebar');
    expect(localStorage.getItem('eclipsvault.sidebar.collapsed')).toBe('1');

    TestBed.resetTestingModule();
    const reloaded = await render((request) => request.flush(me));
    const reloadedSidebar = reloaded.querySelector<HTMLElement>('aside.sidebar')!;
    const reloadedToggle = reloaded.querySelector<HTMLButtonElement>('.sidebar-toggle')!;

    expect(reloadedSidebar.classList).toContain('is-collapsed');
    expect(reloadedToggle.getAttribute('aria-expanded')).toBe('false');

    reloadedToggle.click();
    await TestBed.inject(ApplicationRef).whenStable();

    expect(reloadedSidebar.classList).not.toContain('is-collapsed');
    expect(reloadedToggle.getAttribute('aria-expanded')).toBe('true');
    expect(localStorage.getItem('eclipsvault.sidebar.collapsed')).toBe('0');
  });

  it('expands a collapsed sidebar and opens the clicked group when a rail group icon is clicked', async () => {
    localStorage.setItem('eclipsvault.sidebar.collapsed', '1');
    const shell = await render((request) => request.flush(me));
    const sidebar = shell.querySelector<HTMLElement>('aside.sidebar')!;
    const administration = shell.querySelector<HTMLDetailsElement>(
      'details.nav-group[aria-label="Administration"]',
    )!;
    const summary = administration.querySelector<HTMLElement>('summary')!;

    expect(sidebar.classList).toContain('is-collapsed');
    expect(administration.open).toBe(false);

    summary.click();
    await TestBed.inject(ApplicationRef).whenStable();

    expect(sidebar.classList).not.toContain('is-collapsed');
    expect(administration.open).toBe(true);
    expect(localStorage.getItem('eclipsvault.sidebar.collapsed')).toBe('0');
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
});
