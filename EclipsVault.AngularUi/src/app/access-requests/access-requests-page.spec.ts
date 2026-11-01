import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { appConfig } from '../app.config';
import type { components } from '../ui-api/schema.generated';
import { AccessRequestsPage } from './access-requests-page';

type Page = components['schemas']['AccessRequestsResponse'];
type Filed = components['schemas']['FiledRequestResponse'];

const path = '/api/ui/access-requests';
const toReview: components['schemas']['RequestToReviewResponse'] = {
  requestId: '11111111-1111-1111-1111-111111111111',
  secretName: 'Deployment_key',
  project: 'GLOBAL',
  requester: 'colleague',
  reason: 'Need it for the release',
  deniedBecause: "Subject project 'OPS' does not match resource project 'GLOBAL'.",
  requestedAtUtc: '2026-10-20T10:05:00Z',
};
const noDenial = {
  ...toReview,
  requestId: '22222222-2222-2222-2222-222222222222',
  secretName: 'Other_key',
  deniedBecause: null,
};
const filed: Filed = {
  requestId: '33333333-3333-3333-3333-333333333333',
  secretName: 'Billing_key',
  project: 'FINANCE',
  status: 'Pending',
  requestedAtUtc: '2026-10-21T08:30:00Z',
  decidedBy: null,
  decidedAtUtc: null,
  decisionNote: null,
};
const empty: Page = { reviewScope: 'project', toReview: [], mine: [] };

