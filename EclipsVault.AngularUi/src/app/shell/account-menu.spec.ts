import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { Me } from '../account/me';
import { appConfig } from '../app.config';
import { AccountMenu } from './account-menu';
import { PAGE_LOCATION } from './command-palette';

const me: Me = {
  username: 'devon',
  displayName: 'Devon Rhodes',
  clearance: 'Confidential',
  project: 'GLOBAL',
  areas: ['access', 'profile'],
  avatarUrl: '/Profile/Avatar?v=42',
  licenseNotice: null,
};

describe('AccountMenu', () => {
  async function render(account: Me) {
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(AccountMenu);
    fixture.componentRef.setInput('me', account);
    await fixture.whenStable();
    return { menu: fixture.nativeElement as HTMLElement, fixture };
  }

  afterEach(() => delete document.documentElement.dataset['theme']);

  it('shows the avatar from the avatar endpoint, the display name and the clearance', async () => {
    const { menu } = await render(me);

    expect(menu.querySelector('img.avatar-img')?.getAttribute('src')).toBe('/Profile/Avatar?v=42');
    expect(menu.querySelector('.user-name')?.textContent?.trim()).toBe('Devon Rhodes');
    expect(menu.querySelector('.user-clearance')?.textContent?.trim()).toBe('Confidential');
  });

  it('links to the profile only when the server offers the profile Area', async () => {
    const { menu: offered } = await render(me);
    expect(offered.querySelector('a[href="/Profile"]')?.textContent?.trim()).toBe('Profile');

    TestBed.resetTestingModule();
    const { menu: withheld } = await render({ ...me, areas: ['access'] });
    expect(withheld.querySelector('a[href="/Profile"]')).toBeNull();
  });

  it('switches the theme and names the one it would switch to next', async () => {
    document.documentElement.dataset['theme'] = 'dark';
    const { menu, fixture } = await render(me);
    const toggle = [...menu.querySelectorAll('button')].find((button) =>
      button.textContent?.includes('Switch to'),
    )!;
    expect(toggle.textContent?.trim()).toBe('Switch to light');

    toggle.click();
    await fixture.whenStable();

    expect(document.documentElement.dataset['theme']).toBe('light');
    expect(toggle.textContent?.trim()).toBe('Switch to dark');
  });
});

describe.each([
  ['the expanded sidebar', false],
  ['the compact rail', true],
])('AccountMenu closing in %s', (_mode, rail) => {
  async function openMenu() {
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(AccountMenu);
    fixture.componentRef.setInput('me', me);
    fixture.componentRef.setInput('rail', rail);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const menu = host.querySelector<HTMLDetailsElement>('details.account-menu')!;
    menu.open = true;
    return { host, menu };
  }

  it('closes as soon as Profile is chosen', async () => {
    const { host, menu } = await openMenu();

    host.querySelector<HTMLElement>('a[href="/Profile"]')!.click();

    expect(menu.open).toBe(false);
  });

  it('closes when a navigation finishes elsewhere', async () => {
    const { menu } = await openMenu();

    await TestBed.inject(Router).navigateByUrl('/Profile');

    expect(menu.open).toBe(false);
  });

  it('closes on a click outside it', async () => {
    const { menu } = await openMenu();

    document.body.click();

    expect(menu.open).toBe(false);
  });

  it('closes on Escape and returns focus to its trigger', async () => {
    const { menu } = await openMenu();
    menu.querySelector<HTMLElement>('.menu-item')!.focus();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

    expect(menu.open).toBe(false);
    expect(document.activeElement).toBe(menu.querySelector('summary'));
  });
});

describe('AccountMenu sign-out', () => {
  const assign = vi.fn();
  let backend: HttpTestingController;

  async function signOut() {
    const fixture = TestBed.createComponent(AccountMenu);
    fixture.componentRef.setInput('me', me);
    await fixture.whenStable();
    const menu = fixture.nativeElement as HTMLElement;
    [...menu.querySelectorAll('button')].find((b) => b.textContent?.includes('Sign out'))!.click();
    await fixture.whenStable();
    return menu;
  }

  beforeEach(() => {
    assign.mockReset();
    // The real application providers, so this is the XSRF wiring the browser gets.
    TestBed.configureTestingModule({
      providers: [
        ...appConfig.providers,
        provideHttpClientTesting(),
        { provide: PAGE_LOCATION, useValue: { assign } },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    document.cookie = 'XSRF-TOKEN=request-token-from-me; path=/';
  });

  afterEach(() => {
    document.cookie = 'XSRF-TOKEN=; path=/; max-age=0';
    backend.verify();
  });

  it('posts the UI API sign-out with the token in the header antiforgery accepts, then goes to sign-in', async () => {
    await signOut();

    const request = backend.expectOne({ method: 'POST', url: '/api/ui/auth/sign-out' });
    expect(request.request.headers.get('RequestVerificationToken')).toBe('request-token-from-me');
    expect(assign).not.toHaveBeenCalled();
    request.flush(null, { status: 204, statusText: 'No Content' });

    expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login');
  });

  it('goes to sign-in with the local-clear warning when the vault could not revoke the session', async () => {
    const menu = await signOut();

    backend.expectOne('/api/ui/auth/sign-out').flush(
      {
        status: 503,
        code: 'sign_out_incomplete',
        title: "This browser's cookie was cleared. Other devices may still be signed in.",
      },
      { status: 503, statusText: 'Service Unavailable' },
    );

    expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login?signOut=sign_out_incomplete');
    expect(menu.querySelector('.menu-error')).toBeNull();
  });

  it('stays on the page and reports a sign-out that did not complete', async () => {
    const menu = await signOut();

    backend.expectOne('/api/ui/auth/sign-out').error(new ProgressEvent('error'));
    TestBed.tick();

    expect(assign).not.toHaveBeenCalled();
    expect(menu.querySelector('.menu-error')?.textContent).toContain('Could not sign out.');
  });
});
