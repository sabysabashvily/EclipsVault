import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { PAGE_LOCATION } from '../shell/command-palette';
import type { components } from '../ui-api/schema.generated';
import { AuditPage } from './audit-page';

type Audit = components['schemas']['AuditResponse'];
type Verification = components['schemas']['AuditVerificationResponse'];

const path = '/api/ui/audit';
const now = new Date('2026-11-13T12:00:00Z');
const listed: Audit = {
  entries: [
    {
      entryId: '00000000-0000-0000-0000-000000000003',
      timestampUtc: '2026-11-13T11:59:30Z',
      action: 'AuditLogViewed',
      actionLabel: 'Audit log viewed',
      tone: 'muted',
      resource: 'AuditTrail',
      actor: 'saba',
      sourceIp: '10.8.0.5',
      details: 'Opened the newest 200 entries',
      isCritical: false,
    },
    {
      entryId: '00000000-0000-0000-0000-000000000002',
      timestampUtc: '2026-11-13T09:15:00Z',
      action: 'HoneyTokenTripped',
      actionLabel: 'Honey token',
      tone: 'critical',
      resource: 'FINANCE_DECOY_KEY',
      actor: 'mallory',
      sourceIp: '192.0.2.9',
      details: 'Session revoked; source IP range blacklisted',
      isCritical: true,
    },
    {
      entryId: '00000000-0000-0000-0000-000000000001',
      timestampUtc: '2026-10-01T08:00:00Z',
      action: 'UserCreated',
      actionLabel: 'User created',
      tone: 'ok',
      resource: 'User',
      actor: 'vault-admin',
      sourceIp: 'internal',
      details: null,
      isCritical: false,
    },
  ],
  signingKeyId: 'sk-1a2b3c4d',
  latestCheckpoint: {
    sequence: 41,
    createdAtUtc: '2026-11-12T18:04:59Z',
    signingKeyId: 'sk-1a2b3c4d',
  },
};
const empty: Audit = { entries: [], signingKeyId: 'sk-1a2b3c4d', latestCheckpoint: null };
const anchored: Verification = {
  outcome: 'anchored',
  entriesVerified: 120,
  anchoredAtSequence: 41,
  firstBrokenSequence: null,
  checkpointsChecked: 2,
  checkpointsNotChecked: 0,
  message:
    "120 entries verified. The signed checkpoint at sequence 41 matches, so entries up to #41 are proven unchanged against the vault's signing key; later entries are covered by the hash chain until the next checkpoint. To prove the trail to an outside party, export a bundle and verify it offline against the vault's public key.",
};
const unanchored: Verification = {
  outcome: 'unanchored',
  entriesVerified: 12,
  anchoredAtSequence: null,
  firstBrokenSequence: null,
  checkpointsChecked: 0,
  checkpointsNotChecked: 0,
  message:
    '12 entries verified. Without a checkpoint, someone with database access could delete the newest entries or rewrite the trail and recompute its hashes without detection here. Create a signed checkpoint.',
};
const tampered: Verification = {
  outcome: 'tampered',
  entriesVerified: 40,
  anchoredAtSequence: null,
  firstBrokenSequence: 41,
  checkpointsChecked: 0,
  checkpointsNotChecked: 1,
  message:
    'The chain ends at #40, before the signed checkpoint at #41: entries were deleted. (First broken entry: #41.) 1 checkpoint(s) signed by another key were not checked.',
};
const busy = 'Another verification or export is running. Try again when it finishes.';
const unavailable = 'The audit trail is unavailable, so the vault refused the request.';
const disposition =
  "attachment; filename=eclipsvault-audit-bundle-20261113-120000.json; filename*=UTF-8''eclipsvault-audit-bundle-20261113-120000.json";
let backend: HttpTestingController;
let saved: string[];
const assign = vi.fn();

