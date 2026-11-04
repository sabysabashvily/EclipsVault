import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Type } from '@angular/core';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { appConfig } from '../app.config';
import { PAGE_LOCATION } from '../shell/command-palette';
import { NewUserPage } from './new-user-page';
import { EditUserPage } from './edit-user-page';

const id = '11111111-1111-1111-1111-111111111111';
const clearances = [
  { value: 1, label: 'Standard' },
  { value: 4, label: 'Top Secret' },
];
const metadata = { emailDomain: 'configured.example.test', clearances };
const details = {
  userId: id,
  username: 'ada',
  displayName: 'Ada Saved',
  clearance: 1,
  project: 'OPS',
  isYou: false,
  clearances,
};

function button(page: ParentNode, name: string): HTMLButtonElement {
  const found = [...page.querySelectorAll('button')].find(
    (item) => item.textContent?.trim() === name,
  );
  if (!found) throw new Error(`No button named ${name}`);
  return found;
}
function input(page: ParentNode, id: string): HTMLInputElement {
  const found = page.querySelector<HTMLInputElement>(`#${id}`);
  if (!found) throw new Error(`No input named ${id}`);
  return found;
}
function fill(page: ParentNode, id: string, value: string): void {
  const field = input(page, id);
  field.value = value;
  field.dispatchEvent(new Event('input'));
}
function submit(page: ParentNode): void {
  const form = page.querySelector('form');
  if (!form) throw new Error('No form');
  form.dispatchEvent(new Event('submit', { cancelable: true }));
}

interface FormCase {
  name: string;
  component: Type<unknown>;
  url: string;
  read: string;
  write: string;
  body: object;
  submitLabel: string;
  loading: string;
  failure: string;
  firstLabel: string;
  signInUrl: string;
  successBody: object | null;
}
const newForm: FormCase = {
  name: 'New user',
  submitLabel: 'Create user',
  component: NewUserPage,
  url: '/Admin/CreateUser',
  read: '/api/ui/users/new',
  write: '/api/ui/users',
  body: metadata,
  loading: 'Loading new user…',
  failure: 'Could not load new user. Try again.',
  firstLabel: 'First name',
  signInUrl: '/Account/Login?ReturnUrl=%2FAdmin%2FCreateUser',
  successBody: { userId: id, username: 'ada', email: 'ada.saved.1@configured.example.test' },
};
const editForm: FormCase = {
  name: 'Edit user',
  submitLabel: 'Save role',
  component: EditUserPage,
  url: `/Admin/EditUser/${id}`,
  read: `/api/ui/users/${id}`,
  write: `/api/ui/users/${id}/role`,
  body: details,
  loading: 'Loading user…',
  failure: 'Could not load user. Try again.',
  firstLabel: 'Clearance level',
  signInUrl: '/Account/Login?ReturnUrl=%2FAdmin%2FEditUser%2F11111111-1111-1111-1111-111111111111',
  successBody: null,
};
let backend: HttpTestingController;
const assign = vi.fn();
function render(form: FormCase) {
  history.replaceState(null, '', form.url);
  TestBed.configureTestingModule({
    providers: [
      ...appConfig.providers,
      provideHttpClientTesting(),
      { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id }) } } },
      { provide: PAGE_LOCATION, useValue: { assign } },
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  const fixture = TestBed.createComponent(form.component);
  fixture.detectChanges();
  const page: HTMLElement = fixture.nativeElement;
  page.querySelectorAll('dialog').forEach((dialog) => {
    dialog.showModal = () => dialog.setAttribute('open', '');
    dialog.close = () => dialog.removeAttribute('open');
  });
  return { fixture, page, navigate };
}
async function loaded(form: FormCase) {
  const rendered = render(form);
  backend.expectOne(form.read).flush(form.body);
  await rendered.fixture.whenStable();
  return rendered;
}
beforeEach(() => assign.mockReset());
afterEach(() => {
  backend.verify();
  vi.useRealTimers();
});

