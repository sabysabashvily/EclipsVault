import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import type { Me } from '../account/me';
import type { components } from '../ui-api/schema.generated';
import { OverviewPage } from './overview-page';

type Overview = components['schemas']['OverviewResponse'];

const path = '/api/ui/overview';
const now = new Date('2026-11-13T12:00:00Z');
const memberAreas = ['overview', 'secrets', 'activity'];
const administratorAreas = [...memberAreas, 'users', 'networks', 'audit'];
const administrator: Overview = {
  displayName: 'Vault Admin',
  totalActiveSecrets: 14,
  developmentCount: 6,
  stagingCount: 5,
  productionCount: 3,
  expiringWithin7Days: 9,
  criticalEventsLast24h: 2,
  staffAccounts: 7,
  activityScope: 'vault',
  recentEvents: [
    {
      timestampUtc: '2026-11-13T11:15:00Z',
      action: 'HoneyTokenTripped',
      actionLabel: 'Honey token',
      tone: 'critical',
      resource: 'FINANCE_DECOY_KEY',
      actor: 'mallory',
      isCritical: true,
    },
    {
      timestampUtc: '2026-11-11T08:00:00Z',
      action: 'SecretAccessDenied',
      actionLabel: 'Access denied',
      tone: 'warn',
      resource: 'PAYMENTS_DB_PASSWORD',
      actor: 'alice',
      isCritical: false,
    },
  ],
  expiringSoon: [
    {
      secretId: '00000000-0000-4000-8000-000000000001',
      name: 'WEB_TLS_CERT',
      environment: 'Production',
      expiresAtUtc: '2026-11-13T11:00:00Z',
    },
    {
      secretId: '00000000-0000-4000-8000-000000000002',
      name: 'SMTP_PASSWORD',
      environment: 'Staging',
      expiresAtUtc: '2026-11-13T12:20:00Z',
    },
    {
      secretId: '00000000-0000-4000-8000-000000000003',
      name: 'CI_DEPLOY_TOKEN',
      environment: 'Development',
      expiresAtUtc: '2026-11-16T09:30:00Z',
    },
  ],
};
const member: Overview = {
  displayName: 'Alice Member',
  totalActiveSecrets: 1,
  developmentCount: 1,
  stagingCount: 0,
  productionCount: 0,
  expiringWithin7Days: 1,
  criticalEventsLast24h: 0,
  staffAccounts: null,
  activityScope: 'own',
  recentEvents: [
    {
      timestampUtc: '2026-11-13T11:59:30Z',
      action: 'SecretAccessDenied',
      actionLabel: 'Access denied',
      tone: 'warn',
      resource: 'Secret',
      actor: 'alice',
      isCritical: false,
    },
  ],
  expiringSoon: [
    {
      secretId: '00000000-0000-4000-8000-000000000004',
      name: 'ALICE_API_KEY',
      environment: 'Development',
      expiresAtUtc: '2026-11-15T12:00:00Z',
    },
  ],
};
const quiet: Overview = {
  ...member,
  totalActiveSecrets: 0,
  developmentCount: 0,
  expiringWithin7Days: 0,
  recentEvents: [],
  expiringSoon: [],
};
let backend: HttpTestingController;

/** The element's words as a reader sees them: Angular drops the whitespace between its spans. */
function text(element: Element | null | undefined): string {
  if (!element) return '';
  const words: string[] = [];
  const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
  while (walker.nextNode()) words.push(walker.currentNode.textContent ?? '');
  return words.join(' ').replace(/\s+/g, ' ').trim();
}
function texts(page: ParentNode, selector: string): string[] {
  return [...page.querySelectorAll(selector)].map((element) => text(element));
}
function links(page: ParentNode): [string, string | null][] {
  return [...page.querySelectorAll('a')].map((link) => [text(link), link.getAttribute('href')]);
}
function me(areas: string[]): Me {
  return {
    username: 'someone',
    displayName: 'Someone',
    clearance: 'Standard',
    project: 'GLOBAL',
    areas,
    avatarUrl: '/Profile/Avatar',
    licenseNotice: null,
  };
}
function render() {
  TestBed.configureTestingModule({
    providers: [...appConfig.providers, provideHttpClientTesting()],
  });
  backend = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(OverviewPage);
  fixture.detectChanges();
  return { fixture, page: fixture.nativeElement as HTMLElement };
}
async function loaded(overview: Overview, areas: string[]) {
  const rendered = render();
  backend.expectOne('/api/ui/me').flush(me(areas));
  backend.expectOne(path).flush(overview);
  await rendered.fixture.whenStable();
  return rendered;
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(now);
});
afterEach(() => {
  try {
    backend.verify();
  } finally {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  }
});

