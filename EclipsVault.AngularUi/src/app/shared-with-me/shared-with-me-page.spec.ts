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
    expect(page.querySelector('.empty-state a')?.textContent).toBe('Go to your secrets');
    expect(page.querySelector('.empty-state a')?.getAttribute('href')).toBe('/Secrets');
    expect(
      page
        .querySelector('nav[aria-label="Secrets views"] a[aria-current="page"]')
        ?.textContent?.trim(),
    ).toBe('Shared with me');
  });

  it('lists the soonest-ending share first, its close expiry as a yellow pill', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-11-03T09:30:00Z'));
    try {
      const { fixture, page } = render();
      backend.expectOne('/api/ui/shared-with-me').flush({ shares: [withoutExpiry, share] });
      await fixture.whenStable();
      expect(page.querySelector('h1')?.textContent).toBe('Shared with me');
      expect(page.querySelector('.page-sub')?.textContent).toBe(
        '2 secrets shared with you · soonest ends 7 Nov.',
      );
      expect(page.querySelector('.trust-line')?.textContent?.trim()).toBe('Every reveal is logged');
      expect(page.querySelector('.sort-note')?.textContent).toBe(
        'Sorted by end date, soonest first',
      );
      expect([...page.querySelectorAll('th')].map((cell) => cell.textContent?.trim())).toEqual([
        'Secret',
        'Environment',
        'Sensitivity',
        'Shared by',
        'Access until',
      ]);
      // The project code is a block under the secret's name, with no space between them.
      expect(
        [...page.querySelectorAll('tbody tr')].map((row) =>
          [...row.querySelectorAll('td')].map((cell) => cell.textContent?.trim()),
        ),
      ).toEqual([
        ['Billing_Db_PasswordBILLING', 'Production', 'Secret', 'colleague', 'In 4 days · 7 Nov'],
        ['Payments_Api_KeyPAYMENTS', 'Development', 'Internal', 'another-colleague', 'No expiry'],
      ]);
      // Exact text, untrimmed: the browser tests match the cell's whole text.
      expect(
        [...page.querySelectorAll('tbody td.cell-meta')].map((cell) => cell.textContent),
      ).toEqual(['colleague', 'another-colleague']);
      const pill = page.querySelector('tbody .badge.tone-warn')!;
      expect(pill.textContent).toBe('In 4 days · 7 Nov');
      expect(pill.getAttribute('title')).toBe('2026-11-07 09:30:00 UTC');
    } finally {
      vi.useRealTimers();
    }
  });

  it('says when no share ends', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/shared-with-me').flush({ shares: [withoutExpiry] });
    await fixture.whenStable();
    expect(page.querySelector('.page-sub')?.textContent).toBe(
      '1 secret shared with you · none of them end.',
    );
  });

  it('opens each shared secret on its details page', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/shared-with-me').flush({ shares: [share, withoutExpiry] });
    await fixture.whenStable();
    expect(
      [...page.querySelectorAll('tbody a')].map((link) => [
        link.textContent,
        link.getAttribute('href'),
      ]),
    ).toEqual([
      ['Billing_Db_Password', '/Secrets/Details/11111111-1111-1111-1111-111111111111'],
      ['Payments_Api_Key', '/Secrets/Details/22222222-2222-2222-2222-222222222222'],
    ]);
  });
});