describe.each([newForm, editForm])('$name', (form) => {
  const { read, write, body } = form;
  it('shows loading and retries a failed metadata read', async () => {
    const { fixture, page } = render(form);
    expect(page.textContent).toContain(form.loading);
    backend.expectOne(read).flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(page.textContent).toContain(form.failure);
    button(page, 'Try again').click();
    fixture.detectChanges();
    backend.expectOne(read).flush(body);
    await fixture.whenStable();
    expect(page.querySelector('label')?.textContent).toBe(form.firstLabel);
  });

  it('shows a server field error beside Project key', async () => {
    const { fixture, page } = await loaded(form);
    submit(page);
    backend.expectOne(write).flush(
      {
        code: 'validation_failed',
        title: 'Project is required.',
        errors: { project: ['Project is required.'] },
      },
      { status: 400, statusText: 'Bad request' },
    );
    await fixture.whenStable();
    expect(input(page, 'project').parentElement?.querySelector('.field-error')?.textContent).toBe(
      'Project is required.',
    );
    expect(page.querySelector('fieldset')?.disabled).toBe(false);
  });

  it.each(['username_taken', 'last_administrator', 'account_changed', 'audit_unavailable'])(
    'announces %s without navigating',
    async (code) => {
      const { fixture, page, navigate } = await loaded(form);
      submit(page);
      backend
        .expectOne(write)
        .flush(
          { code, title: 'The saved account was refused.' },
          { status: 409, statusText: 'Conflict' },
        );
      await fixture.whenStable();
      expect(page.querySelector('.flash-error')?.textContent).toBe(
        'The saved account was refused.',
      );
      expect(navigate).not.toHaveBeenCalled();
    },
  );

  async function prompted(proof: 'code' | 'sign_in') {
    const rendered = await loaded(form);
    button(rendered.page, form.submitLabel).focus();
    submit(rendered.page);
    backend
      .expectOne(write)
      .flush({ code: 'step_up_required', proof }, { status: 403, statusText: 'Forbidden' });
    await rendered.fixture.whenStable();
    const dialog = rendered.page.querySelector('dialog[open]');
    if (!dialog) throw new Error('No proof dialog');
    return { ...rendered, dialog };
  }

  it.each([
    { proof: 'code', prompt: 'Enter an authenticator code to continue.' },
    { proof: 'sign_in', prompt: 'Sign in again to continue.' },
  ] satisfies { proof: 'code' | 'sign_in'; prompt: string }[])(
    'cancelling $proof proof leaves the form available without a write',
    async ({ proof, prompt }) => {
      const { fixture, page, dialog } = await prompted(proof);
      expect(dialog.textContent).toContain(prompt);
      button(dialog, 'Cancel').click();
      await fixture.whenStable();
      expect(dialog.hasAttribute('open')).toBe(false);
      expect(page.querySelector('fieldset')?.disabled).toBe(false);
      expect(document.activeElement).toBe(button(page, form.submitLabel));
      backend.expectNone(write);
    },
  );

  it('sign-in proof ends the stale session and preserves the form return URL', async () => {
    const { fixture, dialog } = await prompted('sign_in');
    button(dialog, 'Sign in again').click();
    backend.expectOne('/Account/Logout').flush('');
    await fixture.whenStable();
    expect(assign).toHaveBeenCalledExactlyOnceWith(form.signInUrl);
    backend.expectNone(write);
  });

  it('a wrong code remains retryable and successful proof retries the grant once', async () => {
    const { fixture, page, dialog } = await prompted('code');
    const code = dialog.querySelector('input');
    if (!code) throw new Error('No authenticator input');
    code.value = '123456';
    submit(dialog);
    backend
      .expectOne('/api/ui/step-up')
      .flush(
        { code: 'step_up_failed', title: 'That authenticator code is not valid. Try again.' },
        { status: 400, statusText: 'Bad request' },
      );
    await fixture.whenStable();
    expect(dialog.textContent).toContain('That authenticator code is not valid. Try again.');
    submit(dialog);
    backend.expectOne('/api/ui/step-up').flush(null);
    backend
      .expectOne(write)
      .flush(
        { code: 'step_up_required', proof: 'code', title: 'Confirm again.' },
        { status: 403, statusText: 'Forbidden' },
      );
    await fixture.whenStable();
    expect(dialog.hasAttribute('open')).toBe(false);
    expect(page.querySelector('.flash-error')?.textContent).toBe('Confirm again.');
    expect(code.value).toBe('');
  });

  it('a successful write returns to Users', async () => {
    const { fixture, page, navigate } = await loaded(form);
    submit(page);
    backend.expectOne(write).flush(form.successBody);
    await fixture.whenStable();
    expect(navigate).toHaveBeenCalledExactlyOnceWith('/Admin/Users');
  });
});