function button(page: ParentNode, name: string): HTMLButtonElement {
  const found = [...page.querySelectorAll('button')].find(
    (item) => item.textContent?.trim() === name,
  );
  if (!found) throw new Error(`No button named ${name}`);
  return found;
}
function text(element: Element | null | undefined): string {
  return element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}
function notice(page: ParentNode): Element | null {
  return page.querySelector('.notice');
}
function status(page: ParentNode, role: 'status' | 'alert'): string {
  return text(page.querySelector(`app-status-line [role="${role}"]`));
}
function rows(page: ParentNode): string[][] {
  return [...page.querySelectorAll('tbody tr')].map((row) =>
    [...row.querySelectorAll('td')].map((cell) => text(cell)),
  );
}
function problem(status: number, code: string, title: string, proof?: string): Blob {
  return new Blob([JSON.stringify({ status, code, title, proof })], {
    type: 'application/problem+json',
  });
}
function render() {
  history.replaceState(null, '', '/Audit');
  TestBed.configureTestingModule({
    providers: [
      ...appConfig.providers,
      provideHttpClientTesting(),
      { provide: PAGE_LOCATION, useValue: { assign } },
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(AuditPage);
  fixture.detectChanges();
  const page: HTMLElement = fixture.nativeElement;
  document.body.append(page);
  page.querySelectorAll('dialog').forEach((dialog) => {
    dialog.showModal = () => dialog.setAttribute('open', '');
    dialog.close = () => dialog.removeAttribute('open');
  });
  return { fixture, page };
}
async function loaded(audit = listed) {
  const rendered = render();
  backend.expectOne(path).flush(audit);
  await rendered.fixture.whenStable();
  return rendered;
}
async function verified(verification: Verification) {
  const rendered = await loaded();
  button(rendered.page, 'Verify integrity').click();
  backend.expectOne({ method: 'POST', url: `${path}/verify` }).flush(verification);
  await rendered.fixture.whenStable();
  return rendered;
}
/** A download's refusal is read from its Blob asynchronously, so the alert can arrive a tick later. */
async function alerted(fixture: ComponentFixture<unknown>, title: string) {
  await vi.waitFor(() => {
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('app-status-line [role="alert"]')?.textContent,
    ).toContain(title);
  });
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(now);
  saved = [];
  URL.createObjectURL = vi.fn(() => 'blob:https://vault.local/bundle');
  URL.revokeObjectURL = vi.fn();
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
    this: HTMLAnchorElement,
  ) {
    saved.push(this.download);
  });
  assign.mockReset();
});
afterEach(() => {
  try {
    backend.verify();
  } finally {
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
    vi.useRealTimers();
  }
});

