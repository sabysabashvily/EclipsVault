import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { PAGE_LOCATION } from '../shell/command-palette';
import type { components } from '../ui-api/schema.generated';
import { DynamicSecretsPage } from './dynamic-secrets-page';

type DynamicSecrets = components['schemas']['DynamicSecretsResponse'];

const path = '/api/ui/dynamic-secrets';
const issuePath = `${path}/issue`;
const internalId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const secretId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const leaseId = 'cccccccc-cccc-cccc-cccc-cccccccccccc';
const revokePath = `${path}/leases/${leaseId}/revoke`;
const internalRole: components['schemas']['DynamicRoleResponse'] = {
  roleId: internalId,
  name: 'reporting_reader',
  description: 'Read-only access to the reporting database.',
  project: 'GLOBAL',
  environment: 'Development',
  sensitivity: 1,
  sensitivityLabel: 'Internal',
  backend: 'SqlServer',
  defaultTtlMinutes: 15,
  maxTtlMinutes: 60,
};
const secretRole: components['schemas']['DynamicRoleResponse'] = {
  ...internalRole,
  roleId: secretId,
  name: 'billing_admin',
  description: 'Billing database owner.',
  sensitivity: 3,
  sensitivityLabel: 'Secret',
  defaultTtlMinutes: 5,
  maxTtlMinutes: 30,
};
const activeLease: components['schemas']['DynamicLeaseResponse'] = {
  leaseId,
  roleName: 'reporting_reader',
  credentialIdentity: 'ev_reporting_reader_k3mq8x2ptw',
  holder: 'alice',
  issuedAtUtc: '2026-11-17T09:00:00Z',
  expiresAtUtc: '2026-11-17T09:15:00Z',
  closedAtUtc: null,
  status: 'Active',
  revocationNote: null,
};
const own: DynamicSecrets = {
  roles: [internalRole, secretRole],
  leaseScope: 'own',
  leases: [activeLease],
  licence: { licensed: true, evaluationDaysRemaining: null },
};
const issued: components['schemas']['IssuedDynamicCredentialResponse'] = {
  leaseId,
  roleName: 'reporting_reader',
  username: 'ev_reporting_reader_k3mq8x2ptw',
  password: 'Q7rT2mXa9LkP4vN8sD3fG6hJ1wZ5cB0yE2uR7iOp',
  expiresAtUtc: '2026-11-17T09:15:00Z',
};
const confirmation = "Hand back 'ev_reporting_reader_k3mq8x2ptw'? It stops working immediately.";
let backend: HttpTestingController;
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
function rowOf(page: ParentNode, role: string): HTMLTableRowElement {
  const found = [...page.querySelectorAll('tr')].find((row) =>
    text(row.querySelector('td strong')).startsWith(role),
  );
  if (!found) throw new Error(`No role row ${role}`);
  return found;
}
function generate(page: ParentNode, role: string, minutes = ''): void {
  const row = rowOf(page, role);
  row.querySelector('input')!.value = minutes;
  row.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
}
function enterProof(page: ParentNode, code: string): void {
  const dialog = page.querySelector('app-step-up-dialog')!;
  dialog.querySelector('input')!.value = code;
  dialog.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
}
function status(page: ParentNode, role: 'status' | 'alert'): string {
  return text(page.querySelector(`app-status-line [role="${role}"]`));
}
function credential(page: ParentNode): string[][] {
  return [...page.querySelectorAll('.panel.highlight tr')].map((row) =>
    [...row.children].map((cell) => text(cell)),
  );
}
function render() {
  history.replaceState(null, '', '/DynamicSecrets');
  TestBed.configureTestingModule({
    providers: [
      ...appConfig.providers,
      provideHttpClientTesting(),
      { provide: PAGE_LOCATION, useValue: { assign } },
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(DynamicSecretsPage);
  fixture.detectChanges();
  const page: HTMLElement = fixture.nativeElement;
  document.body.append(page);
  page.querySelectorAll('dialog').forEach((dialog) => {
    dialog.showModal = () => dialog.setAttribute('open', '');
    dialog.close = () => dialog.removeAttribute('open');
  });
  return { fixture, page };
}
async function loaded(answer: DynamicSecrets = own) {
  const rendered = render();
  backend.expectOne(path).flush(answer);
  await rendered.fixture.whenStable();
  return rendered;
}
async function reload(fixture: ComponentFixture<unknown>, answer: DynamicSecrets = own) {
  fixture.detectChanges();
  backend.expectOne(path).flush(answer);
  await fixture.whenStable();
}
async function issueInternal(page: HTMLElement, fixture: ComponentFixture<unknown>) {
  generate(page, 'reporting_reader');
  backend.expectOne(issuePath).flush(issued);
  await reload(fixture);
}
function refuse(url: string, code: string, title: string, status: number): void {
  backend.expectOne(url).flush({ code, title }, { status, statusText: 'Refused' });
}
async function confirmRevoke(page: HTMLElement, fixture: ComponentFixture<unknown>) {
  button(page, 'Revoke').click();
  await fixture.whenStable();
  button(page, 'Continue').click();
  fixture.detectChanges();
  return backend.expectOne(revokePath);
}

beforeEach(() => {
  assign.mockReset();
});

afterEach(() => {
  try {
    backend.verify();
  } finally {
    TestBed.resetTestingModule();
    document.body.replaceChildren();
  }
});

describe('Dynamic secrets', () => {
  it('shows loading until the page arrives', async () => {
    const { page, fixture } = render();
    expect(page.textContent).toContain('Loading dynamic secrets…');
    backend.expectOne(path).flush(own);
    await fixture.whenStable();
    expect(page.textContent).not.toContain('Loading dynamic secrets…');
  });

  it('shows a failed load with Try again, which loads the page', async () => {
    const { page, fixture } = render();
    backend.expectOne(path).flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(page.textContent).toContain('Could not load dynamic secrets. Try again.');
    button(page, 'Try again').click();
    await reload(fixture);
    expect(page.textContent).not.toContain('Could not load dynamic secrets.');
  });

  it('copies the Legacy heading, roles and the holder’s own leases word for word', async () => {
    const { page } = await loaded();
    expect(text(page.querySelector('.page-sub'))).toBe(
      'Credentials the vault creates on a real backend when you ask, and destroys when the lease elapses. Nothing is stored — the value is shown once and cannot be recovered.',
    );
    expect([...page.querySelectorAll('section h2')].map((heading) => text(heading))).toEqual([
      'Roles you can issue',
      'Your leases',
    ]);
    expect(
      [...page.querySelectorAll('section')[0]!.querySelectorAll('tbody tr')].map((row) =>
        [...row.querySelectorAll('td')].slice(0, 6).map((cell) => text(cell)),
      ),
    ).toEqual([
      [
        'reporting_readerRead-only access to the reporting database.',
        'SqlServer',
        'GLOBAL',
        'Development',
        'Internal',
        '15 min (max 60)',
      ],
      [
        'billing_adminBilling database owner.',
        'SqlServer',
        'GLOBAL',
        'Development',
        'Secret',
        '5 min (max 30)',
      ],
    ]);
    const lease = page.querySelectorAll('section')[1]!;
    expect([...lease.querySelectorAll('th')].map((cell) => text(cell))).toEqual([
      'Role',
      'Username',
      'Issued (UTC)',
      'Lease ends (UTC)',
      'Status',
      '',
    ]);
    expect([...lease.querySelectorAll('tbody td')].map((cell) => text(cell))).toEqual([
      'reporting_reader',
      'ev_reporting_reader_k3mq8x2ptw',
      '2026-11-17 09:00:00Z',
      '2026-11-17 09:15:00Z',
      'Active',
      'Revoke',
    ]);
    const minutes = rowOf(page, 'reporting_reader').querySelector('input')!;
    expect([
      minutes.min,
      minutes.max,
      minutes.placeholder,
      minutes.getAttribute('aria-label'),
    ]).toEqual(['1', '60', '15', 'Lease minutes for reporting_reader']);
  });

  it('shows an administrator everyone’s leases with who took each', async () => {
    const { page } = await loaded({ ...own, leaseScope: 'everyone' });
    const lease = page.querySelectorAll('section')[1]!;
    expect(text(lease.querySelector('h2'))).toBe('All leases');
    expect([...lease.querySelectorAll('th')].map((cell) => text(cell))).toContain('Taken by');
    expect(text(lease.querySelectorAll('tbody td')[2])).toBe('alice');
  });

  it('shows the Legacy empty states', async () => {
    const { page } = await loaded({ ...own, roles: [], leases: [] });
    expect(page.textContent?.replace(/\s+/g, ' ')).toContain(
      'No dynamic-secret roles are available to you. Roles are gated by the same clearance, project, network, and time rules as stored secrets.',
    );
    expect(page.textContent).toContain('No credentials issued yet.');
  });

  it.each([
    [
      null,
      'Dynamic secrets are a licensed feature. Issuing your first credential starts a free 30-day evaluation — no licence key, no registration. After that, issuing new credentials needs a licence; existing leases stay listed and revocable either way.',
    ],
    [
      1,
      'Evaluating dynamic secrets — 1 day left. When the evaluation ends, issuing new credentials needs a licence. Existing leases stay listed and revocable.',
    ],
    [
      12,
      'Evaluating dynamic secrets — 12 days left. When the evaluation ends, issuing new credentials needs a licence. Existing leases stay listed and revocable.',
    ],
    [
      0,
      'The evaluation period for dynamic secrets has ended. Issuing new credentials needs a licence — existing leases are unaffected and can still be revoked.',
    ],
  ])('shows the unlicensed note for %s evaluation days left', async (days, note) => {
    const { page } = await loaded({
      ...own,
      licence: { licensed: false, evaluationDaysRemaining: days },
    });
    expect(text(page.querySelector('section .muted'))).toBe(note);
  });

  it.each([
    ['licensed', { licensed: true, evaluationDaysRemaining: null }],
    ['unreadable', null],
  ] as const)('shows no licence note when the vault is %s', async (_, licence) => {
    const { page } = await loaded({ ...own, licence });
    expect(page.textContent).not.toContain('licence');
  });

  it('shows each lease status, with the fixed note of a failed revocation', async () => {
    const { page } = await loaded({
      ...own,
      leases: [
        { ...activeLease, leaseId: '1', status: 'Expired' },
        { ...activeLease, leaseId: '2', status: 'Revoked' },
        {
          ...activeLease,
          leaseId: '3',
          status: 'RevocationFailed',
          revocationNote: 'The backend refused to destroy the credential; it may still work.',
        },
      ],
    });
    expect(
      [...page.querySelectorAll('section')[1]!.querySelectorAll('tbody tr')].map((row) =>
        [...row.querySelectorAll('td')[4]!.children].map((part) => text(part)),
      ),
    ).toEqual([
      ['Expired'],
      ['Revoked'],
      ['Revocation failed', 'The backend refused to destroy the credential; it may still work.'],
    ]);
    expect(page.querySelectorAll('section')[1]!.querySelectorAll('button')).toHaveLength(0);
  });

  it('issues with the role’s default lease and shows the credential once, focused, then reloads', async () => {
    const { page, fixture } = await loaded();
    generate(page, 'reporting_reader');
    const request = backend.expectOne(issuePath);
    expect(request.request.body).toEqual({ roleId: internalId, ttlMinutes: null });
    request.flush(issued);
    await reload(fixture);
    expect(text(page.querySelector('.panel.highlight h2'))).toBe(
      'Your new credential — shown once',
    );
    expect(text(page.querySelector('.panel.highlight p'))).toBe(
      'The vault kept no copy of this password. Close or reload this page and it is gone for good; you would need to issue a new one. It stops working at 2026-11-17 09:15:00Z, whether or not you are still using it.',
    );
    expect(credential(page)).toEqual([
      ['Role', 'reporting_reader'],
      ['Username', 'ev_reporting_reader_k3mq8x2ptw'],
      ['Password', 'Q7rT2mXa9LkP4vN8sD3fG6hJ1wZ5cB0yE2uR7iOp'],
      ['Lease ends (UTC)', '2026-11-17 09:15:00Z'],
    ]);
    expect(document.activeElement).toBe(page.querySelector('.panel.highlight'));
    expect(status(page, 'status')).toBe('');
  });

  it('sends the lease length typed for the role', async () => {
    const { page, fixture } = await loaded();
    generate(page, 'reporting_reader', '45');
    const request = backend.expectOne(issuePath);
    expect(request.request.body).toEqual({ roleId: internalId, ttlMinutes: 45 });
    request.flush(issued);
    await reload(fixture);
  });

  it('asks for a code once for a Secret role, then retries the issue exactly once', async () => {
    const { page, fixture } = await loaded();
    generate(page, 'billing_admin');
    backend.expectOne(issuePath).flush(
      {
        code: 'step_up_required',
        title: "Confirm it's you before issuing 'billing_admin'.",
        proof: 'code',
      },
      { status: 403, statusText: 'Forbidden' },
    );
    await reload(fixture);
    expect(page.querySelector('app-step-up-dialog dialog[open]')).not.toBeNull();
    expect(button(rowOf(page, 'billing_admin'), 'Generate').disabled).toBe(true);
    expect(credential(page)).toEqual([]);
    enterProof(page, '123456');
    const proof = backend.expectOne('/api/ui/step-up');
    expect(proof.request.body).toEqual({ code: '123456' });
    proof.flush(null);
    const retry = backend.expectOne(issuePath);
    expect(retry.request.body).toEqual({ roleId: secretId, ttlMinutes: null });
    retry.flush({ ...issued, roleName: 'billing_admin' });
    await reload(fixture);
    expect(credential(page)[0]).toEqual(['Role', 'billing_admin']);
  });

  it('cancelling the proof issues nothing, restores focus and keeps the page usable', async () => {
    const { page, fixture } = await loaded();
    const opener = button(rowOf(page, 'billing_admin'), 'Generate');
    opener.focus();
    generate(page, 'billing_admin');
    backend
      .expectOne(issuePath)
      .flush(
        { code: 'step_up_required', title: 'Confirm.', proof: 'code' },
        { status: 403, statusText: 'Forbidden' },
      );
    await reload(fixture);
    button(page.querySelector('app-step-up-dialog')!, 'Cancel').click();
    await fixture.whenStable();
    expect(document.activeElement).toBe(opener);
    expect(page.querySelector('dialog[open]')).toBeNull();
    expect(credential(page)).toEqual([]);
    expect(opener.disabled).toBe(false);
  });

  it('ends the stale session before following the sign-in fallback back to Dynamic secrets', async () => {
    const { page, fixture } = await loaded();
    generate(page, 'billing_admin');
    backend
      .expectOne(issuePath)
      .flush(
        { code: 'step_up_required', title: 'Confirm.', proof: 'sign_in' },
        { status: 403, statusText: 'Forbidden' },
      );
    await reload(fixture);
    button(page, 'Sign in again').click();
    backend.expectOne('/api/ui/auth/sign-out').flush('');
    await fixture.whenStable();
    expect(assign).toHaveBeenCalledWith('/Account/Login?ReturnUrl=%2FDynamicSecrets');
  });

  it.each([
    ['access_denied', 403, "Your clearance and project do not permit issuing 'reporting_reader'."],
    ['role_not_found', 404, 'That role no longer exists.'],
    ['role_disabled', 409, "The role 'reporting_reader' is disabled and cannot issue credentials."],
    [
      'premium_not_licensed',
      402,
      'The 30-day evaluation period for dynamic secrets has ended. Issuing new credentials needs a licence — existing leases are unaffected and can still be revoked.',
    ],
    [
      'mint_refused',
      502,
      "The backend refused to mint a credential for 'reporting_reader'. Nothing was issued.",
    ],
    [
      'mint_incomplete',
      502,
      'The backend failed part-way through, and the vault could not confirm that the login it started to create was removed. No password was shown or kept. An administrator must check the target.',
    ],
    ['audit_unavailable', 503, 'The audit trail is unavailable, so the vault refused the request.'],
    ['validation_failed', 400, 'Enter a lease length between 1 and 1440 minutes.'],
  ])(
    'reports a refused issue (%s) in the status line and shows no credential',
    async (code, httpStatus, title) => {
      const { page, fixture } = await loaded();
      generate(page, 'reporting_reader');
      refuse(issuePath, code, title, httpStatus);
      await reload(fixture);
      expect(status(page, 'alert')).toBe(title);
      expect(credential(page)).toEqual([]);
      expect(button(rowOf(page, 'reporting_reader'), 'Generate').disabled).toBe(false);
    },
  );

  it('forgets the credential when the next issue starts', async () => {
    const { page, fixture } = await loaded();
    await issueInternal(page, fixture);
    generate(page, 'reporting_reader');
    fixture.detectChanges();
    expect(credential(page)).toEqual([]);
    refuse(issuePath, 'mint_refused', 'Nothing was issued.', 502);
    await reload(fixture);
    expect(credential(page)).toEqual([]);
  });

  it('forgets the credential on pagehide and refuses another write', async () => {
    const { page, fixture } = await loaded();
    await issueInternal(page, fixture);
    window.dispatchEvent(new Event('pagehide'));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(credential(page)).toEqual([]);
    expect(page.textContent).not.toContain(issued.password);
    expect(status(page, 'alert')).toBe('Reload the page or sign in before making another change.');
    expect(button(rowOf(page, 'reporting_reader'), 'Generate').disabled).toBe(true);
  });

  it('forgets the credential when the session ends and refuses another write', async () => {
    const { page, fixture } = await loaded();
    await issueInternal(page, fixture);
    document.dispatchEvent(new Event('vault-session-ended'));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(credential(page)).toEqual([]);
    expect(page.textContent).not.toContain(issued.password);
    expect(status(page, 'alert')).toBe('Reload the page or sign in before making another change.');
    expect(button(rowOf(page, 'reporting_reader'), 'Generate').disabled).toBe(true);
  });

  it('forgets the credential when the page is left and opened again', async () => {
    const { page, fixture } = await loaded();
    await issueInternal(page, fixture);
    fixture.destroy();
    page.remove();
    const returned = TestBed.createComponent(DynamicSecretsPage);
    returned.detectChanges();
    await reload(returned);
    expect(credential(returned.nativeElement)).toEqual([]);
    expect(returned.nativeElement.textContent).not.toContain(issued.password);
  });

  it('asks before revoking with the exact Legacy confirmation, and Cancel sends nothing', async () => {
    const { page, fixture } = await loaded();
    const revoke = button(page, 'Revoke');
    revoke.focus();
    revoke.click();
    await fixture.whenStable();
    expect(text(page.querySelector('[role="alertdialog"][open] p'))).toBe(confirmation);
    button(page, 'Cancel').click();
    await fixture.whenStable();
    backend.expectNone(revokePath);
    expect(document.activeElement).toBe(revoke);
  });

  it('says a revoked credential no longer works, then reloads', async () => {
    const { page, fixture } = await loaded();
    (await confirmRevoke(page, fixture)).flush({ status: 'Revoked', revocationNote: null });
    await reload(fixture, { ...own, leases: [{ ...activeLease, status: 'Revoked' }] });
    expect(status(page, 'status')).toBe('Credential handed back — it no longer works.');
    expect(page.querySelectorAll('section')[1]!.querySelectorAll('button')).toHaveLength(0);
  });

  it.each([
    [
      'The backend refused to destroy the credential; it may still work.',
      'The backend refused to destroy the credential; it may still work.',
    ],
    [
      'The role that minted this credential no longer exists, so it could not be destroyed automatically.',
      'The role that minted this credential no longer exists, so it could not be destroyed automatically.',
    ],
    [null, 'The backend refused to destroy the credential; it may still work.'],
  ])(
    'says truthfully that a refused revoke did not destroy the credential (%s)',
    async (note, shown) => {
      const { page, fixture } = await loaded();
      (await confirmRevoke(page, fixture)).flush({
        status: 'RevocationFailed',
        revocationNote: note,
      });
      await reload(fixture);
      expect(status(page, 'alert')).toBe(shown);
      expect(status(page, 'status')).toBe('');
    },
  );

  it.each([
    ['lease_not_found', 404, 'That lease is not active, or is not yours to revoke.'],
    [
      'revoked_unrecorded',
      503,
      "The credential was destroyed, but the vault couldn't record it in the audit trail. Its lease shows Active until it expires.",
    ],
  ])('reports a refused revoke (%s) in the status line', async (code, httpStatus, title) => {
    const { page, fixture } = await loaded();
    (await confirmRevoke(page, fixture)).flush(
      { code, title },
      { status: httpStatus, statusText: 'Refused' },
    );
    await reload(fixture);
    expect(status(page, 'alert')).toBe(title);
  });
});