describe('Overview', () => {
  it('shows loading until the overview arrives', async () => {
    const { page, fixture } = render();
    expect(text(page.querySelector('[role="status"]'))).toBe('Loading the overview…');
    backend.expectOne('/api/ui/me').flush(me(memberAreas));
    backend.expectOne(path).flush(member);
    await fixture.whenStable();
    expect(page.querySelector('[role="status"]')).toBeNull();
  });

  it('shows a failed load with Try again, which loads the overview', async () => {
    const { page, fixture } = render();
    backend.expectOne('/api/ui/me').flush(me(memberAreas));
    backend.expectOne(path).flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(text(page.querySelector('[role="alert"]'))).toBe(
      'Could not load the overview. Try again.',
    );
    [...page.querySelectorAll('button')].find((button) => text(button) === 'Try again')!.click();
    fixture.detectChanges();
    backend.expectOne(path).flush(member);
    await fixture.whenStable();
    expect(page.querySelector('[role="alert"]')).toBeNull();
    expect(text(page.querySelector('.page-sub'))).toBe(
      'Welcome back, Alice Member. Here is the vault at a glance.',
    );
  });

  it('counts every expiring secret in the lede, not only the rows listed', async () => {
    const { page } = await loaded(administrator, administratorAreas);
    expect(text(page.querySelector('.panel-lede'))).toBe(
      '9 secrets will expire within 7 days and be shredded automatically. Rotate them to extend their life.',
    );
    expect(texts(page, '.attention-title')).toEqual([
      'WEB_TLS_CERT',
      'SMTP_PASSWORD',
      'CI_DEPLOY_TOKEN',
    ]);
  });

  it('names a single expiring secret in the singular', async () => {
    const { page } = await loaded(member, memberAreas);
    expect(text(page.querySelector('.panel-lede'))).toBe(
      '1 secret will expire within 7 days and be shredded automatically. Rotate them to extend their life.',
    );
  });

  it('shows no Expiring soon panel when nothing expires', async () => {
    const { page } = await loaded(quiet, memberAreas);
    expect(page.querySelector('.panel-lede')).toBeNull();
    expect(texts(page, 'h2')).toEqual(['Your recent activity', 'Quick actions']);
    expect(text(page.querySelector('.panel .muted'))).toBe('No recorded activity yet.');
  });

  it("gives an administrator the vault's figures and the staff account count", async () => {
    const { page } = await loaded(administrator, administratorAreas);
    expect(texts(page, '.stat-card')).toEqual([
      '14 Active secrets 6 dev · 5 staging · 3 prod',
      '9 Expiring within 7 days Shredded automatically at TTL',
      '2 Critical alerts (24 h) Review the audit log →',
      '7 Staff accounts Manage users →',
    ]);
  });

  it('puts Expiring soon ahead of activity and Quick actions when secrets expire', async () => {
    const { page } = await loaded(administrator, administratorAreas);
    expect(texts(page, 'h2')).toEqual(['Expiring soon', 'Recent activity', 'Quick actions']);
  });

  it("gives an administrator the vault's activity and marks critical events with an icon and word", async () => {
    const { page } = await loaded(administrator, administratorAreas);
    expect(text(page.querySelector('#overview-activity'))).toBe('Recent activity');
    expect(texts(page, '.activity-item')).toEqual([
      'Critical Honey token FINANCE_DECOY_KEY mallory · 45m ago',
      'Access denied PAYMENTS_DB_PASSWORD alice · 2d ago',
    ]);
    const items = [...page.querySelectorAll('.activity-item')];
    expect(items[0]?.querySelector('.critical-mark svg')).not.toBeNull();
    expect(items[1]?.querySelector('.critical-mark')).toBeNull();
  });

  it('links an administrator to the audit log and the administration pages', async () => {
    const { page } = await loaded(administrator, administratorAreas);
    expect(links(page)).toEqual([
      ['New secret', '/Secrets/Create'],
      ['14 Active secrets 6 dev · 5 staging · 3 prod', '/Secrets'],
      ['9 Expiring within 7 days Shredded automatically at TTL', '/Secrets'],
      ['2 Critical alerts (24 h) Review the audit log →', '/Audit'],
      ['7 Staff accounts Manage users →', '/Admin/Users'],
      ['Rotate', '/Secrets/Details/00000000-0000-4000-8000-000000000001'],
      ['Rotate', '/Secrets/Details/00000000-0000-4000-8000-000000000002'],
      ['Rotate', '/Secrets/Details/00000000-0000-4000-8000-000000000003'],
      ['All secrets', '/Secrets'],
      ['Full audit log', '/Audit'],
      ['Browse secrets Metadata only — reveals are audited', '/Secrets'],
      ['Store a new secret AES-256-GCM envelope encryption', '/Secrets/Create'],
      ['Provision a user They set their own password and authenticator', '/Admin/CreateUser'],
      ['Manage trusted networks Trust your current IP, lift blocks', '/Admin/Networks'],
    ]);
  });

  it('gives a non-administrator their figures and the critical count, without a staff count', async () => {
    const { page } = await loaded(member, memberAreas);
    expect(texts(page, '.stat-card')).toEqual([
      '1 Active secrets 1 dev · 0 staging · 0 prod',
      '1 Expiring within 7 days Shredded automatically at TTL',
      '0 Critical alerts (24 h) All quiet',
    ]);
  });

  it('gives a non-administrator their own activity, its denial without the secret', async () => {
    const { page } = await loaded(member, memberAreas);
    expect(text(page.querySelector('#overview-activity'))).toBe('Your recent activity');
    expect(texts(page, '.activity-item')).toEqual(['Access denied Secret alice · just now']);
  });

  it('links a non-administrator only to secrets', async () => {
    const { page } = await loaded(member, memberAreas);
    expect(links(page)).toEqual([
      ['New secret', '/Secrets/Create'],
      ['1 Active secrets 1 dev · 0 staging · 0 prod', '/Secrets'],
      ['1 Expiring within 7 days Shredded automatically at TTL', '/Secrets'],
      ['Rotate', '/Secrets/Details/00000000-0000-4000-8000-000000000004'],
      ['All secrets', '/Secrets'],
      ['Browse secrets Metadata only — reveals are audited', '/Secrets'],
      ['Store a new secret AES-256-GCM envelope encryption', '/Secrets/Create'],
    ]);
  });

  it('tells a non-administrator that critical alerts went to the administrators', async () => {
    const { page } = await loaded({ ...member, criticalEventsLast24h: 3 }, memberAreas);
    expect(texts(page, '.stat-card')).toContain(
      '3 Critical alerts (24 h) Reported to administrators',
    );
  });

  it('shows each expiring secret with its environment and expiry pill', async () => {
    const { page } = await loaded(administrator, administratorAreas);
    expect(
      [...page.querySelectorAll('.attention-list > li')].map((item) => [
        text(item.querySelector('.attention-title')),
        text(item.querySelector('.attention-meta')),
        text(item.querySelector('.attention-chip')),
      ]),
    ).toEqual([
      ['WEB_TLS_CERT', 'Production', 'Expired · 13 Nov'],
      ['SMTP_PASSWORD', 'Staging', 'In 1 hour · 13 Nov'],
      ['CI_DEPLOY_TOKEN', 'Development', 'In 2 days · 16 Nov'],
    ]);
  });
});
