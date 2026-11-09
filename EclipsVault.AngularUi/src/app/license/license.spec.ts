import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import type { components } from '../ui-api/schema.generated';
import { LicensePage } from './license-page';

const missing: components['schemas']['LicenseResponse'] = {
  status: 'Missing',
  message: 'No license is configured — running unlicensed.',
  licence: null,
  updatesLapsed: false,
  beyondTier: [],
};
const perpetual: components['schemas']['LicenseDetailsResponse'] = {
  issuedTo: 'Example',
  tier: 'Max',
  contact: 'owner@example.test',
  licenseId: 'lic-example',
  issuedAtUtc: '2020-01-02T03:04:00Z',
  expiresAtUtc: null,
  updatesUntilUtc: '2025-01-02T03:04:00Z',
  maxNodes: 0,
};
let backend: HttpTestingController;
function render() {
  TestBed.configureTestingModule({
    providers: [...appConfig.providers, provideHttpClientTesting()],
  });
  backend = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(LicensePage);
  fixture.detectChanges();
  const page: HTMLElement = fixture.nativeElement;
  return { fixture, page };
}
async function loaded(response = missing) {
  const rendered = render();
  backend.expectOne('/api/ui/license').flush(response);
  await rendered.fixture.whenStable();
  return rendered;
}
function text(page: HTMLElement): string {
  return page.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}
afterEach(() => {
  backend.verify();
  TestBed.resetTestingModule();
});

it('announces that the licence is loading', () => {
  const { page } = render();
  expect(page.querySelector('[role="status"]')?.textContent).toBe('Loading license…');
  backend.expectOne('/api/ui/license').flush(missing);
});
it('retries a failed load and shows the returned status', async () => {
  const { fixture, page } = render();
  backend.expectOne('/api/ui/license').flush({}, { status: 503, statusText: 'Unavailable' });
  await fixture.whenStable();
  expect(page.querySelector('[role="alert"]')?.textContent).toBe(
    'Could not load license. Try again.',
  );
  page.querySelector('button')?.click();
  fixture.detectChanges();
  backend.expectOne('/api/ui/license').flush(missing);
  await fixture.whenStable();
  expect(text(page)).toContain('No license is configured — running unlicensed.');
});
it.each([
  { status: 'Valid', tone: 'ok' },
  { status: 'Expired', tone: 'warn' },
  { status: 'Missing', tone: 'muted' },
  { status: 'Malformed', tone: 'danger' },
  { status: 'InvalidSignature', tone: 'danger' },
] satisfies { status: components['schemas']['LicenseResponse']['status']; tone: string }[])(
  'shows $status with the $tone badge and the startup message',
  async ({ status, tone }) => {
    const { page } = await loaded({ ...missing, status, message: 'Startup licence message' });
    expect(page.querySelector('.badge')?.textContent).toBe(status);
    expect(page.querySelector('.badge')?.classList.contains(`tone-${tone}`)).toBe(true);
    expect(text(page)).toContain('Startup licence message');
  },
);
it('shows the unlicensed state without licence details', async () => {
  const { page } = await loaded();
  expect(text(page)).toContain('No license is configured — running unlicensed.');
  expect(page.querySelector('dl')).toBeNull();
  expect(page.querySelector('.notice')).toBeNull();
});
it('shows a perpetual licence with unlimited nodes and UTC dates', async () => {
  const { page } = await loaded({ ...missing, status: 'Valid', licence: perpetual });
  expect([...page.querySelectorAll('dd')].map((value) => value.textContent?.trim())).toEqual([
    'Example',
    'Max',
    'owner@example.test',
    'lic-example',
    '2020-01-02 03:04 UTC',
    'never — perpetual',
    '2025-01-02 03:04 UTC',
    'unlimited',
  ]);
});
it('shows an expiring licence with its node limit and no blank contact', async () => {
  const { page } = await loaded({
    ...missing,
    status: 'Expired',
    licence: {
      ...perpetual,
      contact: '  ',
      expiresAtUtc: '2024-02-03T04:05:00Z',
      updatesUntilUtc: null,
      maxNodes: 3,
    },
  });
  expect([...page.querySelectorAll('dd')].map((value) => value.textContent?.trim())).toEqual([
    'Example',
    'Max',
    'lic-example',
    '2020-01-02 03:04 UTC',
    '2024-02-03 04:05 UTC',
    '—',
    '3',
  ]);
});
it('copies the update-window notice word for word', async () => {
  const { page } = await loaded({
    ...missing,
    status: 'Valid',
    licence: perpetual,
    updatesLapsed: true,
  });
  expect(
    [...page.querySelectorAll('.notice')].map((notice) =>
      notice.textContent?.replace(/\s+/g, ' ').trim(),
    ),
  ).toEqual([
    'Your update window has ended. The vault keeps working and still receives security patches; renew to receive new feature releases.',
  ]);
});
it('copies the beyond-tier notice word for word', async () => {
  const { page } = await loaded({
    ...missing,
    status: 'Valid',
    licence: perpetual,
    beyondTier: ['sso', 'kms'],
  });
  expect(
    [...page.querySelectorAll('.notice')].map((notice) =>
      notice.textContent?.replace(/\s+/g, ' ').trim(),
    ),
  ).toEqual([
    'In use beyond your tier: sso, kms. These keep working — licensing is soft — but are not covered by your current tier.',
  ]);
});
it('explains the actual installation inputs', async () => {
  const { page } = await loaded();
  expect(text(page)).toContain(
    'Set the ECLIPSVAULT_LICENSE environment variable to your license token, or point License:FilePath at a file that holds the token, then restart. The token is verified offline against the vendor key pinned into this build — it never leaves your infrastructure.',
  );
});
it('keeps the Legacy commercial copy word for word', async () => {
  const { page } = await loaded();
  expect(text(page)).toContain(
    'How this vault is licensed. Licensing is soft — your secrets, and every baseline protection, are never gated.',
  );
  expect(text(page)).toContain(
    'See the Pricing & licensing section of the README to buy or renew.',
  );
});