describe('New user fields', () => {
  it('uses the configured domain and preserves field order, labels and initial values', async () => {
    const { page } = await loaded(newForm);
    expect(page.textContent).toContain('firstname.lastname.N@configured.example.test');
    expect(
      [...page.querySelectorAll('section form label')].map((label) => label.textContent),
    ).toEqual([
      'First name',
      'Last name',
      'Username',
      'Initial password',
      'Clearance level',
      'Project key',
    ]);
    expect(page.querySelector('select')?.value).toBe('1');
    expect(input(page, 'firstName').placeholder).toBe('Saba');
    expect(input(page, 'lastName').placeholder).toBe('Sabashvili');
  });
  it('debounces breach feedback, shows strength and discards an obsolete verdict', async () => {
    const { fixture, page } = await loaded(newForm);
    vi.useFakeTimers();
    fill(page, 'password', 'Different-Password-456!');
    fixture.detectChanges();
    vi.advanceTimersByTime(399);
    backend.expectNone('/api/ui/passwords/check-breached');
    vi.advanceTimersByTime(1);
    const preview = backend.expectOne('/api/ui/passwords/check-breached');
    expect(preview.request.body).toEqual({ password: 'Different-Password-456!' });
    preview.flush({ compromised: true });
    fixture.detectChanges();
    expect(page.textContent).toContain('This password has appeared in known data breaches');
    expect(page.textContent).toContain('Very strong');
    submit(page);
    backend.expectNone(newForm.write);
    fill(page, 'password', 'short');
    fixture.detectChanges();
    expect(page.textContent).toContain('Too short — 7 more characters needed');
    expect(page.textContent).not.toContain('This password has appeared in known data breaches');
    vi.useRealTimers();
  });
  it('clears the plaintext password when the session ends', async () => {
    const { fixture, page } = await loaded(newForm);
    fill(page, 'password', 'Different-Password-456!');
    document.dispatchEvent(new Event('vault-session-ended'));
    await fixture.whenStable();
    expect(input(page, 'password').value).toBe('');
    expect(page.querySelector('fieldset')?.disabled).toBe(true);
  });
});

describe('Edit user fields', () => {
  it('an unknown account shows User not found with Back to users', async () => {
    const { fixture, page } = render(editForm);
    backend
      .expectOne(editForm.read)
      .flush(
        { code: 'user_not_found', title: 'User not found.' },
        { status: 404, statusText: 'Not found' },
      );
    await fixture.whenStable();
    expect(page.textContent).toContain('User not found.');
    expect(page.querySelector('a')?.textContent).toBe('Back to users');
  });
  it('shows the own-account note and returns through the protected Users route', async () => {
    const { fixture, page } = render(editForm);
    backend.expectOne(editForm.read).flush({ ...details, isYou: true, clearance: 4 });
    await fixture.whenStable();
    expect(page.textContent).toContain('This is your own account');
    expect(page.querySelector('select')?.value).toBe('4');
    fill(page, 'project', 'OTHER');
    submit(page);
    const request = backend.expectOne(editForm.write);
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ clearance: 4, project: 'OTHER' });
    request.flush(null);
    await fixture.whenStable();
    expect(assign).toHaveBeenCalledExactlyOnceWith('/Admin/Users');
  });
  it('an unchanged own Unicode project returns through Users for the saved notice', async () => {
    const { fixture, page } = render(editForm);
    backend
      .expectOne(editForm.read)
      .flush({ ...details, isYou: true, clearance: 4, project: 'STRAßE' });
    await fixture.whenStable();
    submit(page);
    const request = backend.expectOne(editForm.write);
    expect(request.request.body).toEqual({ clearance: 4, project: 'STRAßE' });
    request.flush(null);
    await fixture.whenStable();
    expect(assign).toHaveBeenCalledExactlyOnceWith('/Admin/Users');
  });
});
