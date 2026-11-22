import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import type { components } from '../ui-api/schema.generated';
import { ActivityPage } from './activity-page';

const feed: components['schemas']['ActivityResponse'] = {
  page: 1,
  pageSize: 25,
  hasMore: false,
  hasPrevious: false,
  notableCount: 2,
  items: [
    {
      timestampUtc: '2026-11-13T11:59:30Z',
      category: 'Security',
      title: 'A session was force-revoked',
      severity: 'Critical',
      resourceName: null,
      sourceIp: '203.0.113.8',
    },
    {
      timestampUtc: '2026-11-13T11:00:00Z',
      category: 'Secrets',
      title: 'Revealed a secret value',
      severity: 'Notable',
      resourceName: 'PAYMENTS_KEY',
      sourceIp: '203.0.113.7',
    },
    {
      timestampUtc: '2026-11-13T10:00:00Z',
      category: 'Authentication',
      title: 'Signed in',
      severity: 'Routine',
      resourceName: null,
      sourceIp: '203.0.113.7',
    },
  ],
};
let backend: HttpTestingController;
async function loaded() {
  TestBed.configureTestingModule({
    providers: [...appConfig.providers, provideHttpClientTesting()],
  });
  backend = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(ActivityPage);
  fixture.detectChanges();
  backend.expectOne('/api/ui/activity').flush(feed);
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}
/** The text of each row's cell under the column headed `column`. */
function column(page: HTMLElement, heading: string): string[] {
  const index = [...page.querySelectorAll('th')].findIndex((cell) => cell.textContent === heading);
  return [...page.querySelectorAll('tbody tr')].map(
    (row) => row.children[index]?.textContent?.trim() ?? '',
  );
}
afterEach(() => {
  backend.verify();
  TestBed.resetTestingModule();
});

it('names a notable or critical row’s severity in words, apart from its kind', async () => {
  const page = await loaded();
  expect(column(page, 'Severity')).toEqual(['Critical', 'Notable', '']);
  expect(column(page, 'Kind')).toEqual(['Security', 'Secrets', 'Sign-in']);
});
