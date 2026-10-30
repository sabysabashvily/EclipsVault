import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { ProfilePage } from './profile-page';
import { button, dialogs, fill, operation, profile, refreshed } from './profile-test';

describe('ProfilePage', () => {
  let backend: HttpTestingController;
  function render() {
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ProfilePage);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    dialogs(page);
    backend.expectOne('/api/ui/me').flush({});
    return { fixture, page };
  }
  afterEach(() => backend.verify({ ignoreCancelled: true }));
  it('loads the cards in Legacy order', async () => {
    const { fixture, page } = render();
    expect(page.textContent).toContain('Loading your profile…');
    backend.expectOne('/api/ui/profile').flush(profile);
    await fixture.whenStable();
    expect(
      [...page.querySelectorAll('section > .panel-header > h2')].map(
        (heading) => heading.textContent,
      ),
    ).toEqual(['Profile picture', 'Details', 'Access', 'Security', 'Passkeys']);
  });
  it('keeps username and access labels immutable', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/profile').flush(profile);
    await fixture.whenStable();
    expect(page.querySelector<HTMLInputElement>('#profile-username')?.disabled).toBe(true);
    expect(page.textContent).toContain('GLOBAL');
  });
  it('advises changing a password on suspected compromise', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/profile').flush(profile);
    await fixture.whenStable();
    expect(page.textContent).toContain('Change it if you suspect it has been compromised.');
  });
  it('offers no Remove action for a generated avatar', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/profile').flush(profile);
    await fixture.whenStable();
    expect(button(page, 'Remove')).toBeUndefined();
  });
  it('retries only the snapshot after a failed fetch', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/profile').flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    button(page, 'Try again').click();
    fixture.detectChanges();
    backend.expectOne('/api/ui/profile').flush(profile);
    await fixture.whenStable();
    expect(page.textContent).toContain('Details');
    backend.expectNone((request) => request.method !== 'GET');
  });
  it('saves only the display name and refreshes shared account state', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/profile').flush(profile);
    await fixture.whenStable();
    fill(page, 'display-name', 'Renamed');
    button(page, 'Save changes').click();
    button(page, 'Save changes').click();
    const save = backend.expectOne({ method: 'PATCH', url: '/api/ui/profile/details' });
    expect(save.request.body).toEqual({ displayName: 'Renamed' });
    save.flush({ displayName: 'Renamed' });
    fixture.detectChanges();
    refreshed(backend, { ...profile, displayName: 'Renamed' });
    await fixture.whenStable();
    expect(page.textContent).toContain('Your profile was updated.');
  });
  it('requires destructive confirmation before removing the custom avatar', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/profile').flush({ ...profile, hasCustomAvatar: true });
    await fixture.whenStable();
    button(page, 'Remove').click();
    await fixture.whenStable();
    expect(page.querySelector('dialog[role=alertdialog]')?.textContent).toContain(
      'go back to the generated avatar?',
    );
    button(page, 'Continue').click();
    backend.expectOne({ method: 'DELETE', url: '/api/ui/profile/avatar' }).flush(null);
    fixture.detectChanges();
    refreshed(backend);
    await fixture.whenStable();
    expect(button(page, 'Remove')).toBeUndefined();
  });
  it('keeps email read-only when SMTP is unavailable and explains why', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/profile').flush({
      ...profile,
      changes: {
        ...profile.changes,
        changeEmail: {
          available: false,
          unavailableReason: 'mail_unavailable',
          explanation: 'SMTP delivery is unavailable.',
        },
      },
    });
    await fixture.whenStable();
    expect(page.querySelector<HTMLInputElement>('#active-email')?.readOnly).toBe(true);
    expect(page.textContent).toContain('SMTP delivery is unavailable.');
    expect(page.querySelector('#new-email')).toBeNull();
  });
  it('requests a frozen mailbox only after verification and keeps the active email unchanged', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/profile').flush(profile);
    await fixture.whenStable();
    fill(page, 'new-email', 'new@example.test');
    button(page, 'Change email').click();
    backend.expectOne('/api/ui/profile/operations').flush({ ...operation, kind: 'ChangeEmail' });
    await fixture.whenStable();
    fill(page, 'verification-password', 'Current-Password');
    fill(page, 'verification-code', '123456');
    button(page, 'Verify account').click();
    backend
      .expectOne(`/api/ui/profile/operations/${operation.operationId}/verify`)
      .flush({ operationId: operation.operationId, commitByUtc: '2099-01-01T00:00:00Z' });
    const pending = {
      operationId: operation.operationId,
      address: 'new@example.test',
      expiresAtUtc: '2099-01-01T00:00:00Z',
    };
    backend.expectOne('/api/ui/profile/email/request').flush(pending);
    fixture.detectChanges();
    backend.expectOne('/api/ui/profile').flush({ ...profile, pendingEmail: pending });
    await fixture.whenStable();
    expect(page.querySelector<HTMLInputElement>('#active-email')?.value).toBe(
      'morgan@example.test',
    );
    expect(page.textContent).toContain('Pending email: new@example.test');
  });
  it('shows response loss truthfully without automatically replaying a write', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/profile').flush(profile);
    await fixture.whenStable();
    button(page, 'Save changes').click();
    backend.expectOne('/api/ui/profile/details').error(new ProgressEvent('error'));
    await fixture.whenStable();
    expect(page.textContent).toContain('The change may have been saved.');
    backend.expectNone('/api/ui/profile/details');
  });
  it.each(['Remove', 'Sign out everywhere'])(
    'Cancel restores the enabled %s opener',
    async (openerText) => {
      const { fixture, page } = render();
      backend.expectOne('/api/ui/profile').flush({ ...profile, hasCustomAvatar: true });
      await fixture.whenStable();
      const opener = button(page, openerText);
      opener.focus();
      opener.click();
      await fixture.whenStable();
      button(page.querySelector('app-confirmation-dialog')!, 'Cancel').click();
      await fixture.whenStable();
      expect(opener.disabled).toBe(false);
      expect(document.activeElement).toBe(opener);
      backend.expectNone((request) => request.method !== 'GET');
    },
  );
  it.each(['Remove', 'Sign out everywhere'])(
    'Escape restores the enabled %s opener',
    async (openerText) => {
      const { fixture, page } = render();
      backend.expectOne('/api/ui/profile').flush({ ...profile, hasCustomAvatar: true });
      await fixture.whenStable();
      const opener = button(page, openerText);
      opener.focus();
      opener.click();
      await fixture.whenStable();
      page
        .querySelector('app-confirmation-dialog dialog')!
        .dispatchEvent(new Event('cancel', { cancelable: true }));
      await fixture.whenStable();
      expect(opener.disabled).toBe(false);
      expect(document.activeElement).toBe(opener);
      backend.expectNone((request) => request.method !== 'GET');
    },
  );
  it('refreshes the snapshot after a mailbox invalidation and offers a fresh email change', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/profile').flush({
      ...profile,
      pendingEmail: {
        operationId: operation.operationId,
        address: 'pending@example.test',
        expiresAtUtc: operation.expiresAtUtc,
      },
    });
    await fixture.whenStable();
    fill(page, 'mailbox-code', 'last-wrong-code');
    button(page, 'Confirm email').click();
    backend
      .expectOne('/api/ui/profile/email/confirm')
      .flush(
        { code: 'email_code_invalid', title: 'That mailbox code is not valid.' },
        { status: 400, statusText: 'Bad Request' },
      );
    fixture.detectChanges();
    backend.expectOne('/api/ui/profile').flush(profile);
    await fixture.whenStable();
    expect(page.querySelector('#mailbox-code')).toBeNull();
    expect(button(page, 'Change email')).toBeDefined();
    expect(page.textContent).toContain('That mailbox code is not valid.');
    backend.expectNone('/api/ui/profile/email/confirm');
  });
});
