import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { ChangePasswordPage } from './change-password-page';
import { button, dialogs, fill, operation, profile, refreshed } from './profile-test';

describe('ChangePasswordPage', () => {
  let backend: HttpTestingController;
  async function render(value = profile) {
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ChangePasswordPage);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    dialogs(page);
    backend.expectOne('/api/ui/me').flush({});
    backend.expectOne('/api/ui/profile').flush(value);
    await fixture.whenStable();
    return { fixture, page };
  }
  afterEach(() => backend.verify({ ignoreCancelled: true }));

  function passwords(page: HTMLElement) {
    fill(page, 'current-password', 'Current-Password-123!');
    fill(page, 'new-password', 'Different-Password-456!');
    fill(page, 'confirm-password', 'Different-Password-456!');
  }
  it('shows the Legacy fields, length rules, reveal button and advisory meter', async () => {
    const { fixture, page } = await render();
    passwords(page);
    await fixture.whenStable();
    expect(page.querySelector('h1')?.textContent).toBe('Change password');
    expect(page.querySelector('#new-password')?.getAttribute('maxlength')).toBe('256');
    expect(page.textContent).toContain('Very strong');
    page.querySelector<HTMLButtonElement>('[aria-label="Show password"]')!.click();
    await fixture.whenStable();
    expect(page.querySelector<HTMLInputElement>('#new-password')?.type).toBe('text');
  });
  it.each([
    ['short', 'short', 'Your new password must be 12–256 characters.'],
    ['Different-Password-456!', 'Mismatch-Password-456!', 'The passwords do not match.'],
    ['Current-Password-123!', 'Current-Password-123!', 'Choose a different password'],
  ])('refuses %s with inline validation', async (candidate, confirmation, message) => {
    const { fixture, page } = await render();
    fill(page, 'current-password', 'Current-Password-123!');
    fill(page, 'new-password', candidate);
    fill(page, 'confirm-password', confirmation);
    page.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();
    expect(page.textContent).toContain(message);
    backend.expectNone('/api/ui/profile/operations');
  });
  it('uses server capability reasons and disables changes when local proof is unavailable', async () => {
    const { page } = await render({
      ...profile,
      changes: {
        ...profile.changes,
        changePassword: { available: false, unavailableReason: 'method_unavailable' },
      },
    });
    expect(page.textContent).toContain('existing local credential');
    expect(page.querySelector('fieldset')?.disabled).toBe(true);
  });
  it('freezes the candidate once, verifies inline, commits once and refreshes the session', async () => {
    const { fixture, page } = await render();
    passwords(page);
    await fixture.whenStable();
    const form = page.querySelector('form')!;
    form.dispatchEvent(new Event('submit', { cancelable: true }));
    form.dispatchEvent(new Event('submit', { cancelable: true }));
    const prepare = backend.expectOne('/api/ui/profile/operations');
    expect(prepare.request.body).toEqual({
      kind: 'ChangePassword',
      newPassword: 'Different-Password-456!',
    });
    prepare.flush(operation);
    await fixture.whenStable();
    expect(page.querySelector<HTMLInputElement>('#new-password')?.value).toBe('');
    fill(page, 'verification-code', '123456');
    button(page, 'Verify account').click();
    backend
      .expectOne(`/api/ui/profile/operations/${operation.operationId}/verify`)
      .flush(
        { code: 'verification_failed', title: 'Check your proof.' },
        { status: 400, statusText: 'Bad Request' },
      );
    await fixture.whenStable();
    expect(page.textContent).toContain('Check your proof.');
    expect(page.querySelector('dialog')?.open).toBe(true);
    fill(page, 'verification-password', 'Current-Password-123!');
    fill(page, 'verification-code', '654321');
    button(page, 'Verify account').click();
    backend
      .expectOne(`/api/ui/profile/operations/${operation.operationId}/verify`)
      .flush({ operationId: operation.operationId, commitByUtc: '2099-01-01T00:00:00Z' });
    backend.expectOne('/api/ui/profile/password').flush(null);
    fixture.detectChanges();
    refreshed(backend);
    await fixture.whenStable();
    expect(page.textContent).toContain('Your password was changed.');
    expect(page.querySelector('dialog')?.open).toBe(false);
  });
  it('clears secrets and cancels previews on session loss or page departure', async () => {
    const { fixture, page } = await render();
    passwords(page);
    await fixture.whenStable();
    document.dispatchEvent(new Event('vault-session-ended'));
    await fixture.whenStable();
    expect(page.querySelector<HTMLInputElement>('#current-password')?.value).toBe('');
    expect(page.querySelector<HTMLInputElement>('#new-password')?.value).toBe('');
    expect(page.querySelector<HTMLInputElement>('#confirm-password')?.value).toBe('');
    expect(page.querySelector('fieldset')?.disabled).toBe(true);
  });
  it('clears candidate fields after Cancel and never commits the operation', async () => {
    const { fixture, page } = await render();
    passwords(page);
    button(page, 'Change password').click();
    backend.expectOne('/api/ui/profile/operations').flush(operation);
    await fixture.whenStable();
    button(page, 'Cancel').click();
    backend
      .expectOne({ method: 'DELETE', url: `/api/ui/profile/operations/${operation.operationId}` })
      .flush(null);
    await fixture.whenStable();
    expect(page.querySelector<HTMLInputElement>('#verification-password')?.value).toBe('');
    backend.expectNone('/api/ui/profile/password');
  });
  it('debounces breach preview and discards a verdict when the candidate becomes short', async () => {
    const { fixture, page } = await render();
    vi.useFakeTimers();
    fill(page, 'new-password', 'Different-Password-456!');
    fixture.detectChanges();
    vi.advanceTimersByTime(399);
    backend.expectNone('/api/ui/passwords/check-breached');
    vi.advanceTimersByTime(1);
    const preview = backend.expectOne('/api/ui/passwords/check-breached');
    expect(preview.request.body).toEqual({ password: 'Different-Password-456!' });
    fill(page, 'new-password', 'short');
    fixture.detectChanges();
    expect(preview.cancelled).toBe(true);
    vi.useRealTimers();
    await fixture.whenStable();
    expect(page.textContent).not.toContain('Not found in the breach corpus.');
  });
});
