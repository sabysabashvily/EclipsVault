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
  awaitingActivation: false,
  invitationExpiresAtUtc: null,
};
const invited: User = {
  ...colleague,
  mfaEnrolled: false,
  awaitingActivation: true,
  invitationExpiresAtUtc: '2099-11-26T14:30:00Z',
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
  it('renders the columns even with no accounts', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/users').flush({ users: [] });
    await fixture.whenStable();
    expect([...page.querySelectorAll('th')].map((cell) => cell.textContent)).toEqual([
      'Person',
      'Clearance',
      'Project',
      'Two-step',
      'Status',
      '',
    ]);
    expect(page.querySelectorAll('tbody tr')).toHaveLength(0);
  });
  it('sums up the people, skipping counts that are zero', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/users').flush({
      users: [
        colleague,
        { ...colleague, userId: 'b', mfaEnrolled: false },
        { ...colleague, userId: 'c', status: 'locked' },
      ],
    });
    await fixture.whenStable();
    expect(page.querySelector('.page-sub')?.textContent?.trim()).toBe(
      '3 people · 1 without two-step · 1 locked. New users set their own password and authenticator from an emailed invitation.',
    );
  });
  it('sums up one person with nothing to flag', async () => {
    const { page } = await loaded();
    expect(page.querySelector('.page-sub')?.textContent?.trim()).toBe(
      '1 person. New users set their own password and authenticator from an emailed invitation.',
    );
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
    search.value = 'not set up';
    search.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(page.querySelectorAll('tbody tr')).toHaveLength(0);
  });
  it('offers Edit, Disable, Force logout, Reset MFA and Delete user, in that order, on an active row', async () => {
    const { page } = await loaded();
    expect(
      [...page.querySelectorAll('[role="menuitem"]')].map((item) => item.textContent?.trim()),
    ).toEqual(['Edit', 'Disable', 'Force logout', 'Reset MFA', 'Delete user']);
    expect(page.querySelector('.cell-verb > button')).toBeNull();
  });
  it('keeps Disable out of a disabled row and offers Enable beside the menu', async () => {
    const { page } = await loaded({ ...colleague, status: 'disabled' });
    expect(
      [...page.querySelectorAll('[role="menuitem"]')].map((item) => item.textContent?.trim()),
    ).toEqual(['Edit', 'Force logout', 'Reset MFA', 'Delete user']);
    expect(page.querySelector('.cell-verb > button')?.textContent?.trim()).toBe('Enable');
  });
  it('names the Disable dialog after the account, with its consequence and the audit note', async () => {
    const { fixture, page } = await loaded();
    button(page, 'Disable').click();
    await fixture.whenStable();
    const dialog = page.querySelector('dialog[open]');
    if (!dialog) throw new Error('No confirmation');
    expect(dialog.querySelector('h2')?.textContent).toBe('Disable ada?');
    expect(dialog.textContent).toContain(
      "Disable 'ada'? They will be signed out immediately and cannot sign in until re-enabled.",
    );
    expect(dialog.querySelector('.dialog-note')?.textContent).toBe('Saved to the audit log');
    button(dialog, 'Cancel').click();
    await fixture.whenStable();
    backend.expectNone(`${path}/disable`);
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
  it('filtering by you keeps the signed-in account in the list', async () => {
    const { page, fixture } = await loaded({ ...colleague, isYou: true });
    const search = page.querySelector('input[type="search"]');
    if (!(search instanceof HTMLInputElement)) throw new Error('No user filter');
    search.value = 'you';
    search.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(page.querySelectorAll('tbody tr')).toHaveLength(1);
    expect(page.querySelector('tbody')?.textContent).toContain('Ada Staff');
  });
  it.each([
    [
      'Disable',
      'Disable and sign out',
      `${path}/disable`,
      'Account disabled and its sessions revoked.',
    ],
    [
      'Reset MFA',
      'Reset MFA',
      `${path}/reset-mfa`,
      'MFA was reset — the user will enroll a new authenticator at their next sign-in.',
    ],
    [
      'Force logout',
      'End their sessions',
      `${path}/sign-out`,
      "All of the user's sessions were revoked.",
    ],
    ['Delete user', 'Delete user', path, 'User deleted.'],
  ])(
    '%s confirms with %s before writing, announces the outcome and reloads',
    async (label, confirm, url, outcome) => {
      const { fixture, page } = await loaded();
      button(page, label).click();
      await fixture.whenStable();
      const dialog = page.querySelector('dialog[open]');
      if (!dialog) throw new Error('No confirmation');
      button(dialog, confirm).click();
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
  it('counts the accounts awaiting activation in the summary', async () => {
    const { page } = await loaded(invited);
    expect(page.querySelector('.page-sub')?.textContent?.trim()).toBe(
      '1 person · 1 awaiting activation · 1 without two-step. New users set their own password and authenticator from an emailed invitation.',
    );
  });
  it('shows an invited row as a pill with its word and offers Resend and address replacement, not Reset MFA', async () => {
    const { page } = await loaded(invited);
    expect(page.querySelector('tbody .badge.tone-info')?.textContent?.trim()).toBe(
      'Invited · expires 26 Nov 2099, 14:30 UTC',
    );
    expect(
      [...page.querySelectorAll('[role="menuitem"]')].map((item) => item.textContent?.trim()),
    ).toEqual([
      'Edit',
      'Resend invitation',
      'Replace invitation address',
      'Disable',
      'Force logout',
      'Delete user',
    ]);
    const replace = [...page.querySelectorAll('a[role="menuitem"]')].find(
      (link) => link.textContent?.trim() === 'Replace invitation address',
    );
    expect(replace?.getAttribute('href')).toBe(`/Admin/EditUser/${colleague.userId}#invitation`);
    expect(page.querySelector('.cell-verb > button')).toBeNull();
  });
  it('puts Resend beside the menu of a row whose invitation expired', async () => {
    const { page } = await loaded({ ...invited, invitationExpiresAtUtc: null });
    expect(page.querySelector('tbody .badge.tone-danger')?.textContent?.trim()).toBe(
      'Invitation expired',
    );
    expect(page.querySelector('.cell-verb > button')?.textContent?.trim()).toBe(
      'Resend invitation',
    );
    expect(
      [...page.querySelectorAll('[role="menuitem"]')].map((item) => item.textContent?.trim()),
    ).not.toContain('Resend invitation');
  });
  it('shows a disabled invited account as Disabled, with no invitation actions', async () => {
    const { page } = await loaded({ ...invited, status: 'disabled' });
    expect(page.querySelector('tbody .badge.tone-danger')?.textContent?.trim()).toBe('Disabled');
    expect(page.textContent).not.toContain('Resend invitation');
    expect(page.textContent).not.toContain('Replace invitation address');
  });
  it.each([
    ['sent', 'flash-success', 'Invitation sent to ada@example.test. It expires in 72 hours.'],
    [
      'not_delivered',
      'flash-error',
      'A new link was made, but it could not be delivered. Resend it to try again.',
    ],
  ] as const)(
    'Resend confirms, writes, says the %s outcome and reloads',
    async (invitation, flash, outcome) => {
      const { fixture, page } = await loaded(invited);
      button(page, 'Resend invitation').click();
      await fixture.whenStable();
      const dialog = page.querySelector('dialog[open]');
      if (!dialog) throw new Error('No confirmation');
      expect(dialog.querySelector('h2')?.textContent).toBe('Resend invitation to ada?');
      expect(dialog.textContent).toContain(
        'Send a new invitation to ada@example.test? The previous link stops working.',
      );
      expect(dialog.querySelector('.dialog-note')?.textContent).toBe('Saved to the audit log');
      button(dialog, 'Resend invitation').click();
      backend
        .expectOne({ method: 'POST', url: `${path}/invitation` })
        .flush({ invitation, expiresAtUtc: '2099-11-27T14:30:00Z' });
      fixture.detectChanges();
      backend.expectOne('/api/ui/users').flush({ users: [invited] });
      await fixture.whenStable();
      expect(page.querySelector(`.${flash}`)?.textContent).toBe(outcome);
    },
  );
  it('cancelling Resend sends nothing', async () => {
    const { fixture, page } = await loaded(invited);
    button(page, 'Resend invitation').click();
    await fixture.whenStable();
    const dialog = page.querySelector('dialog[open]');
    if (!dialog) throw new Error('No confirmation');
    button(dialog, 'Cancel').click();
    await fixture.whenStable();
    backend.expectNone(`${path}/invitation`);
  });
  it('Resend asks for fresh proof, then retries once', async () => {
    const { fixture, page } = await loaded(invited);
    button(page, 'Resend invitation').click();
    await fixture.whenStable();
    button(page.querySelector('dialog[open]')!, 'Resend invitation').click();
    backend
      .expectOne(`${path}/invitation`)
      .flush({ code: 'step_up_required', proof: 'code' }, { status: 403, statusText: 'Forbidden' });
    fixture.detectChanges();
    backend.expectOne('/api/ui/users').flush({ users: [invited] });
    await fixture.whenStable();
    const dialog = page.querySelector('dialog[open]');
    const code = dialog?.querySelector('input');
    if (!dialog || !code) throw new Error('No code dialog');
    code.value = '123456';
    code.dispatchEvent(new Event('input'));
    dialog.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
    backend.expectOne('/api/ui/step-up').flush(null);
    backend
      .expectOne(`${path}/invitation`)
      .flush({ invitation: 'sent', expiresAtUtc: '2099-11-27T14:30:00Z' });
    fixture.detectChanges();
    backend.expectOne('/api/ui/users').flush({ users: [invited] });
    await fixture.whenStable();
    expect(page.querySelector('.flash-success')?.textContent).toBe(
      'Invitation sent to ada@example.test. It expires in 72 hours.',
    );
  });
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
    const logout = backend.expectOne('/api/ui/auth/sign-out');
    expect(logout.request.headers.get('RequestVerificationToken')).toBe('step-up-sign-out');
    expect(assign).not.toHaveBeenCalled();
    logout.flush('');
    await fixture.whenStable();
    expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login?ReturnUrl=%2FAdmin%2FUsers');
    backend.expectNone(`${path}/enable`);
    document.cookie = 'XSRF-TOKEN=; path=/; max-age=0';
  });

  it('a failed sign-out keeps sign-in step-up open with a retryable error', async () => {
    const { fixture, dialog } = await prompted('sign_in');
    button(dialog, 'Sign in again').click();
    backend
      .expectOne('/api/ui/auth/sign-out')
      .flush('', { status: 503, statusText: 'Unavailable' });
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
    button(page, 'End their sessions').click();
    backend
      .expectOne(`${path}/sign-out`)
      .flush({ code, title: 'The server outcome.' }, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    backend.expectOne('/api/ui/users').flush({ users: [colleague] });
    await fixture.whenStable();
    expect(page.querySelector('.flash-error')?.textContent).toBe('The server outcome.');
  });
  it('leaves for sign-in with the unrecorded warning after signing yourself out without a record', async () => {
    const { fixture, page } = await loaded({ ...colleague, isYou: true });
    button(page, 'Force logout').click();
    await fixture.whenStable();
    button(page, 'End their sessions').click();
    backend
      .expectOne(`${path}/sign-out`)
      .flush(
        { code: 'revoked_unrecorded', title: 'The server outcome.' },
        { status: 503, statusText: 'Unavailable' },
      );
    expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login?signOut=revoked_unrecorded');
  });
});
