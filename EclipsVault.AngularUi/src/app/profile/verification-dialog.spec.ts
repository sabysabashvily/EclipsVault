import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { PasskeyProof } from './passkey-proof';
import { ProfileWrites } from './profile-writes';
import { button, dialogs, fill, operation } from './profile-test';
import { VerificationDialog } from './verification-dialog';

describe('VerificationDialog', () => {
  let backend: HttpTestingController;
  const passkey = { get: vi.fn() };
  async function render() {
    TestBed.configureTestingModule({
      providers: [
        ...appConfig.providers,
        provideHttpClientTesting(),
        ProfileWrites,
        { provide: PasskeyProof, useValue: passkey },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(VerificationDialog);
    fixture.detectChanges();
    backend.expectOne('/api/ui/me').flush({});
    const page = fixture.nativeElement as HTMLElement;
    dialogs(page);
    const opener = document.createElement('button');
    document.body.append(opener);
    opener.focus();
    fixture.componentInstance.open(operation, 'Change email to frozen@example.test.');
    await fixture.whenStable();
    return { fixture, page, opener };
  }
  afterEach(() => {
    backend.verify({ ignoreCancelled: true });
    document.body.querySelectorAll('button').forEach((button) => button.remove());
    vi.restoreAllMocks();
  });

  it('announces the dialog name and frozen intent', async () => {
    const { page } = await render();
    const dialog = page.querySelector('dialog')!;
    expect(
      page.querySelector(`[id="${dialog.getAttribute('aria-labelledby')}"]`)?.textContent,
    ).toBe('Verify your account');
    expect(
      page.querySelector(`[id="${dialog.getAttribute('aria-describedby')}"]`)?.textContent,
    ).toBe('Change email to frozen@example.test.');
  });
  it('offers only the methods accepted for this operation', async () => {
    const { page } = await render();
    expect(
      [...page.querySelectorAll('option')].map((option) => option.textContent?.trim()),
    ).toEqual(['Password and authenticator code', 'Password and recovery code', 'Passkey']);
  });
  it('focuses Cancel by default', async () => {
    const { page } = await render();
    expect(document.activeElement).toBe(button(page, 'Cancel'));
  });
  it('Escape cancels the operation, clears proof and returns focus', async () => {
    const { fixture, page, opener } = await render();
    fill(page, 'verification-password', 'Sensitive-Password');
    fill(page, 'verification-code', '123456');
    await fixture.whenStable();
    page.querySelector('dialog')!.dispatchEvent(new Event('cancel', { cancelable: true }));
    backend
      .expectOne({ method: 'DELETE', url: `/api/ui/profile/operations/${operation.operationId}` })
      .flush(null);
    await fixture.whenStable();
    expect(page.querySelector<HTMLInputElement>('#verification-password')?.value).toBe('');
    expect(page.querySelector<HTMLInputElement>('#verification-code')?.value).toBe('');
    expect(page.querySelector('dialog')?.open).toBe(false);
    expect(document.activeElement).toBe(opener);
  });
  it('submits a recovery proof once without falling back to another method', async () => {
    const { fixture, page } = await render();
    const method = page.querySelector('select')!;
    method.value = 'PasswordAndRecoveryCode';
    method.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    fill(page, 'verification-password', 'Current-Password');
    fill(page, 'verification-code', 'recovery-code');
    const form = page.querySelector('form')!;
    form.dispatchEvent(new Event('submit'));
    form.dispatchEvent(new Event('submit'));
    const proof = backend.expectOne(`/api/ui/profile/operations/${operation.operationId}/verify`);
    expect(proof.request.body).toEqual({
      method: 'PasswordAndRecoveryCode',
      currentPassword: 'Current-Password',
      code: 'recovery-code',
    });
    proof.flush({ operationId: operation.operationId, commitByUtc: '2099-01-01T00:00:00Z' });
  });
  it('leaves passkey cancellation inline without submitting a password proof', async () => {
    const { fixture, page } = await render();
    passkey.get.mockRejectedValue(new DOMException('Cancelled', 'NotAllowedError'));
    const method = page.querySelector('select')!;
    method.value = 'Passkey';
    method.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    button(page, 'Verify account').click();
    backend
      .expectOne(`/api/ui/profile/operations/${operation.operationId}/passkey/begin`)
      .flush({ publicKey: {} });
    await fixture.whenStable();
    expect(page.textContent).toContain('No other verification method was attempted.');
    backend.expectNone(`/api/ui/profile/operations/${operation.operationId}/verify`);
  });
  it('completes the passkey proof with the current password for a password change', async () => {
    const { fixture, page } = await render();
    fixture.componentInstance.open(operation, 'Change password.', { needsPassword: true });
    await fixture.whenStable();
    passkey.get.mockResolvedValue({ id: 'credential' });
    const method = page.querySelector('select')!;
    method.value = 'Passkey';
    method.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    fill(page, 'verification-password', 'Current-Password');
    button(page, 'Verify account').click();
    backend
      .expectOne(`/api/ui/profile/operations/${operation.operationId}/passkey/begin`)
      .flush({ publicKey: {} });
    await fixture.whenStable();
    const complete = backend.expectOne(
      `/api/ui/profile/operations/${operation.operationId}/passkey/complete`,
    );
    expect(complete.request.body).toEqual({
      credential: { id: 'credential' },
      currentPassword: 'Current-Password',
    });
    complete.flush({ operationId: operation.operationId, commitByUtc: '2099-01-01T00:00:00Z' });
  });
  it('expires the dialog without submitting or retaining proof', async () => {
    const { fixture, page } = await render();
    vi.useFakeTimers();
    fixture.componentInstance.open(
      { ...operation, expiresAtUtc: new Date(Date.now() + 1000).toISOString() },
      'Change password.',
    );
    fill(page, 'verification-password', 'Sensitive');
    fixture.detectChanges();
    vi.advanceTimersByTime(1001);
    fixture.detectChanges();
    expect(page.textContent).toContain('Cancel and start it again.');
    expect(page.querySelector<HTMLInputElement>('#verification-password')?.value).toBe('');
    expect(button(page, 'Verify account').disabled).toBe(true);
    vi.useRealTimers();
  });
  it('cycles Tab from the final action to the method and Shift+Tab back', async () => {
    const { page } = await render();
    button(page, 'Verify account').focus();
    const forward = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
    page.querySelector('dialog')!.dispatchEvent(forward);
    expect(forward.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(page.querySelector('select'));
    const backward = new KeyboardEvent('keydown', {
      key: 'Tab',
      shiftKey: true,
      bubbles: true,
      cancelable: true,
    });
    page.querySelector('dialog')!.dispatchEvent(backward);
    expect(backward.defaultPrevented).toBe(true);
    expect(document.activeElement?.textContent?.trim()).toBe('Verify account');
  });
});
