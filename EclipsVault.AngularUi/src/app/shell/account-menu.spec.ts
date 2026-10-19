import { HttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Me } from '../account/me';
import { appConfig } from '../app.config';
import { AccountMenu, signOut } from './account-menu';

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

describe('signOut', () => {
  const assign = vi.fn();
  const failed = vi.fn();
  let backend: HttpTestingController;
  let http: HttpClient;

  beforeEach(() => {
    assign.mockReset();
    failed.mockReset();
    // The real application providers, so this is the XSRF wiring the browser gets.
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
    http = TestBed.inject(HttpClient);
    document.cookie = 'XSRF-TOKEN=request-token-from-me; path=/';
  });

  afterEach(() => {
    document.cookie = 'XSRF-TOKEN=; path=/; max-age=0';
    backend.verify();
  });

  it('posts to the existing sign-out endpoint with the token in the header antiforgery accepts, then goes to sign-in', () => {
    signOut(http, { assign }, failed);

    const request = backend.expectOne({ method: 'POST', url: '/Account/Logout' });
    expect(request.request.headers.get('RequestVerificationToken')).toBe('request-token-from-me');
    expect(assign).not.toHaveBeenCalled();
    request.flush('<!doctype html>');

    expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login');
    expect(failed).not.toHaveBeenCalled();
  });

  it('stays on the page and reports a refused sign-out', () => {
    signOut(http, { assign }, failed);

    backend.expectOne('/Account/Logout').flush('', { status: 400, statusText: 'Bad Request' });

    expect(assign).not.toHaveBeenCalled();
    expect(failed).toHaveBeenCalledOnce();
  });
});
