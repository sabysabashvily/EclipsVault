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
    for (const dialog of page.querySelectorAll('dialog')) {
      dialog.showModal = () => dialog.setAttribute('open', '');
      dialog.close = () => dialog.removeAttribute('open');
    }
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

  /** Opens the approve dialog, types the expiry, and confirms. */
  async function approve(
    fixture: { whenStable(): Promise<unknown> },
    page: HTMLElement,
    days?: string,
  ) {
    button(page, 'Approve').click();
    await fixture.whenStable();
    if (days !== undefined) {
      const field = page.querySelector<HTMLInputElement>('#approval-days')!;
      field.value = days;
      field.dispatchEvent(new Event('input'));
    }
    button(page, 'Approve and grant access').click();
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
    expect(panel(page, 'To review').querySelectorAll('li')).toHaveLength(1);
    expect(page.textContent).not.toContain('Could not load');
  });

  it('shows the empty state in each panel and that nothing waits on you', async () => {
    const { page } = await loaded(empty);
    expect(page.querySelector('.title-row .badge')?.textContent).toBe('Nothing to review');
    expect(page.querySelector('.title-row .badge')?.classList).toContain('tone-ok');
    expect(page.querySelector('.page-sub')?.textContent).toBe(
      'Nothing waits on you · requests you file from a denied secret appear under My requests.',
    );
    const review = panel(page, 'To review');
    expect(review.querySelector('.sort-note')?.textContent).toBe('Your project · Newest first');
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

  it('lists each request to review with who asks, why, why they were denied and how long it waited', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-20T13:05:00Z'));
    try {
      const { page } = await loaded({
        ...empty,
        reviewScope: 'all',
        toReview: [toReview, noDenial],
      });
      expect(page.querySelector('.title-row .badge')?.textContent).toBe('Waiting on you');
      expect(page.querySelector('.title-row .badge')?.classList).toContain('tone-warn');
      expect(page.querySelector('.page-sub')?.textContent).toBe(
        '2 requests wait on you · approving shares the secret; you choose when access ends.',
      );
      const review = panel(page, 'To review');
      expect(review.querySelector('.sort-note')?.textContent).toBe('All projects · Newest first');
      const list = review.querySelector('ul[aria-label="Requests to review"]')!;
      expect(
        [...list.querySelectorAll('li')].map((item) =>
          [...item.querySelectorAll('.attention-title, .attention-meta, .badge, button')].map(
            (part) => part.textContent?.trim(),
          ),
        ),
      ).toEqual([
        [
          'Deployment_key GLOBAL',
          'colleague · Need it for the release',
          "Denied because: Subject project 'OPS' does not match resource project 'GLOBAL'.",
          '3h ago',
          'Approve',
          'Reject',
        ],
        ['Other_key GLOBAL', 'colleague · Need it for the release', '3h ago', 'Approve', 'Reject'],
      ]);
      expect(list.querySelector('.badge')?.getAttribute('title')).toBe('2026-10-20 10:05:00 UTC');
    } finally {
      vi.useRealTimers();
    }
  });

  it('dates a request that waited over a week', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-28T10:05:00Z'));
    try {
      const { page } = await loaded({ ...empty, toReview: [toReview] });
      expect(page.querySelector('.attention-chip')?.textContent).toBe('Since 20 Oct');
    } finally {
      vi.useRealTimers();
    }
  });

  it('asks before approving, naming the request and what the share carries', async () => {
    const { fixture, page } = await loaded({ ...empty, toReview: [toReview] });
    button(page, 'Approve').click();
    await fixture.whenStable();
    const dialog = page.querySelector('dialog[open]')!;
    expect(dialog.querySelector('h2')?.textContent).toBe("Approve colleague's request?");
    expect(dialog.querySelector('h2 + p')?.textContent).toBe(
      'colleague gets Deployment_key as a share.',
    );
    expect(
      [...dialog.querySelectorAll('.facts div')].map((row) => [
        row.querySelector('dt')?.textContent,
        row.querySelector('dd')?.textContent,
      ]),
    ).toEqual([
      ['Secret', 'Deployment_key'],
      ['Project', 'GLOBAL'],
      ['Requester', 'colleague'],
      ['Reason', 'Need it for the release'],
    ]);
    const field = dialog.querySelector<HTMLInputElement>('input')!;
    expect(dialog.querySelector(`label[for="${field.id}"]`)?.textContent).toBe(
      'Access ends after (days)',
    );
    expect([field.type, field.min, field.max, field.value]).toEqual(['number', '0', '3650', '0']);
    expect(document.getElementById(field.getAttribute('aria-describedby')!)?.textContent).toBe(
      '0 = no end date',
    );
    const chips = () =>
      [...dialog.querySelectorAll('.scope-chips li')].map((chip) => chip.textContent?.trim());
    expect(chips()).toEqual(['Every reveal is logged', 'No end date']);
    field.value = '3';
    field.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(chips()).toEqual(['Every reveal is logged', 'Ends after 3 days']);
    expect(dialog.querySelector('.dialog-note')?.textContent).toBe('Saved to the audit log');
    button(page, 'Cancel').click();
    backend.expectNone(`${path}/${toReview.requestId}/approve`);
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

  it('gives each status the tone of what it means', async () => {
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

  it('sends the typed expiry with the antiforgery token once confirmed', async () => {
    document.cookie = 'XSRF-TOKEN=access-requests-token; path=/';
    const { fixture, page } = await loaded({ ...empty, toReview: [toReview] });
    await approve(fixture, page, '7');
    fixture.detectChanges();
    expect(page.querySelector('dialog[open]')).toBeNull();
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
    await approve(fixture, page);
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
    await approve(fixture, page);
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
    await approve(fixture, page, '');
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
    'refuses an expiry of %s before anything is sent, keeping the approval and what was typed',
    async (typed) => {
      const { fixture, page } = await loaded({ ...empty, toReview: [toReview] });
      await approve(fixture, page, typed);
      await fixture.whenStable();
      backend.expectNone(`${path}/${toReview.requestId}/approve`);
      const dialog = page.querySelector('dialog[open]');
      expect(dialog?.querySelector('h2')?.textContent).toBe("Approve colleague's request?");
      expect(dialog?.querySelector<HTMLInputElement>('#approval-days')?.value).toBe(typed);
      expect(announced(page).error).toBe('');
    },
  );

  it('sends no rejection when the dialog is cancelled', async () => {
    const { fixture, page } = await loaded({ ...empty, toReview: [toReview] });
    button(page, 'Reject').click();
    await fixture.whenStable();
    expect(page.querySelector('dialog h2')?.textContent).toBe('Reject this request?');
    expect(page.querySelector('dialog p')?.textContent).toBe(
      "Reject colleague's request for Deployment_key?",
    );
    button(page, 'Cancel').click();
    await fixture.whenStable();
    backend.expectNone(`${path}/${toReview.requestId}/reject`);
    expect(panel(page, 'To review').querySelectorAll('li')).toHaveLength(1);
  });

  it('announces a confirmed rejection once the lists reload', async () => {
    const { fixture, page } = await loaded({ ...empty, toReview: [toReview] });
    button(page, 'Reject').click();
    await fixture.whenStable();
    button(page, 'Reject request').click();
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
    button(page, 'Withdraw request').click();
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
      await approve(fixture, page);
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
    button(page, 'Withdraw request').click();
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
    // A name every plain object inherits: were the refusals map an object literal, it would be found.
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
