import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Type } from '@angular/core';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { appConfig } from '../app.config';
import { PAGE_LOCATION } from '../shell/command-palette';
import type { components } from '../ui-api/schema.generated';
import { ServiceAccountsPage } from './service-accounts-page';
import { NewServiceAccountPage } from './new-service-account-page';
import { ServiceAccountPage } from './service-account-page';
import { ServiceAccountNotices } from './service-account-notices';

const id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const keyId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
const path = `/api/ui/service-accounts/${id}`;
const clearances = [
  { value: 1, label: 'Standard' },
  { value: 3, label: 'Secret' },
];
const key: components['schemas']['ServiceAccountKeyResponse'] = {
  keyId,
  prefix: 'evk_prefix',
  createdAtUtc: '2026-01-02T03:04:00Z',
  expiresAtUtc: null,
  lastUsedAtUtc: null,
  status: 'active',
  fullAccess: true,
  metadataOnly: false,
  clearanceCeilingLabel: null,
  projectScope: null,
  allowedRanges: [],
};
const details: components['schemas']['ServiceAccountDetailsResponse'] = {
  serviceAccountId: id,
  name: 'payments-ci',
  clearance: 3,
  clearanceLabel: 'Secret',
  project: 'PAYMENTS',
  enabled: true,
  createdAtUtc: '2026-01-02T03:04:00Z',
  ceilings: [
    { value: 1, label: 'Standard' },
    { value: 2, label: 'Elevated' },
  ],
  keys: [],
};
const oneTimeKey = 'evk_disposable-page-memory-only';
let backend: HttpTestingController;
const assign = vi.fn();
function button(page: ParentNode, name: string): HTMLButtonElement {
  const found = [...page.querySelectorAll('button')].find(
    (item) => item.textContent?.trim() === name,
  );
  if (!found) throw new Error(`No button named ${name}`);
  return found;
}
function field(page: ParentNode, id: string): HTMLInputElement {
  const found = page.querySelector<HTMLInputElement>(`#${id}`);
  if (!found) throw new Error(`No input named ${id}`);
  return found;
}
function fill(page: ParentNode, id: string, value: string): void {
  const input = field(page, id);
  input.value = value;
  input.dispatchEvent(new Event('input'));
}
function submit(page: ParentNode): void {
  const form = page.querySelector('form');
  if (!form) throw new Error('No form');
  form.dispatchEvent(new Event('submit', { cancelable: true }));
}
function render<T>(component: Type<T>, url = `/ServiceAccounts/Details/${id}`) {
  history.replaceState(null, '', url);
  TestBed.configureTestingModule({
    providers: [
      ...appConfig.providers,
      provideHttpClientTesting(),
      { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id })) } },
      { provide: PAGE_LOCATION, useValue: { assign } },
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  const fixture = TestBed.createComponent(component);
  fixture.detectChanges();
  const page: HTMLElement = fixture.nativeElement;
  document.body.append(page);
  page.querySelectorAll('dialog').forEach((dialog) => {
    dialog.showModal = () => dialog.setAttribute('open', '');
    dialog.close = () => dialog.removeAttribute('open');
  });
  return { fixture, page, navigate };
}
async function loadedCreation() {
  const rendered = render(NewServiceAccountPage, '/ServiceAccounts/Create');
  backend.expectOne('/api/ui/service-accounts/new').flush({ clearances });
  await rendered.fixture.whenStable();
  return rendered;
}
function enterProof(page: ParentNode, code: string): void {
  const proof = page.querySelector('app-step-up-dialog');
  const input = proof?.querySelector('input');
  if (!proof || !input) throw new Error('No authenticator code dialog');
  input.value = code;
  submit(proof);
}
async function loadedDetails(account = details) {
  const rendered = render(ServiceAccountPage);
  backend.expectOne(path).flush(account);
  await rendered.fixture.whenStable();
  return rendered;
}
async function reload(fixture: ComponentFixture<unknown>, account = details) {
  fixture.detectChanges();
  backend.expectOne(path).flush(account);
  await fixture.whenStable();
}
function blocked(proof: 'code' | 'sign_in' = 'code') {
  backend
    .expectOne(path + '/keys')
    .flush(
      { code: 'step_up_required', title: "Confirm it's you before granting access.", proof },
      { status: 403, statusText: 'Forbidden' },
    );
}
beforeEach(() => {
  assign.mockReset();
});
afterEach(() => {
  try {
    backend.verify();
  } finally {
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
  }
});

