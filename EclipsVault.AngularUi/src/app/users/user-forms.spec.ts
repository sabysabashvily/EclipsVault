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
const metadata = { clearances, invitationsAvailable: true };
const details = {
  userId: id,
  username: 'ada',
  displayName: 'Ada Saved',
  email: 'ada@example.test',
  clearance: 1,
  project: 'OPS',
  isYou: false,
  clearances,
  awaitingActivation: false,
  invitationExpiresAtUtc: null as string | null,
};
const invited = {
  ...details,
  awaitingActivation: true,
  invitationExpiresAtUtc: '2099-11-26T14:30:00Z',
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
  successBody: {
    userId: id,
    username: 'ada',
    email: 'ada@example.test',
    invitation: 'sent',
    expiresAtUtc: '2026-11-25T12:00:00+00:00',
  },
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
    backend.expectOne('/api/ui/auth/sign-out').flush('');
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
});

describe('New user fields', () => {
  it('asks for the email in place of a password and preserves field order, labels and initial values', async () => {
    const { page } = await loaded(newForm);
    expect(
      [...page.querySelectorAll('section form label')].map((label) => label.textContent),
    ).toEqual(['First name', 'Last name', 'Username', 'Email', 'Clearance level', 'Project key']);
    expect(page.textContent).toContain(
      'The invitation goes here. They choose their own password and set up their authenticator.',
    );
    expect(page.querySelector('input[type="password"]')).toBeNull();
    expect(page.querySelector('select')?.value).toBe('1');
    expect(input(page, 'firstName').placeholder).toBe('Saba');
    expect(input(page, 'lastName').placeholder).toBe('Sabashvili');
  });

  it.each([
    ['sent', 'Invitation sent to ada@example.test. It expires in 72 hours.', false],
    [
      'not_delivered',
      "The account was created, but the invitation could not be delivered. Resend it from the user's page.",
      true,
    ],
  ])(
    'posts the email and shows the %s outcome in place of the form',
    async (invitation, message, error) => {
      const { fixture, page, navigate } = await loaded(newForm);
      fill(page, 'firstName', 'Ada');
      fill(page, 'lastName', 'Lovelace');
      fill(page, 'username', 'ada');
      fill(page, 'email', 'ada@example.test');
      fill(page, 'project', 'OPS');
      submit(page);
      const create = backend.expectOne(newForm.write);
      expect(create.request.body).toEqual({
        firstName: 'Ada',
        lastName: 'Lovelace',
        username: 'ada',
        email: 'ada@example.test',
        clearance: 1,
        project: 'OPS',
      });
      create.flush({ ...newForm.successBody, invitation });
      await fixture.whenStable();
      expect(page.querySelector('section form')).toBeNull();
      expect(page.querySelector(error ? '.flash-error' : '.flash-success')?.textContent).toBe(
        message,
      );
      expect(navigate).not.toHaveBeenCalled();
    },
  );
});

describe('New user without invitations', () => {
  it('explains what invitations need and keeps the form from submitting', async () => {
    const { fixture, page } = render(newForm);
    backend.expectOne(newForm.read).flush({ ...metadata, invitationsAvailable: false });
    await fixture.whenStable();
    expect(
      page.querySelector('.notice[role="status"]')?.textContent?.replace(/\s+/g, ' ').trim(),
    ).toBe(
      "Invitations need email delivery and the vault's public address (Identity:PublicUrl). Configure both to add accounts.",
    );
    expect(page.querySelector('fieldset')?.disabled).toBe(true);
  });
  it('shows no notice when invitations are available', async () => {
    const { page } = await loaded(newForm);
    expect(page.querySelector('.notice')).toBeNull();
    expect(page.querySelector('fieldset')?.disabled).toBe(false);
  });
});

