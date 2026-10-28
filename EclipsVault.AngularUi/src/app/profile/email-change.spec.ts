import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { EmailChange } from './email-change';
import type { Profile } from './profile-types';
import { ProfileWrites } from './profile-writes';
import { button, dialogs, fill, operation, profile } from './profile-test';

const pending = {
  operationId: operation.operationId,
  address: 'pending@example.test',
  expiresAtUtc: new Date(Date.now() + 300_000).toISOString(),
};
describe('EmailChange', () => {
  let backend: HttpTestingController;
  async function render(pendingEmail: Profile['pendingEmail'] = pending) {
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting(), ProfileWrites],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(EmailChange);
    fixture.componentRef.setInput('profile', { ...profile, pendingEmail });
    const changed = vi.fn();
    fixture.componentInstance.changed.subscribe(changed);
    fixture.detectChanges();
    backend.expectOne('/api/ui/me').flush({});
    const page = fixture.nativeElement as HTMLElement;
    dialogs(page);
    await fixture.whenStable();
    return { fixture, page, changed };
  }
  afterEach(() => backend.verify({ ignoreCancelled: true }));
  it('confirms the pending operation once and refreshes shared account state', async () => {
    const { fixture, page } = await render();
    fill(page, 'mailbox-code', 'mailbox-secret');
    await fixture.whenStable();
    button(page, 'Confirm email').click();
    button(page, 'Confirm email').click();
    const confirm = backend.expectOne('/api/ui/profile/email/confirm');
    expect(confirm.request.body).toEqual({
      operationId: operation.operationId,
      code: 'mailbox-secret',
    });
    confirm.flush(null);
    fixture.detectChanges();
    backend.expectOne('/api/ui/me').flush({});
    await fixture.whenStable();
    expect(page.querySelector<HTMLInputElement>('#mailbox-code')?.value).toBe('');
    expect(TestBed.inject(ProfileWrites).status().message).toContain('Your email was changed.');
  });
  it('keeps the pending address after a wrong code and clears that code', async () => {
    const { fixture, page } = await render();
    fill(page, 'mailbox-code', 'wrong-secret');
    await fixture.whenStable();
    button(page, 'Confirm email').click();
    backend
      .expectOne('/api/ui/profile/email/confirm')
      .flush(
        { code: 'email_code_invalid', title: 'That mailbox code is not valid.' },
        { status: 400, statusText: 'Bad Request' },
      );
    await fixture.whenStable();
    expect(page.textContent).toContain('pending@example.test');
    expect(page.querySelector<HTMLInputElement>('#mailbox-code')?.value).toBe('');
    expect(TestBed.inject(ProfileWrites).status().error).toBe(true);
  });
  it('cancels the pending email explicitly without activating it', async () => {
    const { page } = await render();
    button(page, 'Cancel email change').click();
    const cancel = backend.expectOne({ method: 'DELETE', url: '/api/ui/profile/email/pending' });
    expect(cancel.request.body).toEqual({ operationId: operation.operationId });
    cancel.flush(null);
    backend.expectNone('/api/ui/profile/email/confirm');
  });
  it('re-verifies the same frozen pending target before a deliberate resend', async () => {
    const { fixture, page } = await render();
    button(page, 'Resend code').click();
    await fixture.whenStable();
    expect(page.querySelector('dialog')?.textContent).toContain(
      'Change email to pending@example.test.',
    );
    backend.expectNone('/api/ui/profile/operations');
    fill(page, 'verification-password', 'Current-Password');
    fill(page, 'verification-code', '123456');
    button(page, 'Verify account').click();
    backend
      .expectOne(`/api/ui/profile/operations/${operation.operationId}/verify`)
      .flush({ operationId: operation.operationId, commitByUtc: '2099-01-01T00:00:00Z' });
    backend.expectOne('/api/ui/profile/email/resend').flush(pending);
    await fixture.whenStable();
    expect(TestBed.inject(ProfileWrites).status().message).toContain(
      'Your current email is still active.',
    );
  });
  it('asks for fresh proof after an expired grant without replaying confirmation', async () => {
    const { fixture, page, changed } = await render();
    fill(page, 'mailbox-code', 'mailbox-secret');
    button(page, 'Confirm email').click();
    backend
      .expectOne('/api/ui/profile/email/confirm')
      .flush(
        { code: 'verification_expired', title: 'Verify your account again.' },
        { status: 409, statusText: 'Conflict' },
      );
    await fixture.whenStable();
    expect(changed).toHaveBeenCalledOnce();
    expect(TestBed.inject(ProfileWrites).status().message).toBe('Verify your account again.');
    button(page, 'Verify account again').click();
    await fixture.whenStable();
    fill(page, 'verification-password', 'Current-Password');
    fill(page, 'verification-code', '123456');
    button(page, 'Verify account').click();
    backend
      .expectOne(`/api/ui/profile/operations/${operation.operationId}/verify`)
      .flush({ operationId: operation.operationId, commitByUtc: '2099-01-01T00:00:00Z' });
    await fixture.whenStable();
    expect(button(page, 'Confirm email').disabled).toBe(false);
    backend.expectNone('/api/ui/profile/email/confirm');
  });
  it.each([
    ['email_code_expired', 'The mailbox code expired. Start the change again.'],
    ['email_code_invalid', 'That mailbox code is not valid.'],
  ])('reconciles a removed pending operation after %s', async (code, title) => {
    const { fixture, page, changed } = await render();
    fill(page, 'mailbox-code', 'last-wrong-code');
    button(page, 'Confirm email').click();
    backend
      .expectOne('/api/ui/profile/email/confirm')
      .flush({ code, title }, { status: 400, statusText: 'Bad Request' });
    expect(changed).toHaveBeenCalledOnce();
    fixture.componentRef.setInput('profile', { ...profile, pendingEmail: null });
    await fixture.whenStable();
    expect(page.querySelector('#mailbox-code')).toBeNull();
    expect(button(page, 'Change email')).toBeDefined();
    expect(page.querySelector<HTMLInputElement>('#active-email')?.value).toBe(
      'morgan@example.test',
    );
  });
  it('reconciles a missing operation after Cancel instead of keeping stale pending controls', async () => {
    const { fixture, page, changed } = await render();
    button(page, 'Cancel email change').click();
    backend
      .expectOne('/api/ui/profile/email/pending')
      .flush(
        { code: 'operation_not_found', title: 'That change no longer exists. Start it again.' },
        { status: 404, statusText: 'Not Found' },
      );
    expect(changed).toHaveBeenCalledOnce();
    fixture.componentRef.setInput('profile', profile);
    await fixture.whenStable();
    expect(button(page, 'Change email')).toBeDefined();
    expect(page.querySelector('#mailbox-code')).toBeNull();
  });
  it('reads pending state after SMTP request failure without activating or retrying', async () => {
    const { fixture, page, changed } = await render(null);
    fill(page, 'new-email', 'pending@example.test');
    button(page, 'Change email').click();
    backend.expectOne('/api/ui/profile/operations').flush({ ...operation, kind: 'ChangeEmail' });
    await fixture.whenStable();
    fill(page, 'verification-password', 'Current-Password');
    fill(page, 'verification-code', '123456');
    button(page, 'Verify account').click();
    backend
      .expectOne(`/api/ui/profile/operations/${operation.operationId}/verify`)
      .flush({ operationId: operation.operationId, commitByUtc: '2099-01-01T00:00:00Z' });
    backend
      .expectOne('/api/ui/profile/email/request')
      .flush(
        { code: 'mail_unavailable', title: 'Delivery failed. Verify again before resending.' },
        { status: 409, statusText: 'Conflict' },
      );
    expect(changed).toHaveBeenCalledOnce();
    fixture.componentRef.setInput('profile', { ...profile, pendingEmail: pending });
    await fixture.whenStable();
    expect(page.querySelector<HTMLInputElement>('#active-email')?.value).toBe(
      'morgan@example.test',
    );
    expect(TestBed.inject(ProfileWrites).status().message).toContain(
      'Verify again before resending.',
    );
    backend.expectNone('/api/ui/profile/email/request');
  });
  it.each([
    ['mail_unavailable', 409, 'Delivery failed. Verify again before resending.'],
    ['throttled', 429, 'Too many attempts. Wait before resending.'],
  ])('announces %s after a failed resend without repeating it', async (code, status, title) => {
    const { fixture, page, changed } = await render();
    button(page, 'Resend code').click();
    await fixture.whenStable();
    fill(page, 'verification-password', 'Current-Password');
    fill(page, 'verification-code', '123456');
    button(page, 'Verify account').click();
    backend
      .expectOne(`/api/ui/profile/operations/${operation.operationId}/verify`)
      .flush({ operationId: operation.operationId, commitByUtc: '2099-01-01T00:00:00Z' });
    backend
      .expectOne('/api/ui/profile/email/resend')
      .flush({ code, title }, { status, statusText: 'Refused' });
    await fixture.whenStable();
    expect(changed).toHaveBeenCalledOnce();
    expect(TestBed.inject(ProfileWrites).status().message).toBe(title);
    backend.expectNone('/api/ui/profile/email/resend');
  });
  it('keeps the pending mailbox when re-verification is cancelled and restores focus', async () => {
    const { fixture, page } = await render();
    const opener = button(page, 'Resend code');
    opener.focus();
    opener.click();
    await fixture.whenStable();
    fill(page, 'verification-password', 'Current-Password');
    fill(page, 'verification-code', '123456');
    await fixture.whenStable();
    button(page, 'Cancel').click();
    await fixture.whenStable();
    expect(page.textContent).toContain('pending@example.test');
    expect(button(page, 'Resend code').disabled).toBe(false);
    expect(document.activeElement).toBe(opener);
    expect(page.querySelector<HTMLInputElement>('#verification-password')?.value).toBe('');
    backend.expectNone(`/api/ui/profile/operations/${operation.operationId}`);
  });
  it('disposes the mailbox code and in-flight confirmation on session loss', async () => {
    const { fixture, page, changed } = await render();
    fill(page, 'mailbox-code', 'mailbox-secret');
    await fixture.whenStable();
    button(page, 'Confirm email').click();
    const confirm = backend.expectOne('/api/ui/profile/email/confirm');
    document.dispatchEvent(new Event('vault-session-ended'));
    await fixture.whenStable();
    expect(confirm.cancelled).toBe(true);
    expect(page.querySelector<HTMLInputElement>('#mailbox-code')?.value).toBe('');
    expect(button(page, 'Confirm email').disabled).toBe(true);
    expect(changed).not.toHaveBeenCalled();
  });
  it('unlocks pending controls after a delayed verification reply has already expired', async () => {
    const { fixture, page } = await render();
    button(page, 'Verify account again').click();
    await fixture.whenStable();
    fill(page, 'verification-password', 'Current-Password');
    fill(page, 'verification-code', '123456');
    button(page, 'Verify account').click();
    backend
      .expectOne(`/api/ui/profile/operations/${operation.operationId}/verify`)
      .flush({ operationId: operation.operationId, commitByUtc: '2000-01-01T00:00:00Z' });
    await fixture.whenStable();
    expect(page.querySelector('dialog')?.open).toBe(false);
    expect(button(page, 'Verify account again').disabled).toBe(false);
    expect(TestBed.inject(ProfileWrites).status().message).toContain('Your verification expired.');
  });
});
