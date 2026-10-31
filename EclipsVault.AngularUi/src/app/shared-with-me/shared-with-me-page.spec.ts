import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import type { components } from '../ui-api/schema.generated';
import { SharedWithMePage } from './shared-with-me-page';

const share: components['schemas']['ReceivedShareResponse'] = {
  secretId: '11111111-1111-1111-1111-111111111111',
  name: 'Billing_Db_Password',
  project: 'BILLING',
  environment: 'Production',
  sensitivity: 3,
  sensitivityLabel: 'Secret',
  sharedBy: 'colleague',
  accessUntilUtc: '2026-11-07T09:30:00Z',
};
const withoutExpiry: components['schemas']['ReceivedShareResponse'] = {
  secretId: '22222222-2222-2222-2222-222222222222',
  name: 'Payments_Api_Key',
  project: 'PAYMENTS',
  environment: 'Development',
  sensitivity: 1,
  sensitivityLabel: 'Internal',
  sharedBy: 'another-colleague',
  accessUntilUtc: null,
};

describe('SharedWithMePage', () => {
  let backend: HttpTestingController;

  function render() {
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(SharedWithMePage);
    fixture.detectChanges();
    return { fixture, page: fixture.nativeElement as HTMLElement };
  }

  afterEach(() => backend.verify());

  it('shows loading until the server answers', () => {
    const { page } = render();
    expect(page.querySelector('[role="status"]')?.textContent).toBe(
      'Loading the secrets shared with you…',
    );
    backend.expectOne('/api/ui/shared-with-me').flush({ shares: [] });
  });

  it('retries a failed fetch in place', async () => {
    const { fixture, page } = render();
    backend
      .expectOne('/api/ui/shared-with-me')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(page.querySelector('[role="alert"]')?.textContent).toBe(
      'Could not load the secrets shared with you. Try again.',
    );
    page.querySelector('button')!.click();
    fixture.detectChanges();
    backend.expectOne('/api/ui/shared-with-me').flush({ shares: [share] });
    await fixture.whenStable();
    expect(page.querySelectorAll('tbody tr')).toHaveLength(1);
    expect(page.querySelector('[role="alert"]')).toBeNull();
  });

  it('explains the empty state and links to your secrets', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/shared-with-me').flush({ shares: [] });
    await fixture.whenStable();
    expect(page.querySelector('h3')?.textContent).toBe('Nothing shared with you yet');
    expect(page.querySelector('.empty-state p')?.textContent).toBe(
      "This is where secrets from other projects show up when a colleague grants you access to them. There's nothing for you to do here until someone shares one.",
    );
    expect(page.querySelector('table')).toBeNull();
    expect(page.querySelector('a')?.textContent).toBe('Go to your secrets');
    expect(page.querySelector('a')?.getAttribute('href')).toBe('/Secrets');
  });

  it('lists each shared secret under the Legacy columns with UTC dates and no expiry', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/shared-with-me').flush({ shares: [share, withoutExpiry] });
    await fixture.whenStable();
    expect(page.querySelector('h1')?.textContent).toBe('Shared with me');
    expect(page.querySelector('.page-sub')?.textContent).toBe(
      'Secrets outside your project that someone has granted you access to.',
    );
    expect([...page.querySelectorAll('th')].map((cell) => cell.textContent?.trim())).toEqual([
      'Name',
      'Project',
      'Environment',
      'Sensitivity',
      'Shared by',
      'Access until',
      '',
    ]);
    expect(
      [...page.querySelectorAll('tbody tr')].map((row) =>
        [...row.querySelectorAll('td')].map((cell) => cell.textContent?.trim()),
      ),
    ).toEqual([
      [
        'Billing_Db_Password',
        'BILLING',
        'Production',
        'Secret',
        'colleague',
        '2026-11-07 09:30',
        'Open',
      ],
      [
        'Payments_Api_Key',
        'PAYMENTS',
        'Development',
        'Internal',
        'another-colleague',
        'no expiry',
        'Open',
      ],
    ]);
  });

  it('opens each shared secret on its Legacy details page', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/shared-with-me').flush({ shares: [share, withoutExpiry] });
    await fixture.whenStable();
    expect(
      [...page.querySelectorAll('tbody a')].map((link) => [
        link.textContent,
        link.getAttribute('href'),
      ]),
    ).toEqual([
      ['Open', '/Secrets/Details/11111111-1111-1111-1111-111111111111'],
      ['Open', '/Secrets/Details/22222222-2222-2222-2222-222222222222'],
    ]);
  });
});