describe('Edit user invitation', () => {
  async function invitedPage() {
    const rendered = render(editForm);
    backend.expectOne(editForm.read).flush(invited);
    await rendered.fixture.whenStable();
    const panel = rendered.page.querySelector<HTMLElement>('#invitation');
    if (!panel) throw new Error('No invitation panel');
    return { ...rendered, panel };
  }
  function openDialog(page: ParentNode): Element {
    const dialog = page.querySelector('dialog[open]');
    if (!dialog) throw new Error('No open dialog');
    return dialog;
  }
  function replaceWith(panel: HTMLElement, email: string): void {
    fill(panel, 'invitation-email', email);
    panel.querySelector('form')?.dispatchEvent(new Event('submit', { cancelable: true }));
  }

  it('is absent for an active account', async () => {
    const { page } = await loaded(editForm);
    expect(page.querySelector('#invitation')).toBeNull();
  });
  it('shows the status pill, the stored address and both actions', async () => {
    const { panel } = await invitedPage();
    expect(panel.querySelector('h2')?.textContent).toBe('Invitation');
    expect(panel.querySelector('.badge.tone-info')?.textContent?.trim()).toBe(
      'Invited · expires 26 Nov 2099, 14:30 UTC',
    );
    expect(panel.textContent).toContain('Sent to ada@example.test.');
    expect(input(panel, 'invitation-email').value).toBe('ada@example.test');
    expect(button(panel, 'Resend invitation').type).toBe('button');
    expect(button(panel, 'Replace address').type).toBe('submit');
  });
  it('says an expired invitation in words', async () => {
    const { fixture, page } = render(editForm);
    backend.expectOne(editForm.read).flush({ ...invited, invitationExpiresAtUtc: null });
    await fixture.whenStable();
    expect(page.querySelector('#invitation .badge.tone-danger')?.textContent?.trim()).toBe(
      'Invitation expired',
    );
  });
  it('Resend confirms, writes, announces and reloads', async () => {
    const { fixture, page, panel } = await invitedPage();
    button(panel, 'Resend invitation').click();
    await fixture.whenStable();
    const dialog = openDialog(page);
    expect(dialog.textContent).toContain(
      'Send a new invitation to ada@example.test? The previous link stops working.',
    );
    button(dialog, 'Resend invitation').click();
    backend
      .expectOne({ method: 'POST', url: `/api/ui/users/${id}/invitation` })
      .flush({ invitation: 'sent', expiresAtUtc: '2099-11-27T14:30:00Z' });
    fixture.detectChanges();
    backend.expectOne(editForm.read).flush(invited);
    await fixture.whenStable();
    expect(page.querySelector('.flash-success')?.textContent).toBe(
      'Invitation sent to ada@example.test. It expires in 72 hours.',
    );
  });
  it('Replace asks with the decided sentence, sends no link and tells the administrator to resend', async () => {
    const { fixture, page, panel } = await invitedPage();
    replaceWith(panel, '  grace@example.test ');
    await fixture.whenStable();
    const dialog = openDialog(page);
    expect(dialog.textContent).toContain(
      'Use grace@example.test for ada? The link already sent stops working. Resend to send a new one there.',
    );
    expect(dialog.querySelector('.dialog-note')?.textContent).toBe('Saved to the audit log');
    button(dialog, 'Replace address').click();
    const request = backend.expectOne({
      method: 'POST',
      url: `/api/ui/users/${id}/invitation-address`,
    });
    expect(request.request.body).toEqual({ email: 'grace@example.test' });
    request.flush({ email: 'grace@example.test' });
    fixture.detectChanges();
    backend.expectOne(editForm.read).flush({ ...invited, email: 'grace@example.test' });
    await fixture.whenStable();
    backend.expectNone(`/api/ui/users/${id}/invitation`);
    expect(page.querySelector('.flash-success')?.textContent).toBe(
      'The invitation address is now grace@example.test. Resend the invitation to send a link there.',
    );
    expect(input(panel, 'invitation-email').value).toBe('grace@example.test');
  });
  it('cancelling Replace sends nothing', async () => {
    const { fixture, page, panel } = await invitedPage();
    replaceWith(panel, 'grace@example.test');
    await fixture.whenStable();
    button(openDialog(page), 'Cancel').click();
    await fixture.whenStable();
    backend.expectNone(`/api/ui/users/${id}/invitation-address`);
  });
  it('Replace asks for fresh proof, then retries once', async () => {
    const { fixture, page, panel } = await invitedPage();
    replaceWith(panel, 'grace@example.test');
    await fixture.whenStable();
    button(openDialog(page), 'Replace address').click();
    backend
      .expectOne(`/api/ui/users/${id}/invitation-address`)
      .flush({ code: 'step_up_required', proof: 'code' }, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable();
    const proof = openDialog(page);
    const code = proof.querySelector('input');
    if (!code) throw new Error('No code field');
    code.value = '123456';
    code.dispatchEvent(new Event('input'));
    submit(proof);
    backend.expectOne('/api/ui/step-up').flush(null);
    backend
      .expectOne(`/api/ui/users/${id}/invitation-address`)
      .flush({ email: 'grace@example.test' });
    fixture.detectChanges();
    backend.expectOne(editForm.read).flush(invited);
    await fixture.whenStable();
    expect(page.querySelector('.flash-success')?.textContent).toContain('grace@example.test');
  });
  it('cancelling Replace step-up returns focus to Replace address', async () => {
    const { fixture, page, panel } = await invitedPage();
    replaceWith(panel, 'grace@example.test');
    await fixture.whenStable();
    button(openDialog(page), 'Replace address').click();
    backend
      .expectOne(`/api/ui/users/${id}/invitation-address`)
      .flush({ code: 'step_up_required', proof: 'code' }, { status: 403, statusText: 'Forbidden' });
    await fixture.whenStable();
    button(openDialog(page), 'Cancel').click();
    await fixture.whenStable();
    expect(document.activeElement).toBe(button(panel, 'Replace address'));
  });
  it('announces a refused address', async () => {
    const { fixture, page, panel } = await invitedPage();
    replaceWith(panel, 'taken@example.test');
    await fixture.whenStable();
    button(openDialog(page), 'Replace address').click();
    backend
      .expectOne(`/api/ui/users/${id}/invitation-address`)
      .flush(
        { code: 'email_taken', title: 'That email address cannot be used.' },
        { status: 409, statusText: 'Conflict' },
      );
    await fixture.whenStable();
    expect(page.querySelector('.flash-error')?.textContent).toBe(
      'That email address cannot be used.',
    );
  });
});

describe('Edit user fields', () => {
  it('a successful write returns to Users', async () => {
    const { fixture, page, navigate } = await loaded(editForm);
    submit(page);
    backend.expectOne(editForm.write).flush(editForm.successBody);
    await fixture.whenStable();
    expect(navigate).toHaveBeenCalledExactlyOnceWith('/Admin/Users');
  });
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