describe('Service accounts list', () => {
  it('shows loading then an error with retry and the empty state', async () => {
    const { page, fixture } = render(ServiceAccountsPage, '/ServiceAccounts');
    expect(page.textContent).toContain('Loading service accounts…');
    backend
      .expectOne('/api/ui/service-accounts')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(page.textContent).toContain('Could not load service accounts. Try again.');
    button(page, 'Try again').click();
    fixture.detectChanges();
    backend.expectOne('/api/ui/service-accounts').flush({ serviceAccounts: [] });
    await fixture.whenStable();
    expect(page.textContent).toContain('No service accounts yet.');
  });
  it('shows the saved list columns, status, active count and UTC date', async () => {
    const { page, fixture } = render(ServiceAccountsPage);
    backend
      .expectOne('/api/ui/service-accounts')
      .flush({ serviceAccounts: [{ ...details, activeKeys: 2, enabled: false }] });
    await fixture.whenStable();
    expect([...page.querySelectorAll('th')].map((cell) => cell.textContent)).toEqual([
      'Name',
      'Clearance',
      'Project',
      'Active keys',
      'Status',
      'Created (UTC)',
      '',
    ]);
    expect([...page.querySelectorAll('td')].map((cell) => cell.textContent?.trim())).toEqual([
      'payments-ci',
      'Secret',
      'PAYMENTS',
      '2',
      'Disabled',
      '2026-01-02',
      'Manage',
    ]);
    expect(page.querySelector('tbody a')?.getAttribute('href')).toBe(
      `/ServiceAccounts/Details/${id}`,
    );
  });
  it('announces deletion once after returning from Details', async () => {
    const { page, fixture } = render(ServiceAccountsPage);
    backend.expectOne('/api/ui/service-accounts').flush({ serviceAccounts: [] });
    await fixture.whenStable();
    fixture.destroy();
    TestBed.inject(ServiceAccountNotices).deleted = true;
    const next = TestBed.createComponent(ServiceAccountsPage);
    next.detectChanges();
    backend.expectOne('/api/ui/service-accounts').flush({ serviceAccounts: [] });
    await next.whenStable();
    expect(next.nativeElement.textContent).toContain('Service account and its keys deleted.');
    expect(TestBed.inject(ServiceAccountNotices).deleted).toBe(false);
    expect(page.textContent).not.toContain(oneTimeKey);
  });
});

