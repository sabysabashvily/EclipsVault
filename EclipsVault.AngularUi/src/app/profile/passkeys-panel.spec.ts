import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { PasskeyCreation } from './passkey-creation';
import { ProfilePage } from './profile-page';
import { button, dialogs, fill, operation, profile, refreshed } from './profile-test';
import type { Profile } from './profile-types';

describe('ProfilePage passkeys', () => {
  let backend: HttpTestingController;
  const creation = { supported: true, create: vi.fn() };
  const laptop = {
    id: '22222222-2222-2222-2222-222222222222',
    nickname: 'Work laptop',
    createdAtUtc: '2026-03-01T23:30:00Z',
    removal: { available: true, unavailableReason: null },
  };
  const withLaptop: Profile = { ...profile, passkeys: [laptop] };
  const verifyUrl = `/api/ui/profile/operations/${operation.operationId}/verify`;

  async function render(account = withLaptop) {
    TestBed.configureTestingModule({
      providers: [
        ...appConfig.providers,
        provideHttpClientTesting(),
        { provide: PasskeyCreation, useValue: creation },
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
    return {
      fixture,
      page,
      panel: page.querySelector<HTMLElement>('app-passkeys-panel')!,
      changes: page.querySelector<HTMLElement>('app-credential-change')!,
    };
  }

  async function verify(fixture: { whenStable(): Promise<void> }, changes: HTMLElement) {
    await fixture.whenStable();
    fill(changes, 'credential-verification-password', 'Current-Password');
    fill(changes, 'credential-verification-code', '123456');
    button(changes, 'Verify account').click();
    backend
      .expectOne(verifyUrl)
      .flush({ operationId: operation.operationId, commitByUtc: '2099-01-01T00:00:00Z' });
  }

  afterEach(() => {
    backend.verify({ ignoreCancelled: true });
    creation.create.mockReset();
    creation.supported = true;
  });

  it('lists each passkey by name with its creation date in UTC', async () => {
    const { panel } = await render();
    expect(panel.querySelector('.settings-row strong')?.textContent).toBe('Work laptop');
    expect(panel.querySelector('.settings-row .muted')?.textContent).toBe('Added 1 Mar 2026 UTC');
  });

  it('offers no way to rename a passkey', async () => {
    const { panel } = await render();
    expect(
      [...panel.querySelectorAll('button')].map((control) => control.textContent?.trim()),
    ).toEqual(['Remove', 'Add a passkey']);
  });

  it('adds a passkey under the name frozen before verification', async () => {
    const { fixture, page, panel, changes } = await render();
    creation.create.mockResolvedValue({
      id: 'new-credential',
      clientDataJSON: 'client',
      attestationObject: 'attestation',
    });
    fill(panel, 'passkey-nickname', 'Phone');
    button(panel, 'Add a passkey').click();
    const prepare = backend.expectOne({ method: 'POST', url: '/api/ui/profile/operations' });
    expect(prepare.request.body).toEqual({ kind: 'AddPasskey', passkeyNickname: 'Phone' });
    prepare.flush({ ...operation, kind: 'AddPasskey' });
    await fixture.whenStable();
    expect(changes.textContent).toContain(
      'Add the passkey “Phone”. Any other signed-in devices will be signed out.',
    );
    await verify(fixture, changes);
    const begin = backend.expectOne('/api/ui/profile/passkeys/registration/begin');
    expect(begin.request.body).toEqual({ operationId: operation.operationId });
    begin.flush({ publicKey: { challenge: 'server-challenge' } });
    await fixture.whenStable();
    expect(creation.create).toHaveBeenCalledExactlyOnceWith(
      { challenge: 'server-challenge' },
      expect.any(AbortSignal),
    );
    const complete = backend.expectOne('/api/ui/profile/passkeys/registration/complete');
    expect(complete.request.body).toEqual({
      operationId: operation.operationId,
      credential: {
        id: 'new-credential',
        clientDataJSON: 'client',
        attestationObject: 'attestation',
      },
    });
    complete.flush({ id: 'new-id', nickname: 'Phone', createdAtUtc: '2026-10-28T10:00:00Z' });
    fixture.detectChanges();
    refreshed(backend);
    await fixture.whenStable();
    expect(page.querySelector('[role=status]')?.textContent).toContain(
      'Passkey added. You can now sign in with it.',
    );
    expect(panel.querySelector<HTMLInputElement>('#passkey-nickname')?.value).toBe('');
  });

  it('asks the person to follow the browser prompt while it is open', async () => {
    const { fixture, panel, changes } = await render();
    creation.create.mockReturnValue(new Promise(() => undefined));
    button(panel, 'Add a passkey').click();
    backend.expectOne('/api/ui/profile/operations').flush({ ...operation, kind: 'AddPasskey' });
    await verify(fixture, changes);
    backend
      .expectOne('/api/ui/profile/passkeys/registration/begin')
      .flush({ publicKey: { challenge: 'server-challenge' } });
    await fixture.whenStable();
    expect(panel.querySelector('.passkey-status')?.textContent?.trim()).toBe(
      'Follow the prompt on your device…',
    );
  });

  it('reports a dismissed browser prompt as cancellation and registers nothing', async () => {
    const { fixture, page, panel, changes } = await render();
    creation.create.mockRejectedValue(new DOMException('Dismissed', 'NotAllowedError'));
    button(panel, 'Add a passkey').click();
    backend.expectOne('/api/ui/profile/operations').flush({ ...operation, kind: 'AddPasskey' });
    await verify(fixture, changes);
    backend
      .expectOne('/api/ui/profile/passkeys/registration/begin')
      .flush({ publicKey: { challenge: 'server-challenge' } });
    // The browser's rejection settles outside Angular; the refusal then rereads the profile.
    await new Promise((settled) => setTimeout(settled));
    fixture.detectChanges();
    backend.expectOne('/api/ui/profile').flush(withLaptop);
    await fixture.whenStable();
    expect(page.querySelector('[role=alert]')?.textContent).toContain(
      'The passkey prompt was dismissed or timed out.',
    );
    expect(panel.querySelector('.passkey-status')?.textContent?.trim()).toBe('');
    backend.expectNone('/api/ui/profile/passkeys/registration/complete');
  });

  it.each([
    ['pagehide', window],
    ['vault-session-ended', document],
  ])('ignores a late browser credential after %s', async (event, target) => {
    const { fixture, panel, changes } = await render();
    let resolveCredential!: (credential: unknown) => void;
    creation.create.mockReturnValue(
      new Promise((resolve) => {
        resolveCredential = resolve;
      }),
    );
    button(panel, 'Add a passkey').click();
    backend.expectOne('/api/ui/profile/operations').flush({ ...operation, kind: 'AddPasskey' });
    await verify(fixture, changes);
    backend
      .expectOne('/api/ui/profile/passkeys/registration/begin')
      .flush({ publicKey: { challenge: 'server-challenge' } });
    await fixture.whenStable();
    const prompt: AbortSignal = creation.create.mock.calls[0][1];
    target.dispatchEvent(new Event(event));
    expect(prompt.aborted).toBe(true);
    resolveCredential({ id: 'late-credential' });
    await fixture.whenStable();
    backend.expectNone('/api/ui/profile/passkeys/registration/complete');
  });

  it('shows server ceremony expiry without repeating registration', async () => {
    const { fixture, page, panel, changes } = await render();
    creation.create.mockResolvedValue({ id: 'expired-credential' });
    button(panel, 'Add a passkey').click();
    backend.expectOne('/api/ui/profile/operations').flush({ ...operation, kind: 'AddPasskey' });
    await verify(fixture, changes);
    backend.expectOne('/api/ui/profile/passkeys/registration/begin').flush({ publicKey: {} });
    await fixture.whenStable();
    backend
      .expectOne('/api/ui/profile/passkeys/registration/complete')
      .flush(
        { code: 'ceremony_expired', title: 'The passkey prompt expired. Start again.' },
        { status: 409, statusText: 'Conflict' },
      );
    fixture.detectChanges();
    backend.expectOne('/api/ui/profile').flush(withLaptop);
    await fixture.whenStable();
    expect(page.querySelector('[role=alert]')?.textContent).toContain(
      'The passkey prompt expired. Start again.',
    );
    backend.expectNone('/api/ui/profile/passkeys/registration/begin');
    backend.expectNone('/api/ui/profile/passkeys/registration/complete');
  });

  it('prepares only one addition while account proof is pending', async () => {
    const { fixture, panel, changes } = await render();
    button(panel, 'Add a passkey').click();
    button(panel, 'Add a passkey').click();
    backend.expectOne('/api/ui/profile/operations').flush({ ...operation, kind: 'AddPasskey' });
    await fixture.whenStable();
    button(panel, 'Add a passkey').click();
    backend.expectNone('/api/ui/profile/operations');
    button(changes.querySelector('app-verification-dialog')!, 'Cancel').click();
    backend.expectOne(`/api/ui/profile/operations/${operation.operationId}`).flush(null);
  });

  it('deletes a passkey once when verification is submitted twice', async () => {
    const { fixture, panel, changes } = await render();
    button(panel, 'Remove').click();
    button(panel, 'Remove').click();
    await fixture.whenStable();
    button(changes, 'Continue').click();
    backend.expectOne('/api/ui/profile/operations').flush({ ...operation, kind: 'RemovePasskey' });
    await fixture.whenStable();
    fill(changes, 'credential-verification-password', 'Current-Password');
    fill(changes, 'credential-verification-code', '123456');
    button(changes, 'Verify account').click();
    button(changes, 'Verify account').click();
    backend
      .expectOne(verifyUrl)
      .flush({ operationId: operation.operationId, commitByUtc: '2099-01-01T00:00:00Z' });
    backend.expectOne(`/api/ui/profile/passkeys/${laptop.id}`).flush(null);
    fixture.detectChanges();
    refreshed(backend, profile);
    await fixture.whenStable();
    backend.expectNone(`/api/ui/profile/passkeys/${laptop.id}`);
  });

  it('disables Add a passkey in a browser without passkeys and says why', async () => {
    creation.supported = false;
    const { panel } = await render();
    const add = button(panel, 'Add a passkey');
    expect(add.disabled).toBe(true);
    expect(add.title).toBe('This browser does not support passkeys.');
  });

  it('removes a passkey after confirmation and verification', async () => {
    const { fixture, page, panel, changes } = await render();
    button(panel, 'Remove').click();
    await fixture.whenStable();
    expect(changes.querySelector('dialog[role=alertdialog]')?.textContent).toContain(
      'Remove this passkey? You will no longer be able to sign in with it.',
    );
    button(changes, 'Continue').click();
    const prepare = backend.expectOne('/api/ui/profile/operations');
    expect(prepare.request.body).toEqual({ kind: 'RemovePasskey', passkeyId: laptop.id });
    prepare.flush({ ...operation, kind: 'RemovePasskey' });
    await fixture.whenStable();
    expect(changes.textContent).toContain(
      'Remove the passkey “Work laptop”. Any other signed-in devices will be signed out.',
    );
    await verify(fixture, changes);
    const removal = backend.expectOne({
      method: 'DELETE',
      url: `/api/ui/profile/passkeys/${laptop.id}`,
    });
    expect(removal.request.body).toEqual({ operationId: operation.operationId });
    removal.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    refreshed(backend, { ...profile, passkeys: [] });
    await fixture.whenStable();
    expect(page.querySelector('[role=status]')?.textContent).toContain(
      'Passkey removed. Any other signed-in devices have been signed out.',
    );
    expect(panel.textContent).toContain('No passkeys registered yet.');
  });

  it('explains why the last passkey cannot be removed without another proven method', async () => {
    const { panel } = await render({
      ...profile,
      passkeys: [
        {
          ...laptop,
          removal: { available: false, unavailableReason: 'alternate_method_required' },
        },
      ],
    });
    expect(button(panel, 'Remove').disabled).toBe(true);
    expect(panel.textContent).toContain(
      'Another usable sign-in method must remain after this change.',
    );
  });

  it('shows the server refusal of a stale last-key removal and rereads the passkeys', async () => {
    const { fixture, page, panel, changes } = await render();
    button(panel, 'Remove').click();
    await fixture.whenStable();
    button(changes, 'Continue').click();
    backend.expectOne('/api/ui/profile/operations').flush({ ...operation, kind: 'RemovePasskey' });
    await verify(fixture, changes);
    backend.expectOne(`/api/ui/profile/passkeys/${laptop.id}`).flush(
      {
        code: 'alternate_method_required',
        title:
          'Removing your last passkey needs another way to sign in that keeps working afterwards.',
      },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();
    backend.expectOne('/api/ui/profile').flush({
      ...profile,
      passkeys: [
        {
          ...laptop,
          removal: { available: false, unavailableReason: 'alternate_method_required' },
        },
      ],
    });
    await fixture.whenStable();
    expect(page.querySelector('[role=alert]')?.textContent).toContain(
      'Removing your last passkey needs another way to sign in that keeps working afterwards.',
    );
    expect(button(panel, 'Remove').disabled).toBe(true);
  });

  it('asks to sign in again when a saved passkey could not keep this browser signed in', async () => {
    const { fixture, page, panel, changes } = await render();
    creation.create.mockResolvedValue({ id: 'new-credential' });
    button(panel, 'Add a passkey').click();
    backend.expectOne('/api/ui/profile/operations').flush({ ...operation, kind: 'AddPasskey' });
    await verify(fixture, changes);
    backend
      .expectOne('/api/ui/profile/passkeys/registration/begin')
      .flush({ publicKey: { challenge: 'server-challenge' } });
    await fixture.whenStable();
    backend.expectOne('/api/ui/profile/passkeys/registration/complete').flush(
      {
        code: 'changed_sign_in_required',
        title:
          'The change was saved, but this browser could not stay signed in. Sign in again to continue.',
      },
      { status: 503, statusText: 'Service Unavailable' },
    );
    await fixture.whenStable();
    expect(page.querySelector('[role=alert]')?.textContent).toContain(
      'The change was saved, but this browser could not stay signed in.',
    );
    expect(button(panel, 'Add a passkey').disabled).toBe(true);
    backend.expectNone('/api/ui/profile/passkeys/registration/complete');
  });
});
