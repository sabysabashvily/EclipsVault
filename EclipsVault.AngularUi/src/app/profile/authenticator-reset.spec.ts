import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { PAGE_LOCATION } from '../shell/command-palette';
import { ProfilePage } from './profile-page';
import { button, dialogs, fill, operation, profile } from './profile-test';

describe('ProfilePage authenticator reset', () => {
  let backend: HttpTestingController;
  const assign = vi.fn();
  const resetOperation = { ...operation, kind: 'ResetAuthenticator' };
  const verifyUrl = `/api/ui/profile/operations/${operation.operationId}/verify`;

  async function render(account = profile) {
    TestBed.configureTestingModule({
      providers: [
        ...appConfig.providers,
        provideHttpClientTesting(),
        { provide: PAGE_LOCATION, useValue: { assign } },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ProfilePage);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    dialogs(page);
    backend.expectOne('/api/ui/me').flush({});
    backend.expectOne('/api/ui/profile').flush(account);
    await fixture.whenStable();
    const changes = page.querySelector<HTMLElement>('app-credential-change')!;
    return { fixture, page, changes };
  }
  afterEach(() => {
    backend.verify({ ignoreCancelled: true });
    assign.mockReset();
  });

  it('confirms, verifies the frozen reset, then sends the browser to sign-in', async () => {
    const { fixture, page, changes } = await render();
    button(page, 'Reset authenticator').click();
    await fixture.whenStable();
    expect(changes.querySelector('dialog[role=alertdialog]')?.textContent).toContain(
      "Reset your authenticator? You'll be signed out and set up a new one at next sign-in.",
    );
    button(changes, 'Continue').click();
    const prepare = backend.expectOne({ method: 'POST', url: '/api/ui/profile/operations' });
    expect(prepare.request.body).toEqual({ kind: 'ResetAuthenticator' });
    prepare.flush(resetOperation);
    await fixture.whenStable();
    expect(changes.textContent).toContain(
      'Reset your authenticator and recovery codes. Every session, including this one, will be signed out.',
    );
    fill(changes, 'credential-verification-password', 'Current-Password');
    fill(changes, 'credential-verification-code', '123456');
    button(changes, 'Verify account').click();
    backend
      .expectOne(verifyUrl)
      .flush({ operationId: operation.operationId, commitByUtc: '2099-01-01T00:00:00Z' });
    const reset = backend.expectOne({
      method: 'POST',
      url: '/api/ui/profile/authenticator/reset',
    });
    expect(reset.request.body).toEqual({ operationId: operation.operationId });
    reset.flush(null, { status: 204, statusText: 'No Content' });
    expect(assign).toHaveBeenCalledExactlyOnceWith('/Account/Login');
  });

  it('explains why reset is unavailable without an enrolled authenticator', async () => {
    const { page } = await render({
      ...profile,
      authenticatorEnrolled: false,
      changes: {
        ...profile.changes,
        resetAuthenticator: { available: false, unavailableReason: 'authenticator_not_enrolled' },
      },
    });
    expect(button(page, 'Reset authenticator').disabled).toBe(true);
    expect(page.textContent).toContain('This needs an authenticator app set up first.');
  });

  it('prepares nothing when the reset confirmation is cancelled', async () => {
    const { fixture, page, changes } = await render();
    button(page, 'Reset authenticator').click();
    await fixture.whenStable();
    button(changes, 'Cancel').click();
    backend.expectNone('/api/ui/profile/operations');
    expect(button(page, 'Reset authenticator').disabled).toBe(false);
  });

  it('prepares the confirmed reset even if another change is clicked behind the dialog', async () => {
    const { fixture, page, changes } = await render();
    button(page, 'Reset authenticator').click();
    await fixture.whenStable();
    button(page, 'Regenerate').click();
    button(changes, 'Continue').click();
    expect(backend.expectOne('/api/ui/profile/operations').request.body).toEqual({
      kind: 'ResetAuthenticator',
    });
  });

  it('shows the server refusal and rereads what the account can change', async () => {
    const { fixture, page, changes } = await render();
    button(page, 'Reset authenticator').click();
    await fixture.whenStable();
    button(changes, 'Continue').click();
    backend.expectOne('/api/ui/profile/operations').flush(
      {
        code: 'authenticator_not_enrolled',
        title: 'This needs an authenticator app set up first.',
      },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();
    backend.expectOne('/api/ui/profile').flush(profile);
    await fixture.whenStable();
    expect(page.querySelector('[role=alert]')?.textContent).toContain(
      'This needs an authenticator app set up first.',
    );
    expect(assign).not.toHaveBeenCalled();
  });

  it('keeps a wrong proof in the dialog and resets nothing', async () => {
    const { fixture, page, changes } = await render();
    button(page, 'Reset authenticator').click();
    await fixture.whenStable();
    button(changes, 'Continue').click();
    backend.expectOne('/api/ui/profile/operations').flush(resetOperation);
    await fixture.whenStable();
    fill(changes, 'credential-verification-password', 'Wrong-Password');
    fill(changes, 'credential-verification-code', '000000');
    await fixture.whenStable();
    button(changes, 'Verify account').click();
    backend.expectOne(verifyUrl).flush(
      {
        code: 'verification_failed',
        title: "That didn't verify your account. Check what you entered and try again.",
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();
    expect(changes.querySelector('app-verification-dialog dialog')?.textContent).toContain(
      "That didn't verify your account.",
    );
    expect(
      changes.querySelector<HTMLInputElement>('#credential-verification-password')?.value,
    ).toBe('');
    backend.expectNone('/api/ui/profile/authenticator/reset');
  });

  it('reports a lost reset reply as unknown without sending it again', async () => {
    const { fixture, page, changes } = await render();
    button(page, 'Reset authenticator').click();
    await fixture.whenStable();
    button(changes, 'Continue').click();
    backend.expectOne('/api/ui/profile/operations').flush(resetOperation);
    await fixture.whenStable();
    fill(changes, 'credential-verification-password', 'Current-Password');
    fill(changes, 'credential-verification-code', '123456');
    button(changes, 'Verify account').click();
    backend
      .expectOne(verifyUrl)
      .flush({ operationId: operation.operationId, commitByUtc: '2099-01-01T00:00:00Z' });
    backend.expectOne('/api/ui/profile/authenticator/reset').error(new ProgressEvent('error'));
    fixture.detectChanges();
    backend.expectNone('/api/ui/profile');
    await fixture.whenStable();
    expect(page.textContent).toContain('The change may have been saved.');
    backend.expectNone('/api/ui/profile/authenticator/reset');
    expect(assign).not.toHaveBeenCalled();
  });
});