describe('New service account', () => {
  it('retries metadata and shows server-supplied clearance choices', async () => {
    const { page, fixture } = render(NewServiceAccountPage, '/ServiceAccounts/Create');
    expect(page.textContent).toContain('Loading new service account…');
    backend
      .expectOne('/api/ui/service-accounts/new')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(page.textContent).toContain('Could not load new service account. Try again.');
    button(page, 'Try again').click();
    fixture.detectChanges();
    backend.expectOne('/api/ui/service-accounts/new').flush({ clearances });
    await fixture.whenStable();
    expect(
      [...page.querySelectorAll('option')].map((option) => option.textContent?.trim()),
    ).toEqual(['Standard', 'Secret']);
    expect(page.textContent).toContain('After creating it, issue an API key on the next screen.');
  });
  it('posts the entered values and opens the saved account with an id-only notice', async () => {
    const { page, fixture, navigate } = render(NewServiceAccountPage, '/ServiceAccounts/Create');
    backend.expectOne('/api/ui/service-accounts/new').flush({ clearances });
    await fixture.whenStable();
    fill(page, 'name', ' payments-ci ');
    fill(page, 'project', ' straße ');
    submit(page);
    const request = backend.expectOne('/api/ui/service-accounts');
    expect(request.request.body).toEqual({
      name: ' payments-ci ',
      clearance: 1,
      project: ' straße ',
    });
    request.flush({ serviceAccountId: id });
    await fixture.whenStable();
    expect(navigate).toHaveBeenCalledWith(`/ServiceAccounts/Details/${id}`);
    expect(TestBed.inject(ServiceAccountNotices).createdId).toBe(id);
  });
  it('shows the name field refusal in the StatusLine', async () => {
    const { page, fixture } = await loadedCreation();
    submit(page);
    backend.expectOne('/api/ui/service-accounts').flush(
      {
        code: 'validation_failed',
        title: 'Choose a name.',
        errors: { name: ['Choose a name.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();
    expect(page.querySelector('.field-error')?.textContent).toBe('Choose a name.');
    expect(page.querySelector('app-status-line [role="alert"]')?.textContent).toContain(
      'Choose a name.',
    );
  });
  it('shows the taken-name conflict in the StatusLine', async () => {
    const { page, fixture } = await loadedCreation();
    submit(page);
    backend
      .expectOne('/api/ui/service-accounts')
      .flush(
        { code: 'name_taken', title: 'That name is taken.' },
        { status: 409, statusText: 'Conflict' },
      );
    await fixture.whenStable();
    expect(page.querySelector('app-status-line [role="alert"]')?.textContent).toContain(
      'That name is taken.',
    );
  });
  it('shows the create audit failure without navigating to a saved account', async () => {
    const { page, fixture, navigate } = await loadedCreation();
    submit(page);
    backend
      .expectOne('/api/ui/service-accounts')
      .flush(
        { code: 'audit_unavailable', title: 'The audit trail is unavailable.' },
        { status: 503, statusText: 'Unavailable' },
      );
    await fixture.whenStable();
    expect(page.querySelector('app-status-line [role="alert"]')?.textContent).toContain(
      'The audit trail is unavailable.',
    );
    expect(navigate).not.toHaveBeenCalled();
  });
});

describe('Service account Details', () => {
  it('retries a failed read and shows a missing account safely', async () => {
    const { page, fixture } = render(ServiceAccountPage);
    expect(page.textContent).toContain('Loading service account…');
    backend
      .expectOne(path)
      .flush(
        { code: 'service_account_not_found', title: 'Service account not found.' },
        { status: 404, statusText: 'Not Found' },
      );
    await fixture.whenStable();
    expect(page.textContent).toContain('Service account not found.');
    button(page, 'Try again').click();
    fixture.detectChanges();
    backend.expectOne(path).flush(details);
    await fixture.whenStable();
    expect(page.querySelector('h1')?.textContent).toBe('payments-ci');
    expect(page.textContent).toContain('No keys yet. Issue one so this account can authenticate.');
  });
  it('shows the saved create notice and consumes it without plaintext', async () => {
    const rendered = render(ServiceAccountPage);
    TestBed.inject(ServiceAccountNotices).createdId = id;
    backend.expectOne(path).flush(details);
    await rendered.fixture.whenStable();
    expect(rendered.page.querySelector('app-status-line [role="status"]')?.textContent).toContain(
      "Service account 'payments-ci' created. Issue an API key to let it authenticate.",
    );
    expect(TestBed.inject(ServiceAccountNotices).createdId).toBeNull();
  });
  it('shows server statuses, scope badges, UTC values and only active revoke controls', async () => {
    const { page } = await loadedDetails({
      ...details,
      enabled: false,
      keys: [
        key,
        {
          ...key,
          keyId: 'expired',
          status: 'expired',
          fullAccess: false,
          metadataOnly: true,
          clearanceCeilingLabel: 'Standard',
          projectScope: 'OPS',
          allowedRanges: ['10.8.0.0/24'],
          expiresAtUtc: '2026-02-03T00:00:00Z',
          lastUsedAtUtc: '2026-01-04T05:06:00Z',
        },
        { ...key, keyId: 'revoked', status: 'revoked' },
      ],
    });
    expect(page.textContent).toContain('full access');
    expect(page.textContent).toContain('metadata-only');
    expect(page.textContent).toContain('at most Standard');
    expect(page.textContent).toContain('1 network');
    expect(page.textContent).toContain('2026-01-04 05:06');
    expect(page.textContent).toContain('2026-02-03');
    expect(page.textContent).toContain('never');
    expect(
      [...page.querySelectorAll('tbody tr')].map((row) =>
        row.querySelectorAll('td')[5]?.textContent?.trim(),
      ),
    ).toEqual(['Active', 'Expired', 'Revoked']);
    expect(
      [...page.querySelectorAll('button')].filter((item) => item.textContent?.trim() === 'Revoke'),
    ).toHaveLength(1);
    expect(button(page, 'Enable').textContent?.trim()).toBe('Enable');
  });
  it('posts all scopes without normalizing them, then displays and copies the visible one-time key', async () => {
    const copy = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: copy },
    });
    const { page, fixture } = await loadedDetails();
    fill(page, 'expiry', '7');
    fill(page, 'projectScope', ' straße ');
    fill(page, 'allowedRanges', '10.8.0.5, 10.8.0.5');
    const ceiling = page.querySelector<HTMLSelectElement>('#ceiling');
    if (!ceiling) throw new Error('No ceiling');
    ceiling.value = '1';
    ceiling.dispatchEvent(new Event('change'));
    const metadata = page.querySelector<HTMLInputElement>('input[type="checkbox"]');
    if (!metadata) throw new Error('No metadata');
    metadata.click();
    submit(page);
    const request = backend.expectOne(path + '/keys');
    expect(request.request.body).toEqual({
      expiresInDays: 7,
      clearanceCeiling: 1,
      projectScope: ' straße ',
      metadataOnly: true,
      allowedRanges: '10.8.0.5, 10.8.0.5',
    });
    request.flush({ keyId, prefix: 'evk_prefix', apiKey: oneTimeKey });
    await reload(fixture);
    expect(page.querySelector('#new-token')?.textContent).toBe(oneTimeKey);
    expect(page.textContent).toContain('API key issued. Copy it now — it will not be shown again.');
    button(page, 'Copy').click();
    await fixture.whenStable();
    expect(copy).toHaveBeenCalledWith(oneTimeKey);
    expect(document.cookie.includes(oneTimeKey)).toBe(false);
    expect(JSON.stringify(localStorage).includes(oneTimeKey)).toBe(false);
    expect(JSON.stringify(sessionStorage).includes(oneTimeKey)).toBe(false);
    expect(location.href.includes(oneTimeKey)).toBe(false);
    expect(JSON.stringify(TestBed.inject(ServiceAccountNotices)).includes(oneTimeKey)).toBe(false);
  });
  it.each([
    { event: 'pagehide', target: window },
    { event: 'vault-session-ended', target: document },
  ])(
    'clears the key synchronously on $event and blocks another write',
    async ({ event, target }) => {
      const { page, fixture } = await loadedDetails();
      submit(page);
      backend.expectOne(path + '/keys').flush({ keyId, prefix: 'evk_prefix', apiKey: oneTimeKey });
      await reload(fixture);
      expect(page.querySelector('#new-token')?.textContent).toBe(oneTimeKey);
      target.dispatchEvent(new Event(event));
      expect(page.querySelector('#new-token')?.textContent).toBe('');
      await fixture.whenStable();
      expect(page.textContent).not.toContain(oneTimeKey);
      expect(button(page, 'Issue new key').matches(':disabled')).toBe(true);
    },
  );
  it('clears on component departure and a reloaded Details never recovers a key', async () => {
    const { fixture, page } = await loadedDetails();
    submit(page);
    backend.expectOne(path + '/keys').flush({ keyId, prefix: 'evk_prefix', apiKey: oneTimeKey });
    await reload(fixture);
    fixture.destroy();
    expect(page.textContent).not.toContain(oneTimeKey);
    const next = TestBed.createComponent(ServiceAccountPage);
    next.detectChanges();
    backend.expectOne(path).flush({ ...details, keys: [key] });
    await next.whenStable();
    expect(next.nativeElement.textContent).not.toContain(oneTimeKey);
    expect(next.nativeElement.textContent).toContain('evk_prefix');
  });
  it('shows the expiry field refusal beside the input', async () => {
    const { page, fixture } = await loadedDetails();
    submit(page);
    backend.expectOne(path + '/keys').flush(
      {
        code: 'validation_failed',
        title: 'Choose an expiry.',
        errors: { expiresInDays: ['Choose an expiry.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();
    expect(page.querySelector('.field-error')?.textContent).toBe('Choose an expiry.');
  });
  it('blocks another issuance after a lost response', async () => {
    const { page, fixture } = await loadedDetails();
    submit(page);
    backend.expectOne(path + '/keys').error(new ProgressEvent('error'));
    await fixture.whenStable();
    expect(page.textContent).toContain(
      'The response was lost. Reload the page before trying again.',
    );
    expect(button(page, 'Issue new key').matches(':disabled')).toBe(true);
  });
  it('requires the exact Revoke confirmation and cancellation leaves the key active', async () => {
    const { page, fixture } = await loadedDetails({ ...details, keys: [key] });
    button(page, 'Revoke').click();
    await fixture.whenStable();
    expect(page.querySelector('[role="alertdialog"]')?.textContent).toContain(
      'Revoke key evk_prefix…? Any caller using it will stop working immediately.',
    );
    button(page.querySelector('[role="alertdialog"]')!, 'Cancel').click();
    backend.expectNone(path + '/keys/' + keyId);
    button(page, 'Revoke').click();
    await fixture.whenStable();
    button(page, 'Continue').click();
    backend.expectOne(path + '/keys/' + keyId).flush(null);
    await reload(fixture, { ...details, keys: [{ ...key, status: 'revoked' }] });
    expect(page.textContent).toContain('API key revoked.');
    expect(page.textContent).toContain('Revoked');
  });
  it('requires the exact Delete confirmation and returns to the list with a safe notice', async () => {
    const { page, fixture, navigate } = await loadedDetails();
    button(page, 'Delete').click();
    await fixture.whenStable();
    expect(page.querySelector('[role="alertdialog"]')?.textContent).toContain(
      "Delete service account 'payments-ci' and all its API keys? This cannot be undone.",
    );
    button(page, 'Continue').click();
    backend.expectOne(path).flush(null);
    await fixture.whenStable();
    expect(navigate).toHaveBeenCalledWith('/ServiceAccounts');
    expect(TestBed.inject(ServiceAccountNotices).deleted).toBe(true);
  });
  it('disables without confirmation and preserves the Legacy notice', async () => {
    const { page, fixture } = await loadedDetails();
    button(page, 'Disable').click();
    backend.expectOne(path + '/disable').flush(null);
    await reload(fixture, { ...details, enabled: false });
    expect(page.textContent).toContain(
      'Service account disabled — all of its keys are now rejected.',
    );
    expect(button(page, 'Enable').matches(':disabled')).toBe(false);
    expect(page.querySelector('dialog[open]')).toBeNull();
  });
  it('enables without confirmation and preserves the Legacy notice', async () => {
    const { page, fixture } = await loadedDetails({ ...details, enabled: false });
    button(page, 'Enable').click();
    backend.expectOne(path + '/enable').flush(null);
    await reload(fixture);
    expect(page.textContent).toContain('Service account enabled.');
    expect(button(page, 'Disable').matches(':disabled')).toBe(false);
    expect(page.querySelector('dialog[open]')).toBeNull();
  });
  it.each([
    { action: 'Issue new key', enabled: true, endpoint: '/keys' },
    { action: 'Enable', enabled: false, endpoint: '/enable' },
  ])(
    'cancelling $action proof restores its keyboard focus without retrying',
    async ({ action, enabled, endpoint }) => {
      const { page, fixture } = await loadedDetails({ ...details, enabled });
      const opener = button(page, action);
      opener.focus();
      opener.click();
      backend
        .expectOne(path + endpoint)
        .flush(
          { code: 'step_up_required', proof: 'code' },
          { status: 403, statusText: 'Forbidden' },
        );
      await fixture.whenStable();
      button(page.querySelector('app-step-up-dialog')!, 'Cancel').click();
      await fixture.whenStable();
      expect(document.activeElement).toBe(opener);
      expect(page.querySelector('dialog[open]')).toBeNull();
    },
  );
  it('retries issuance exactly once after shared code proof', async () => {
    const { page, fixture } = await loadedDetails();
    submit(page);
    blocked();
    await fixture.whenStable();
    enterProof(page, '123456');
    backend.expectOne('/api/ui/step-up').flush(null);
    backend
      .expectOne(path + '/keys')
      .flush(
        { code: 'step_up_required', title: 'Proof expired.', proof: 'code' },
        { status: 403, statusText: 'Forbidden' },
      );
    await fixture.whenStable();
    expect(page.textContent).toContain('Proof expired.');
    expect(page.querySelector('dialog[open]')).toBeNull();
  });
  it('ends the stale session before following the sign-in fallback with its return URL', async () => {
    const { page, fixture } = await loadedDetails();
    submit(page);
    blocked('sign_in');
    await fixture.whenStable();
    button(page, 'Sign in again').click();
    backend.expectOne('/Account/Logout').flush('');
    await fixture.whenStable();
    expect(assign).toHaveBeenCalledWith(
      '/Account/Login?ReturnUrl=%2FServiceAccounts%2FDetails%2Faaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
    );
  });
  it('shows audit failure without claiming the account was disabled', async () => {
    const { page, fixture } = await loadedDetails();
    button(page, 'Disable').click();
    backend
      .expectOne(path + '/disable')
      .flush(
        { code: 'audit_unavailable', title: 'The audit trail is unavailable.' },
        { status: 503, statusText: 'Unavailable' },
      );
    await fixture.whenStable();
    expect(page.querySelector('app-status-line [role="alert"]')?.textContent).toContain(
      'The audit trail is unavailable.',
    );
    expect(button(page, 'Disable').textContent?.trim()).toBe('Disable');
  });
});