describe('AccessRequestsPage', () => {
  let backend: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;

  function render(query: Record<string, string> = {}) {
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
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(AccessRequestsPage);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    const dialog = page.querySelector('dialog')!;
    dialog.showModal = () => dialog.setAttribute('open', '');
    dialog.close = () => dialog.removeAttribute('open');
    return { fixture, page };
  }

  async function loaded(body: Page, query: Record<string, string> = {}) {
    const rendered = render(query);
    backend.expectOne(path).flush(body);
    await rendered.fixture.whenStable();
    return rendered;
  }

  function button(scope: ParentNode, text: string): HTMLButtonElement {
    return [...scope.querySelectorAll('button')].find(
      (candidate) => candidate.textContent?.trim() === text,
    )!;
  }

  function panel(page: HTMLElement, heading: string): HTMLElement {
    return [...page.querySelectorAll<HTMLElement>('section.panel')].find(
      (section) => section.querySelector('h2')?.textContent === heading,
    )!;
  }

  function rows(scope: ParentNode): (string | undefined)[][] {
    return [...scope.querySelectorAll('tbody tr')].map((row) =>
      [...row.querySelectorAll('td')].map((cell) => cell.textContent?.replace(/\s+/g, ' ').trim()),
    );
  }

  /** The text of every secret, muted and plain-text cell, exactly as written. */
  function textCells(scope: ParentNode): (string | null)[] {
    return [
      ...scope.querySelectorAll(
        'tbody td.cell-strong, tbody td.muted, tbody td:not([class]):not(:has(*))',
      ),
    ].map((cell) => cell.textContent);
  }

  function announced(page: HTMLElement) {
    return {
      success: page.querySelector('.flash-success')?.textContent ?? '',
      error: page.querySelector('.flash-error')?.textContent ?? '',
    };
  }

  afterEach(() => {
    document.cookie = 'XSRF-TOKEN=; path=/; max-age=0';
    backend.verify();
  });

  it('shows loading until the server answers', () => {
    const { page } = render();
    expect(page.textContent).toContain('Loading your access requests…');
    backend.expectOne(path).flush(empty);
  });

  it('retries a failed fetch in place', async () => {
    const { fixture, page } = render();
    backend.expectOne(path).flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(page.querySelector('p[role="alert"]')?.textContent).toBe(
      'Could not load your access requests. Try again.',
    );
    button(page, 'Try again').click();
    fixture.detectChanges();
    backend.expectOne(path).flush({ ...empty, toReview: [toReview] });
    await fixture.whenStable();
    expect(rows(panel(page, 'To review'))).toHaveLength(1);
    expect(page.textContent).not.toContain('Could not load');
  });

  it('shows the Legacy empty state in each panel', async () => {
    const { page } = await loaded(empty);
    const review = panel(page, 'To review');
    expect(review.querySelector('.panel-header .muted')?.textContent?.trim()).toBe('Your project');
    expect(review.querySelector('h3')?.textContent).toBe('Nothing waiting on you');
    expect(review.querySelector('.empty-state p')?.textContent).toBe(
      'When someone requests access to a secret you can share, it lands here for you to approve or reject.',
    );
    expect(review.querySelector('a')).toBeNull();

    const mine = panel(page, 'My requests');
    expect(mine.querySelector('h3')?.textContent).toBe("You haven't requested any access");
    expect(mine.querySelector('.empty-state p')?.textContent).toBe(
      'If you\'re denied a secret outside your clearance or project, its page offers a "Request access" button — your requests and their decisions then show up here.',
    );
    expect(mine.querySelector('a')?.textContent).toBe('Browse secrets');
    expect(mine.querySelector('a')?.getAttribute('href')).toBe('/Secrets');
  });

  it('visual parity: an empty My requests panel draws the Legacy inbox icon', async () => {
    const { page } = await loaded(empty);
    expect(panel(page, 'My requests').querySelectorAll('.empty-icon path')).toHaveLength(3);
  });

  it('lists each request to review as Legacy did', async () => {
    const { page } = await loaded({ ...empty, reviewScope: 'all', toReview: [toReview, noDenial] });
    const review = panel(page, 'To review');
    expect(review.querySelector('.panel-header .muted')?.textContent?.trim()).toBe('All projects');
    expect([...review.querySelectorAll('th')].map((cell) => cell.textContent)).toEqual([
      'Secret',
      'Requester',
      'Reason',
      'Denied because',
      'Requested',
      '',
    ]);
    expect(rows(review)).toEqual([
      [
        'Deployment_key GLOBAL',
        'colleague',
        'Need it for the release',
        "Subject project 'OPS' does not match resource project 'GLOBAL'.",
        '2026-10-20 10:05',
        'Approve Reject',
      ],
      [
        'Other_key GLOBAL',
        'colleague',
        'Need it for the release',
        '—',
        '2026-10-20 10:05',
        'Approve Reject',
      ],
    ]);
    expect(review.querySelector('td .badge.tone-muted')?.textContent).toBe('GLOBAL');
    // Legacy's text cells held their text alone, with no spaces around it.
    expect(textCells(review)).toEqual([
      'Deployment_key GLOBAL',
      'colleague',
      'Need it for the release',
      "Subject project 'OPS' does not match resource project 'GLOBAL'.",
      '2026-10-20 10:05',
      'Other_key GLOBAL',
      'colleague',
      'Need it for the release',
      '—',
      '2026-10-20 10:05',
    ]);
    const expiry = review.querySelector('input')!;
    expect(expiry.type).toBe('number');
    expect([expiry.min, expiry.max, expiry.value]).toEqual(['0', '3650', '0']);
    expect(expiry.title).toBe('Grant expires in days (0 = no expiry)');
    expect(expiry.getAttribute('aria-label')).toBe('Grant expiry in days');
  });

  it('shows each of your requests as Legacy did', async () => {
    const decided = { decidedBy: 'reviewer', decidedAtUtc: '2026-10-22T17:45:00Z' };
    const { page } = await loaded({
      ...empty,
      mine: [
        filed,
        { ...filed, requestId: 'a', status: 'Approved', ...decided, decisionNote: null },
        {
          ...filed,
          requestId: 'b',
          status: 'Rejected',
          ...decided,
          decisionNote: 'Not this quarter',
        },
        // Withdrawing records the requester as the one who decided.
        {
          ...filed,
          requestId: 'c',
          status: 'Cancelled',
          decidedBy: 'requester',
          decidedAtUtc: '2026-10-21T09:00:00Z',
        },
      ],
    });
    const mine = panel(page, 'My requests');
    expect([...mine.querySelectorAll('th')].map((cell) => cell.textContent)).toEqual([
      'Secret',
      'Status',
      'Requested',
      'Decision',
      '',
    ]);
    expect(rows(mine)).toEqual([
      ['Billing_key FINANCE', 'Pending', '2026-10-21 08:30', '—', 'Withdraw'],
      ['Billing_key FINANCE', 'Approved', '2026-10-21 08:30', 'reviewer · 2026-10-22', ''],
      [
        'Billing_key FINANCE',
        'Rejected',
        '2026-10-21 08:30',
        'reviewer · 2026-10-22 — Not this quarter',
        '',
      ],
      ['Billing_key FINANCE', 'Withdrawn', '2026-10-21 08:30', 'requester · 2026-10-21', ''],
    ]);
    expect(textCells(mine)).toEqual([
      'Billing_key FINANCE',
      '2026-10-21 08:30',
      '—',
      'Billing_key FINANCE',
      '2026-10-21 08:30',
      'reviewer · 2026-10-22',
      'Billing_key FINANCE',
      '2026-10-21 08:30',
      'reviewer · 2026-10-22 — Not this quarter',
      'Billing_key FINANCE',
      '2026-10-21 08:30',
      'requester · 2026-10-21',
    ]);
  });

  it('visual parity: each status badge takes its Legacy tone', async () => {
    const { page } = await loaded({
      ...empty,
      mine: [
        filed,
        { ...filed, requestId: 'a', status: 'Approved' },
        { ...filed, requestId: 'b', status: 'Rejected' },
        { ...filed, requestId: 'c', status: 'Cancelled' },
      ],
    });
    expect(
      [...page.querySelectorAll('td:nth-child(2) .badge')].map((badge) => badge.className),
    ).toEqual(['badge tone-warn', 'badge tone-ok', 'badge tone-danger', 'badge tone-muted']);
  });

  it('sends the typed expiry with the antiforgery token, without asking first', async () => {
    document.cookie = 'XSRF-TOKEN=access-requests-token; path=/';
    const { fixture, page } = await loaded({ ...empty, toReview: [toReview] });
    page.querySelector('input')!.value = '7';
    button(page, 'Approve').click();
    fixture.detectChanges();
    expect(page.querySelector('dialog')?.hasAttribute('open')).toBe(false);
    const approval = backend.expectOne({
      method: 'POST',
      url: `${path}/${toReview.requestId}/approve`,
    });
    expect(approval.request.body).toEqual({ expiresInDays: 7 });
    expect(approval.request.headers.get('RequestVerificationToken')).toBe('access-requests-token');
    approval.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    backend.expectOne(path).flush(empty);
  });

  it('disables every decision while one is in flight', async () => {
    const { fixture, page } = await loaded({ ...empty, toReview: [toReview] });
    button(page, 'Approve').click();
    fixture.detectChanges();
    expect(button(page, 'Approve').disabled).toBe(true);
    expect(button(page, 'Reject').disabled).toBe(true);
    backend
      .expectOne(`${path}/${toReview.requestId}/approve`)
      .flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    backend.expectOne(path).flush(empty);
  });

  it('announces an approval once the lists reload', async () => {
    const { fixture, page } = await loaded({ ...empty, toReview: [toReview] });
    button(page, 'Approve').click();
    backend
      .expectOne(`${path}/${toReview.requestId}/approve`)
      .flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    backend.expectOne(path).flush(empty);
    await fixture.whenStable();
    expect(announced(page)).toEqual({
      success: 'Request approved — a grant was created for the requester.',
      error: '',
    });
    expect(page.textContent).toContain('Nothing waiting on you');
  });

  it('sends an empty expiry as null, which the server refuses as out of range', async () => {
    const { fixture, page } = await loaded({ ...empty, toReview: [toReview] });
    page.querySelector('input')!.value = '';
    button(page, 'Approve').click();
    const approval = backend.expectOne(`${path}/${toReview.requestId}/approve`);
    expect(approval.request.body).toEqual({ expiresInDays: null });
    approval.flush(
      { code: 'validation_failed', title: 'Grant expiry must be between 0 and 3650 days.' },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();
    backend.expectOne(path).flush({ ...empty, toReview: [toReview] });
    await fixture.whenStable();
    expect(announced(page).error).toBe('Grant expiry must be between 0 and 3650 days.');
  });

  it.each(['1.5', '-1', '3651'])(
    'lets the browser refuse an expiry of %s before anything is sent, as Legacy did',
    async (typed) => {
      const { page } = await loaded({ ...empty, toReview: [toReview] });
      page.querySelector('input')!.value = typed;
      button(page, 'Approve').click();
      backend.expectNone(`${path}/${toReview.requestId}/approve`);
    },
  );

  it('sends no rejection when the dialog is cancelled', async () => {
    const { fixture, page } = await loaded({ ...empty, toReview: [toReview] });
    button(page, 'Reject').click();
    await fixture.whenStable();
    expect(page.querySelector('dialog p')?.textContent).toBe(
      "Reject colleague's request for Deployment_key?",
    );
    button(page, 'Cancel').click();
    await fixture.whenStable();
    backend.expectNone(`${path}/${toReview.requestId}/reject`);
    expect(rows(panel(page, 'To review'))).toHaveLength(1);
  });

  it('announces a confirmed rejection once the lists reload', async () => {
    const { fixture, page } = await loaded({ ...empty, toReview: [toReview] });
    button(page, 'Reject').click();
    await fixture.whenStable();
    button(page, 'Continue').click();
    const rejection = backend.expectOne({
      method: 'POST',
      url: `${path}/${toReview.requestId}/reject`,
    });
    expect(rejection.request.body).toBeNull();
    rejection.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    backend.expectOne(path).flush(empty);
    await fixture.whenStable();
    expect(announced(page)).toEqual({ success: 'Request rejected.', error: '' });
  });

  it('announces a withdrawal once it is confirmed', async () => {
    const { fixture, page } = await loaded({ ...empty, mine: [filed] });
    button(page, 'Withdraw').click();
    await fixture.whenStable();
    expect(page.querySelector('dialog p')?.textContent).toBe(
      'Withdraw your request for Billing_key?',
    );
    button(page, 'Continue').click();
    backend
      .expectOne({ method: 'POST', url: `${path}/${filed.requestId}/withdraw` })
      .flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    backend.expectOne(path).flush({ ...empty, mine: [{ ...filed, status: 'Cancelled' }] });
    await fixture.whenStable();
    expect(announced(page)).toEqual({ success: 'Request withdrawn.', error: '' });
    expect(button(page, 'Withdraw')).toBeUndefined();
  });

  it.each([
    [404, 'request_not_found', 'That request was not found.'],
    [409, 'request_not_pending', 'That request is no longer pending.'],
    [
      409,
      'request_ineligible',
      "A share would not help: the requester's clearance is below this secret's sensitivity.",
    ],
    [409, 'share_exists', 'They already have a share on this secret.'],
    [503, 'audit_unavailable', 'The audit trail is unavailable, so the vault refused the request.'],
  ])(
    'announces a %i %s refusal as an error over freshly loaded lists',
    async (status, code, title) => {
      const { fixture, page } = await loaded({ ...empty, toReview: [toReview] });
      button(page, 'Approve').click();
      backend
        .expectOne(`${path}/${toReview.requestId}/approve`)
        .flush({ code, title }, { status, statusText: 'Refused' });
      fixture.detectChanges();
      backend.expectOne(path).flush({ ...empty, toReview: [toReview] });
      await fixture.whenStable();
      expect(announced(page)).toEqual({ success: '', error: title });
      expect(button(page, 'Approve').disabled).toBe(false);
    },
  );

  it("announces a network failure in the interceptor's words", async () => {
    const { fixture, page } = await loaded({ ...empty, mine: [filed] });
    button(page, 'Withdraw').click();
    await fixture.whenStable();
    button(page, 'Continue').click();
    backend.expectOne(`${path}/${filed.requestId}/withdraw`).error(new ProgressEvent('error'));
    fixture.detectChanges();
    backend.expectOne(path).flush({ ...empty, mine: [filed] });
    await fixture.whenStable();
    expect(announced(page).error).toBe('The vault could not complete the request.');
  });

  it('announces a filed request once', async () => {
    const { page } = await loaded(empty, { filed: '1' });
    expect(announced(page)).toEqual({
      success: 'Access request submitted. A reviewer for this secret will decide.',
      error: '',
    });
    expect(navigate).toHaveBeenCalledWith(
      [],
      expect.objectContaining({
        queryParams: { filed: null, refused: null },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      }),
    );
  });

  it.each([
    [
      'share_cannot_help',
      "A share would not help: your clearance is below this secret's sensitivity.",
    ],
    [
      'already_able_to_open',
      'A share would not help: your project, clearance or an active share already covers this secret.',
    ],
    ['already_pending', 'You already have a pending request for this secret.'],
    ['reason_too_short', 'Add a short reason so a reviewer can decide.'],
    ['secret_not_found', 'That secret no longer exists.'],
  ])('announces the refusal %s as an error', async (code, message) => {
    const { page } = await loaded(empty, { refused: code });
    expect(announced(page)).toEqual({ success: '', error: message });
    expect(navigate).toHaveBeenCalledTimes(1);
  });

  it.each<Record<string, string>>([
    // A name every plain object inherits: a lookup in an object literal would find it and show a message.
    { refused: 'constructor' },
    { refused: 'unknown_code' },
    { filed: 'yes' },
  ])('removes %j without showing anything', async (query) => {
    const { page } = await loaded(empty, query);
    expect(announced(page)).toEqual({ success: '', error: '' });
    expect(navigate).toHaveBeenCalledTimes(1);
  });

  it('leaves the address alone when it carries no outcome', async () => {
    await loaded(empty);
    expect(navigate).not.toHaveBeenCalled();
  });
});
