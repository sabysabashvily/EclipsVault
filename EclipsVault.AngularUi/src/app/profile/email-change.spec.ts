import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { EmailChange } from './email-change';
import { ProfileSession } from './profile-session';
import { button, dialogs, fill, operation, profile } from './profile-test';

const pending = {
  operationId: operation.operationId,
  address: 'pending@example.test',
  expiresAtUtc: new Date(Date.now() + 300_000).toISOString(),
};
describe('EmailChange', () => {
  let backend: HttpTestingController;
  async function render() {
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting(), ProfileSession],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(EmailChange);
    fixture.componentRef.setInput('profile', { ...profile, pendingEmail: pending });
    fixture.detectChanges();
    backend.expectOne('/api/ui/me').flush({});
    const page = fixture.nativeElement as HTMLElement;
    dialogs(page);
    await fixture.whenStable();
    return { fixture, page };
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
    expect(TestBed.inject(ProfileSession).status().message).toContain('Your email was changed.');
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
    expect(TestBed.inject(ProfileSession).status().error).toBe(true);
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
    expect(TestBed.inject(ProfileSession).status().message).toContain(
      'Your current email is still active.',
    );
  });
});
