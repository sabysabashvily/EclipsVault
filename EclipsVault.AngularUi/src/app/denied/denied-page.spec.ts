import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Navigation, Router } from '@angular/router';
import type { Me } from '../account/me';
import { appConfig } from '../app.config';
import { DeniedPage } from './denied-page';

const secretId = '6d1b2c55-0000-4000-8000-000000000001';
const eligibility = `/api/ui/access-requests/eligibility?secretId=${secretId}`;
const member: Me = {
  username: 'alice',
  displayName: 'Alice',
  clearance: 'Elevated',
  project: 'PHOENIX',
  areas: ['overview', 'secrets'],
  avatarUrl: '/Profile/Avatar',
  licenseNotice: null,
};
const administrator: Me = { ...member, areas: ['overview', 'secrets', 'networks'] };
const reasons = [
  "Subject project 'PHOENIX' does not match resource project 'GLOBAL'.",
  "Source address '203.0.113.9' is outside the trusted network ranges.",
];
const generic =
  "Your clearance, project assignment, the time window, or your network location did not satisfy the resource's requirements. The attempt has been logged.";
const helpText =
  "Ask a reviewer, an administrator or a member of this secret's project who can open it, to share it with you.";

/** The element's words as a reader sees them. */
function text(element: Element | null | undefined): string {
  return (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
}

describe('DeniedPage', () => {
  let backend: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;

  /** Opens the page at `query`, arriving by `navigation` (a reload has no previous navigation). */
  function open(query: Record<string, string>, navigation: Partial<Navigation> | null = null) {
    TestBed.configureTestingModule({
      providers: [
        ...appConfig.providers,
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(query) } },
        },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    vi.spyOn(router, 'currentNavigation').mockReturnValue(navigation as Navigation | null);
    navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(DeniedPage);
    fixture.detectChanges();
    return { fixture, page: fixture.nativeElement as HTMLElement };
  }

  function fromARefusal(state: Record<string, unknown>): Partial<Navigation> {
    return { trigger: 'imperative', previousNavigation: {} as Navigation, extras: { state } };
  }

  async function eligible(me: Me = member) {
    const rendered = open({ secretId }, fromARefusal({ reasons }));
    backend.expectOne('/api/ui/me').flush(me);
    backend.expectOne(eligibility).flush({ eligible: true, refusal: null, message: null });
    await rendered.fixture.whenStable();
    return rendered;
  }

  function submit(page: HTMLElement, reason: string) {
    const field = page.querySelector<HTMLTextAreaElement>('#reason')!;
    field.value = reason;
    field.dispatchEvent(new Event('input'));
    page.querySelector('form')!.dispatchEvent(new Event('submit'));
  }

  afterEach(() => backend.verify());

  it('lists the reasons the refusal carried in navigation memory', async () => {
    const { fixture, page } = open({}, fromARefusal({ reasons }));
    backend.expectOne('/api/ui/me').flush(member);
    await fixture.whenStable();

    expect(text(page.querySelector('h1'))).toBe('Access denied');
    expect([...page.querySelectorAll('.denial-reasons li')].map(text)).toEqual(reasons);
    expect(text(page)).toContain(
      'Access is granted only when every requirement above is satisfied. The attempt has been logged.',
    );
    expect(text(page)).not.toContain(generic);
  });

  it('shows the generic paragraph after a reload, which restores the state without a refusal', async () => {
    const { fixture, page } = open(
      {},
      { trigger: 'imperative', previousNavigation: null, extras: { state: { reasons } } },
    );
    backend.expectOne('/api/ui/me').flush(member);
    await fixture.whenStable();

    expect(page.querySelector('.denial-reasons')).toBeNull();
    expect(text(page)).toContain(generic);
  });

  it('shows the generic paragraph when no secret was refused, and asks nothing about requests', async () => {
    const { fixture, page } = open({});
    backend.expectOne('/api/ui/me').flush(member);
    await fixture.whenStable();

    expect(text(page)).toContain(generic);
    expect(page.querySelector('h2')).toBeNull();
  });

  it('asks nothing about requests for a secret id that is not an id', async () => {
    const { fixture, page } = open({ secretId: 'not-an-id' });
    backend.expectOne('/api/ui/me').flush(member);
    await fixture.whenStable();

    expect(page.querySelector('h2')).toBeNull();
  });

  it('offers the request form when a share would help', async () => {
    const { page } = await eligible();

    expect(text(page.querySelector('h2'))).toBe('Request access');
    expect(text(page)).toContain(helpText);
    expect(page.querySelector('label[for="reason"]')?.textContent).toBe('Reason');
    expect(page.querySelector('#reason')?.getAttribute('maxlength')).toBe('500');
    expect(text(page.querySelector('button[type="submit"]'))).toBe('Submit request');
  });

  it('says why instead of offering the form when a share would not help', async () => {
    const { fixture, page } = open({ secretId });
    backend.expectOne('/api/ui/me').flush(member);
    backend.expectOne(eligibility).flush({
      eligible: false,
      refusal: 'share_cannot_help',
      message: "A share would not help: your clearance is below this secret's sensitivity.",
    });
    await fixture.whenStable();

    expect(text(page.querySelector('h2'))).toBe('Request access');
    expect(text(page)).toContain(
      "A share would not help: your clearance is below this secret's sensitivity.",
    );
    expect(page.querySelector('form')).toBeNull();
    expect(text(page)).not.toContain(helpText);
  });

  it('files the request and opens Access requests saying it was filed', async () => {
    const { page } = await eligible();

    submit(page, 'Release on Friday');
    const filing = backend.expectOne('/api/ui/access-requests');
    expect([filing.request.method, filing.request.body]).toEqual([
      'POST',
      { secretId, reason: 'Release on Friday' },
    ]);
    filing.flush(null, { status: 204, statusText: 'No Content' });

    expect(navigate).toHaveBeenCalledExactlyOnceWith('/AccessRequests?filed=1');
  });

  it('opens Access requests with the refusal code when the request is not filed', async () => {
    const { page } = await eligible();

    submit(page, 'ab');
    backend
      .expectOne('/api/ui/access-requests')
      .flush(
        { code: 'reason_too_short', title: 'Add a short reason so a reviewer can decide.' },
        { status: 409, statusText: 'Conflict' },
      );

    expect(navigate).toHaveBeenCalledExactlyOnceWith('/AccessRequests?refused=reason_too_short');
  });

  it('stays and says so when the vault cannot record the request', async () => {
    const { fixture, page } = await eligible();

    submit(page, 'Release on Friday');
    backend.expectOne('/api/ui/access-requests').flush(
      {
        code: 'audit_unavailable',
        title: 'The audit trail is unavailable, so nothing was changed.',
      },
      { status: 503, statusText: 'Service Unavailable' },
    );
    await fixture.whenStable();

    expect(navigate).not.toHaveBeenCalled();
    expect(text(page.querySelector('[role="alert"]'))).toBe(
      'The audit trail is unavailable, so nothing was changed.',
    );
  });

  it('links back to the secrets, and to trusted networks only for an account that manages them', async () => {
    const links = (page: HTMLElement) =>
      [...page.querySelectorAll('.button-row a')].map((link) => [
        text(link),
        link.getAttribute('href'),
      ]);
    const { page: memberPage } = await eligible(member);
    expect(links(memberPage)).toEqual([['Back to secrets', '/Secrets']]);

    TestBed.resetTestingModule();
    const { page: administratorPage } = await eligible(administrator);
    expect(links(administratorPage)).toEqual([
      ['Back to secrets', '/Secrets'],
      ['Manage trusted networks', '/Admin/Networks'],
    ]);
  });
});
