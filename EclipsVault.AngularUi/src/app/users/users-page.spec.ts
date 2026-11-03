import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PAGE_LOCATION } from '../shell/command-palette';
import { appConfig } from '../app.config';
import type { components } from '../ui-api/schema.generated';
import { UsersPage } from './users-page';

type User = components['schemas']['UserResponse'];
const colleague: User = {
  userId: '11111111-1111-1111-1111-111111111111',
  displayName: 'Ada Staff',
  username: 'ada',
  email: 'ada@example.test',
  clearance: 2,
  clearanceLabel: 'Confidential',
  project: 'OPS',
  mfaEnrolled: true,
  status: 'active',
  avatarUrl: '/Profile/Avatar?id=ada',
  isYou: false,
};
const path = `/api/ui/users/${colleague.userId}`;
function button(scope: ParentNode, text: string): HTMLButtonElement {
  const found = [...scope.querySelectorAll('button')].find(
    (item) => item.textContent?.trim() === text,
  );
  if (!found) throw new Error(`No button named ${text}`);
  return found;
}

describe('UsersPage', () => {
  let backend: HttpTestingController;
  const assign = vi.fn();
  beforeEach(() => {
    assign.mockReset();
    history.replaceState(null, '', '/Admin/Users');
  });
  function render() {
    TestBed.configureTestingModule({
      providers: [
        ...appConfig.providers,
        provideHttpClientTesting(),
        { provide: PAGE_LOCATION, useValue: { assign } },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(UsersPage);
    fixture.detectChanges();
    const page: HTMLElement = fixture.nativeElement;
    page.querySelectorAll('dialog').forEach((dialog) => {
      dialog.showModal = () => dialog.setAttribute('open', '');
      dialog.close = () => dialog.removeAttribute('open');
    });
    backend.expectOne('/api/ui/users/notice').flush({ message: null, error: false });
    return { fixture, page };
  }
  async function loaded(user: User = colleague) {
    const rendered = render();
    backend.expectOne('/api/ui/users').flush({ users: [user] });
    await rendered.fixture.whenStable();
    return rendered;
  }
  afterEach(() => backend.verify());
  it('shows loading until accounts arrive', () => {
    const { page } = render();
    expect(page.textContent).toContain('Loading users…');
    backend.expectOne('/api/ui/users').flush({ users: [] });
  });
  it('retries a failed list request', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/users').flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(page.textContent).toContain('Could not load users. Try again.');
    button(page, 'Try again').click();
    fixture.detectChanges();
    backend.expectOne('/api/ui/users').flush({ users: [colleague] });
    await fixture.whenStable();
    expect(page.textContent).toContain('Ada Staff');
  });
  it('renders the Legacy columns even with no accounts', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/users').flush({ users: [] });
    await fixture.whenStable();
    expect([...page.querySelectorAll('th')].map((cell) => cell.textContent)).toEqual([
      'User',
      'Clearance',
      'Project',
      'MFA',
      'Status',
      '',
    ]);
    expect(page.querySelectorAll('tbody tr')).toHaveLength(0);
  });
  it('filters accounts by their displayed fields', async () => {
    const { fixture, page } = await loaded();
    expect(page.textContent).toContain('ada · ada@example.test');
    const search = page.querySelector('input');
    if (!search) throw new Error('No filter');
    search.value = 'another project';
    search.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(page.querySelectorAll('tbody tr')).toHaveLength(0);
    search.value = 'ops';
    search.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(page.querySelectorAll('tbody tr')).toHaveLength(1);
  });
  it('offers only Edit and Force logout on your own row', async () => {
    const { page } = await loaded({ ...colleague, isYou: true });
    expect(page.textContent).toContain('you');
    expect([...page.querySelectorAll('tbody a')].map((link) => link.textContent?.trim())).toEqual([
      'Edit',
    ]);
    expect(
      [...page.querySelectorAll('tbody button')].map((item) => item.textContent?.trim()),
    ).toEqual(['Force logout']);
  });
  it.each([
    ['Disable', `${path}/disable`, 'Account disabled and its sessions revoked.'],
    [
      'Reset MFA',
      `${path}/reset-mfa`,
      'MFA was reset — the user will enroll a new authenticator at their next sign-in.',
    ],
    ['Force logout', `${path}/sign-out`, "All of the user's sessions were revoked."],
    ['Delete user', path, 'User deleted.'],
  ])(
    '%s confirms before writing, announces the outcome and reloads',
    async (label, url, outcome) => {
      const { fixture, page } = await loaded();
      button(page, label).click();
      await fixture.whenStable();
      button(page, 'Continue').click();
      backend.expectOne(url).flush(null);
      fixture.detectChanges();
      backend.expectOne('/api/ui/users').flush({ users: [colleague] });
      await fixture.whenStable();
      expect(page.querySelector('.flash-success')?.textContent).toBe(outcome);
    },
  );
  it.each([
    ['Enable', 'enable', 'Account enabled.', 'disabled'],
    ['Unlock', 'unlock', 'Lockout cleared. The user can sign in again.', 'locked'],
  ] satisfies [string, string, string, User['status']][])(
    '%s writes without confirmation, announces the outcome and reloads',
    async (label, action, outcome, status) => {
      const { fixture, page } = await loaded({ ...colleague, status });
      button(page, label).click();
      backend.expectOne(`${path}/${action}`).flush(null);
      fixture.detectChanges();
      backend.expectOne('/api/ui/users').flush({ users: [colleague] });
      await fixture.whenStable();
      expect(page.querySelector('.flash-success')?.textContent).toBe(outcome);
      expect(page.querySelector('dialog[open]')).toBeNull();
    },
  );
  it('cancelling deletion keeps the account', async () => {
    const { fixture, page } = await loaded();
    button(page, 'Delete user').click();
    await fixture.whenStable();
    const dialog = page.querySelector('dialog[open]');
    if (!dialog) throw new Error('No confirmation');
    expect(dialog.textContent).toContain("Delete user 'ada' permanently? This is audited.");
    button(dialog, 'Cancel').click();
    await fixture.whenStable();
    expect(dialog.hasAttribute('open')).toBe(false);
    expect(page.textContent).toContain('Ada Staff');
    backend.expectNone(path);
  });
  async function prompted(proof: 'code' | 'sign_in') {
    const rendered = await loaded({ ...colleague, status: 'disabled' });
    button(rendered.page, 'Enable').click();
    backend
      .expectOne(`${path}/enable`)
      .flush({ code: 'step_up_required', proof }, { status: 403, statusText: 'Forbidden' });
    rendered.fixture.detectChanges();
    backend.expectOne('/api/ui/users').flush({ users: [{ ...colleague, status: 'disabled' }] });
    await rendered.fixture.whenStable();
    const dialog = rendered.page.querySelector('dialog[open]');
    if (!dialog) throw new Error('No step-up dialog');
    return { ...rendered, dialog };
  }
  it('cancelling code step-up leaves the account disabled', async () => {
    const { fixture, page, dialog } = await prompted('code');
    expect(dialog.textContent).toContain("Confirm it's you");
    button(dialog, 'Cancel').click();
    await fixture.whenStable();
    expect(dialog.hasAttribute('open')).toBe(false);
    expect(page.textContent).toContain('Disabled');
    backend.expectNone(`${path}/enable`);
  });
  it('sign-in step-up can be cancelled without changing the account', async () => {
    const { fixture, page, dialog } = await prompted('sign_in');
    expect(button(dialog, 'Sign in again').textContent?.trim()).toBe('Sign in again');
    button(dialog, 'Cancel').click();
    await fixture.whenStable();
    expect(dialog.hasAttribute('open')).toBe(false);
    expect(page.textContent).toContain('Disabled');
    backend.expectNone(`${path}/enable`);
  });
  it('sign-in step-up ends the current session before returning to sign-in with the page URL', async () => {
    const { fixture, dialog } = await prompted('sign_in');
    document.cookie = 'XSRF-TOKEN=step-up-sign-out; path=/';
    button(dialog, 'Sign in again').click();
    const logout = backend.expectOne('/Account/Logout');
    expect(logout.request.headers.get('RequestVerificationToken')).toBe('step-up-sign-out');
    expect(assign).not.toHaveBeenCalled();
    logout.flush('');
    await fixture.whenStable();
    expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login?ReturnUrl=%2FAdmin%2FUsers');
    expect(dialog.hasAttribute('open')).toBe(false);
    backend.expectNone(`${path}/enable`);
    document.cookie = 'XSRF-TOKEN=; path=/; max-age=0';
  });

  it('a failed sign-out keeps sign-in step-up open with a retryable error', async () => {
    const { fixture, dialog } = await prompted('sign_in');
    button(dialog, 'Sign in again').click();
    backend.expectOne('/Account/Logout').flush('', { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(dialog.textContent).toContain('Could not sign out. Reload the page to try again.');
    expect(dialog.hasAttribute('open')).toBe(true);
    expect(assign).not.toHaveBeenCalled();
  });

  it('shows a wrong-code error then retries the blocked action only once', async () => {
    const { fixture, page } = await loaded({ ...colleague, status: 'disabled' });
    button(page, 'Enable').click();
    backend
      .expectOne(`${path}/enable`)
      .flush({ code: 'step_up_required', proof: 'code' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    backend.expectOne('/api/ui/users').flush({ users: [{ ...colleague, status: 'disabled' }] });
    await fixture.whenStable();
    const dialog = page.querySelector('dialog[open]');
    const code = dialog?.querySelector('input');
    if (!dialog || !code) throw new Error('No code dialog');
    code.value = '123456';
    dialog.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    backend
      .expectOne('/api/ui/step-up')
      .flush(
        { code: 'step_up_failed', title: 'That authenticator code is not valid. Try again.' },
        { status: 400, statusText: 'Bad request' },
      );
    await fixture.whenStable();
    expect(dialog.textContent).toContain('That authenticator code is not valid. Try again.');
    dialog.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    backend.expectOne('/api/ui/step-up').flush(null);
    backend
      .expectOne(`${path}/enable`)
      .flush(
        { code: 'step_up_required', proof: 'code', title: 'Confirm again.' },
        { status: 403, statusText: 'Forbidden' },
      );
    fixture.detectChanges();
    backend.expectOne('/api/ui/users').flush({ users: [colleague] });
    await fixture.whenStable();
    expect(page.querySelector('dialog[open]')).toBeNull();
    expect(page.querySelector('.flash-error')?.textContent).toBe('Confirm again.');
    expect(code.value).toBe('');
  });
  it.each([
    'user_not_found',
    'last_administrator',
    'own_account',
    'account_changed',
    'audit_unavailable',
    'revoked_unrecorded',
  ])('announces %s and reloads the list', async (code) => {
    const { fixture, page } = await loaded();
    button(page, 'Force logout').click();
    await fixture.whenStable();
    button(page, 'Continue').click();
    backend
      .expectOne(`${path}/sign-out`)
      .flush({ code, title: 'The server outcome.' }, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    backend.expectOne('/api/ui/users').flush({ users: [colleague] });
    await fixture.whenStable();
    expect(page.querySelector('.flash-error')?.textContent).toBe('The server outcome.');
  });
});