describe('Audit log', () => {
  it('shows loading until the entries arrive', async () => {
    const { page, fixture } = render();
    expect(page.textContent).toContain('Loading the audit log…');
    backend.expectOne(path).flush(listed);
    await fixture.whenStable();
  });

  it('shows a failed load with Try again, which loads the entries', async () => {
    const { page, fixture } = render();
    backend.expectOne(path).flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(page.textContent).toContain('Could not load the audit log. Try again.');
    button(page, 'Try again').click();
    fixture.detectChanges();
    backend.expectOne(path).flush(listed);
    await fixture.whenStable();
    expect(page.textContent).not.toContain('Could not load the audit log.');
    expect(rows(page)).toHaveLength(3);
  });

  it('says the trail is unavailable when opening it cannot be recorded, and Try again loads it', async () => {
    const { page, fixture } = render();
    backend
      .expectOne(path)
      .flush(
        { status: 503, code: 'audit_unavailable', title: unavailable },
        { status: 503, statusText: 'Unavailable' },
      );
    await fixture.whenStable();
    expect(text(page.querySelector('[role="alert"]:not(app-status-line *)'))).toBe(unavailable);
    expect(rows(page)).toEqual([]);
    button(page, 'Try again').click();
    fixture.detectChanges();
    backend.expectOne(path).flush(listed);
    await fixture.whenStable();
    expect(rows(page)).toHaveLength(3);
  });

  it('shows an empty trail with no rows and no checkpoint yet', async () => {
    const { page } = await loaded(empty);
    expect(text(page.querySelector('.page-sub'))).toBe(
      'Immutable, fail-closed trail — showing the latest 0 entries.',
    );
    expect(rows(page)).toEqual([]);
    expect(text(page.querySelector('.detail-grid dd'))).toBe(
      'none yet — create one to attest the current trail',
    );
  });

  it('lists the entries in the order sent, in the Legacy columns, with relative times and UTC tooltips', async () => {
    const { page } = await loaded();
    expect(text(page.querySelector('.page-sub'))).toBe(
      'Immutable, fail-closed trail — showing the latest 3 entries.',
    );
    expect([...page.querySelectorAll('th')].map((cell) => cell.textContent)).toEqual([
      'When (UTC)',
      'Action',
      'Resource',
      'Actor',
      'Source IP',
      'Details',
    ]);
    expect(rows(page)).toEqual([
      [
        'just now',
        'Audit log viewed',
        'AuditTrail',
        'saba',
        '10.8.0.5',
        'Opened the newest 200 entries',
      ],
      [
        '2h ago',
        'Honey token',
        'FINANCE_DECOY_KEY',
        'mallory',
        '192.0.2.9',
        'Session revoked; source IP range blacklisted',
      ],
      ['2026-10-01', 'User created', 'User', 'vault-admin', 'internal', ''],
    ]);
    expect(
      [...page.querySelectorAll('tbody td:first-child')].map((cell) => cell.getAttribute('title')),
    ).toEqual(['2026-11-13 11:59:30 UTC', '2026-11-13 09:15:00 UTC', '2026-10-01 08:00:00 UTC']);
  });

  it('visual parity: critical rows are highlighted and each action is badged with its tone', async () => {
    const { page } = await loaded();
    expect(
      [...page.querySelectorAll('tbody tr')].map((row) => row.classList.contains('row-critical')),
    ).toEqual([false, true, false]);
    expect([...page.querySelectorAll('tbody .badge')].map((badge) => badge.className)).toEqual([
      'badge tone-muted',
      'badge tone-critical',
      'badge tone-ok',
    ]);
  });

  it('shows the attestation panel, the signing key and the latest checkpoint in Legacy words', async () => {
    const { page } = await loaded();
    expect(text(page.querySelector('.panel-header'))).toContain('Attestation');
    expect(text(page.querySelector('.panel-header'))).toContain('key sk-1a2b3c4d');
    expect(text(page.querySelector('.hint'))).toBe(
      "The hash chain is tamper-evident to anyone with the database. A signed checkpoint goes further: it binds the chain head with an ECDSA signature, so an outside auditor can prove the trail hasn't been rewritten using only the public key — even against someone who deleted rows and recomputed every hash. Export a bundle and verify it offline with the EclipsVault.AuditVerifier tool.",
    );
    expect(text(page.querySelector('.detail-grid'))).toBe(
      'Latest checkpoint sequence 41 · 2026-11-12 18:04 UTC · sk-1a2b3c4d',
    );
  });

  it('offers the Legacy filter', async () => {
    const { page } = await loaded();
    expect(
      page.querySelector<HTMLInputElement>('input[aria-label="Filter audit entries"]')?.placeholder,
    ).toBe('Filter by user, action, resource, IP…');
  });

  it.each([
    ['MALLORY', ['FINANCE_DECOY_KEY']],
    ['10.8.0', ['AuditTrail']],
    ['user created', ['User']],
    ['2026-10', ['User']],
    ['nothing matches this', []],
    ['', ['AuditTrail', 'FINANCE_DECOY_KEY', 'User']],
  ])('filtering by "%s" keeps the rows for %j, ignoring case', async (query, resources) => {
    const { page, fixture } = await loaded();
    const filter = page.querySelector<HTMLInputElement>(
      'input[aria-label="Filter audit entries"]',
    )!;
    filter.value = query;
    filter.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(rows(page).map((row) => row[2])).toEqual(resources);
  });

  it('shows the anchored notice after Verify without reading the entries again', async () => {
    const { page } = await verified(anchored);
    expect(text(notice(page))).toContain('✓ Hash chain intact.');
    expect(text(notice(page))).toContain(anchored.message);
    backend.expectNone(path);
  });

  it('shows the warning notice when no checkpoint covers the chain', async () => {
    const { page } = await verified(unanchored);
    expect(text(notice(page))).toContain(
      '✓ Hash chain intact, but no signed checkpoint covers it yet.',
    );
    expect(text(notice(page))).toContain(unanchored.message);
  });

  it('shows the critical notice with the reason, the first broken entry and the unchecked checkpoints', async () => {
    const { page } = await verified(tampered);
    expect(text(notice(page))).toContain('✗ Tampering detected.');
    expect(text(notice(page))).toContain(tampered.message);
  });

  it.each([
    [409, 'audit_busy', busy],
    [503, 'audit_unavailable', unavailable],
  ])('a refused Verify (%s) clears the last notice and says why', async (code, name, title) => {
    const { page, fixture } = await verified(anchored);
    button(page, 'Verify integrity').click();
    fixture.detectChanges();
    expect(notice(page)).toBeNull();
    backend
      .expectOne(`${path}/verify`)
      .flush({ status: code, code: name, title }, { status: code, statusText: 'Refused' });
    await alerted(fixture, title);
    expect(status(page, 'alert')).toBe(title);
    expect(notice(page)).toBeNull();
  });

  it('reports a new checkpoint in the StatusLine and shows it as the latest, without reading the entries again', async () => {
    const { page, fixture } = await loaded();
    button(page, 'Create signed checkpoint').click();
    fixture.detectChanges();
    expect(button(page, 'Verify integrity').disabled).toBe(true);
    backend
      .expectOne({ method: 'POST', url: `${path}/checkpoints` })
      .flush(
        { sequence: 57, createdAtUtc: '2026-11-13T11:59:59Z', signingKeyId: 'sk-1a2b3c4d' },
        { status: 201, statusText: 'Created' },
      );
    await fixture.whenStable();
    expect(status(page, 'status')).toBe(
      'Signed checkpoint created at sequence 57 with key sk-1a2b3c4d.',
    );
    expect(text(page.querySelector('.detail-grid dd'))).toBe(
      'sequence 57 · 2026-11-13 11:59 UTC · sk-1a2b3c4d',
    );
    expect(button(page, 'Verify integrity').disabled).toBe(false);
    backend.expectNone(path);
  });

  it.each([
    [409, 'nothing_to_checkpoint', 'There are no audit entries to checkpoint yet.'],
    [503, 'audit_unavailable', unavailable],
  ])('a refused checkpoint (%s) says why and keeps the latest one', async (code, name, title) => {
    const { page, fixture } = await loaded();
    button(page, 'Create signed checkpoint').click();
    backend
      .expectOne(`${path}/checkpoints`)
      .flush({ status: code, code: name, title }, { status: code, statusText: 'Refused' });
    await alerted(fixture, title);
    expect(status(page, 'alert')).toBe(title);
    expect(text(page.querySelector('.detail-grid dd'))).toContain('sequence 41');
  });

  it('a response that never arrives blocks further writes until the page is reloaded', async () => {
    const { page, fixture } = await loaded();
    button(page, 'Create signed checkpoint').click();
    backend.expectOne(`${path}/checkpoints`).error(new ProgressEvent('error'));
    await fixture.whenStable();
    expect(status(page, 'alert')).toBe(
      'The response was lost. Reload the page before trying again.',
    );
    expect(button(page, 'Create signed checkpoint').disabled).toBe(true);
  });

  it('a fresh Export saves the bundle under the server file name', async () => {
    const { page, fixture } = await loaded();
    button(page, 'Export signed bundle').click();
    backend
      .expectOne({ method: 'POST', url: `${path}/export` })
      .flush(new Blob(['{}'], { type: 'application/json' }), {
        headers: { 'Content-Disposition': disposition },
      });
    await fixture.whenStable();
    expect(saved).toEqual(['eclipsvault-audit-bundle-20261113-120000.json']);
    backend.expectNone(path);
  });

  it('a stale Export asks for a code once, then exports exactly once', async () => {
    const { page, fixture } = await loaded();
    button(page, 'Export signed bundle').click();
    backend
      .expectOne(`${path}/export`)
      .flush(
        problem(
          403,
          'step_up_required',
          "Confirm it's you before exporting the audit trail.",
          'code',
        ),
        {
          status: 403,
          statusText: 'Forbidden',
        },
      );
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.querySelector('app-step-up-dialog dialog[open]')).not.toBeNull();
    });
    expect(button(page, 'Export signed bundle').disabled).toBe(true);
    expect(saved).toEqual([]);
    const input = page.querySelector<HTMLInputElement>('app-step-up-dialog input')!;
    input.value = '123456';
    page
      .querySelector('app-step-up-dialog form')!
      .dispatchEvent(new Event('submit', { cancelable: true }));
    const proof = backend.expectOne('/api/ui/step-up');
    expect(proof.request.body).toEqual({ code: '123456' });
    proof.flush(null);
    backend
      .expectOne(`${path}/export`)
      .flush(new Blob(['{}']), { headers: { 'Content-Disposition': disposition } });
    await fixture.whenStable();
    expect(saved).toEqual(['eclipsvault-audit-bundle-20261113-120000.json']);
    expect(page.querySelector('dialog[open]')).toBeNull();
  });

  it('cancelling the proof exports nothing and returns focus to Export', async () => {
    const { page, fixture } = await loaded();
    const exporter = button(page, 'Export signed bundle');
    exporter.focus();
    exporter.click();
    backend
      .expectOne(`${path}/export`)
      .flush(
        problem(
          403,
          'step_up_required',
          "Confirm it's you before exporting the audit trail.",
          'code',
        ),
        {
          status: 403,
          statusText: 'Forbidden',
        },
      );
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.querySelector('app-step-up-dialog dialog[open]')).not.toBeNull();
    });
    button(page.querySelector('app-step-up-dialog')!, 'Cancel').click();
    await fixture.whenStable();
    expect(document.activeElement).toBe(exporter);
    expect(exporter.disabled).toBe(false);
    expect(saved).toEqual([]);
  });

  it('a stale Export without an authenticator signs out and returns to the Audit log', async () => {
    const { page, fixture } = await loaded();
    button(page, 'Export signed bundle').click();
    backend
      .expectOne(`${path}/export`)
      .flush(
        problem(
          403,
          'step_up_required',
          "Confirm it's you before exporting the audit trail.",
          'sign_in',
        ),
        {
          status: 403,
          statusText: 'Forbidden',
        },
      );
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.querySelector('app-step-up-dialog dialog[open]')).not.toBeNull();
    });
    button(page, 'Sign in again').click();
    backend.expectOne('/Account/Logout').flush('');
    await fixture.whenStable();
    expect(assign).toHaveBeenCalledWith('/Account/Login?ReturnUrl=%2FAudit');
  });

  it.each([
    [409, 'audit_busy', busy],
    [503, 'audit_unavailable', unavailable],
  ])('a refused Export (%s) says why and saves nothing', async (code, name, title) => {
    const { page, fixture } = await loaded();
    button(page, 'Export signed bundle').click();
    backend
      .expectOne(`${path}/export`)
      .flush(problem(code, name, title), { status: code, statusText: 'Refused' });
    await alerted(fixture, title);
    expect(status(page, 'alert')).toBe(title);
    expect(saved).toEqual([]);
  });
});
