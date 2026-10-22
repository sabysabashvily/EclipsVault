import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import type { components } from '../ui-api/schema.generated';
import { SharedByMePage } from './shared-by-me-page';

const share: components['schemas']['IssuedShareResponse'] = {
  shareId: '11111111-1111-1111-1111-111111111111',
  secretName: 'Deployment_key',
  recipient: 'colleague',
  createdAtUtc: '2026-10-20T10:05:00Z',
  expiresAtUtc: null,
};
const expiring: components['schemas']['IssuedShareResponse'] = {
  shareId: '22222222-2222-2222-2222-222222222222',
  secretName: 'Expiring_key',
  recipient: 'another-colleague',
  createdAtUtc: '2026-10-19T09:00:00Z',
  expiresAtUtc: '2026-10-26T09:00:00Z',
};

describe('SharedByMePage', () => {
  let backend: HttpTestingController;

  function render() {
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(SharedByMePage);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    const dialog = page.querySelector('dialog')!;
    dialog.showModal = () => dialog.setAttribute('open', '');
    dialog.close = () => dialog.removeAttribute('open');
    return { fixture, page };
  }

  function button(page: HTMLElement, text: string): HTMLButtonElement {
    return [...page.querySelectorAll('button')].find(
      (candidate) => candidate.textContent?.trim() === text,
    )!;
  }

  afterEach(() => {
    document.cookie = 'XSRF-TOKEN=; path=/; max-age=0';
    backend.verify();
  });

  it('shows loading until the server answers', () => {
    const { page } = render();
    expect(page.textContent).toContain('Loading your shares…');
    backend.expectOne('/api/ui/shared-by-me').flush({ shares: [] });
  });

  it('retries a failed fetch in place', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/shared-by-me').flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(page.textContent).toContain('Could not load your shares. Try again.');
    button(page, 'Try again').click();
    fixture.detectChanges();
    backend.expectOne('/api/ui/shared-by-me').flush({ shares: [share] });
    await fixture.whenStable();
    expect(page.querySelectorAll('tbody tr')).toHaveLength(1);
    expect(page.textContent).not.toContain('Could not load');
  });

  it('explains the empty state and links to your secrets', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/shared-by-me').flush({ shares: [] });
    await fixture.whenStable();
    expect(page.textContent).toContain("You haven't shared anything");
    expect(page.textContent).toContain(
      "When you grant a colleague access to one of your secrets, it shows up here so you can keep track of who can see what — and revoke access when it's no longer needed.",
    );
    expect(page.querySelector('a')?.textContent).toBe('Go to your secrets');
    expect(page.querySelector('a')?.getAttribute('href')).toBe('/Secrets');
  });

  it('shows each secret and recipient with UTC dates and no expiry where appropriate', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/shared-by-me').flush({ shares: [share, expiring] });
    await fixture.whenStable();
    expect([...page.querySelectorAll('th')].map((cell) => cell.textContent?.trim())).toEqual([
      'Secret',
      'Shared with',
      'Shared on',
      'Access until',
      '',
    ]);
    expect(
      [...page.querySelectorAll('tbody tr')].map((row) =>
        [...row.querySelectorAll('td')].map((cell) => cell.textContent?.trim()),
      ),
    ).toEqual([
      ['Deployment_key', 'colleague', '2026-10-20 10:05', 'no expiry', 'Revoke'],
      ['Expiring_key', 'another-colleague', '2026-10-19 09:00', '2026-10-26 09:00', 'Revoke'],
    ]);
  });

  it('names the recipient and secret in the confirmation and Cancel sends no request', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/shared-by-me').flush({ shares: [share] });
    await fixture.whenStable();
    button(page, 'Revoke').click();
    await fixture.whenStable();
    expect(page.querySelector('dialog p')?.textContent).toBe(
      "Revoke colleague's access to 'Deployment_key'? They lose access on their next request.",
    );
    button(page, 'Cancel').click();
    await fixture.whenStable();
    backend.expectNone({ method: 'DELETE', url: `/api/ui/shared-by-me/${share.shareId}` });
    expect(page.querySelectorAll('tbody tr')).toHaveLength(1);
  });

  it('confirms revocation with antiforgery and announces success before refreshing', async () => {
    const { fixture, page } = render();
    document.cookie = 'XSRF-TOKEN=shares-request-token; path=/';
    backend.expectOne('/api/ui/shared-by-me').flush({ shares: [share] });
    await fixture.whenStable();
    button(page, 'Revoke').click();
    await fixture.whenStable();
    button(page, 'Continue').click();
    fixture.detectChanges();
    const revoke = backend.expectOne({
      method: 'DELETE',
      url: `/api/ui/shared-by-me/${share.shareId}`,
    });
    expect(revoke.request.headers.get('RequestVerificationToken')).toBe('shares-request-token');
    expect(button(page, 'Revoke').disabled).toBe(true);
    revoke.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    backend.expectOne('/api/ui/shared-by-me').flush({ shares: [] });
    await fixture.whenStable();
    expect(page.querySelector('[role="status"]')?.textContent?.trim()).toBe('Access revoked.');
    expect(page.textContent).toContain("You haven't shared anything");
  });

  async function refuseRevocation(status: number, code: string, title: string) {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/shared-by-me').flush({ shares: [share] });
    await fixture.whenStable();
    button(page, 'Revoke').click();
    await fixture.whenStable();
    button(page, 'Continue').click();
    backend
      .expectOne(`/api/ui/shared-by-me/${share.shareId}`)
      .flush({ code, title }, { status, statusText: 'Refused' });
    return { fixture, page };
  }

  it('announces a share that is already gone and refreshes the list', async () => {
    const title = "That grant no longer exists, or it wasn't yours to revoke.";
    const { fixture, page } = await refuseRevocation(404, 'share_not_found', title);
    fixture.detectChanges();
    backend.expectOne('/api/ui/shared-by-me').flush({ shares: [] });
    await fixture.whenStable();
    expect(page.querySelector('[role="alert"]')?.textContent?.trim()).toBe(title);
    expect(page.textContent).toContain("You haven't shared anything");
  });

  it('announces a revocation the audit trail refused and keeps the share', async () => {
    const title = 'The audit trail is unavailable, so the vault refused the request.';
    const { fixture, page } = await refuseRevocation(503, 'audit_unavailable', title);
    await fixture.whenStable();
    expect(page.querySelector('[role="alert"]')?.textContent?.trim()).toBe(title);
    expect(button(page, 'Revoke').disabled).toBe(false);
    expect(page.querySelectorAll('tbody tr')).toHaveLength(1);
    expect(page.querySelector('[role="status"]')?.textContent?.trim()).toBe('');
  });
});
