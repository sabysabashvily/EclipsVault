import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import { PAGE_LOCATION } from '../shell/command-palette';
import type { components } from '../ui-api/schema.generated';
import { NetworksPage } from './networks-page';

const path = '/api/ui/networks';
const networkId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const empty: components['schemas']['NetworksResponse'] = {
  currentAddress: '10.8.0.5',
  currentAddressTrusted: true,
  configuredRanges: ['127.0.0.1/32', '10.0.0.0/8'],
  trustedNetworks: [],
  blocks: [],
};
const listed: components['schemas']['NetworksResponse'] = {
  currentAddress: '198.51.100.7',
  currentAddressTrusted: false,
  configuredRanges: ['10.0.0.0/8'],
  trustedNetworks: [
    {
      networkId,
      range: '203.0.113.0/24',
      label: 'Office VPN',
      addedBy: 'vault-admin',
      addedAtUtc: '2026-03-04T05:06:07Z',
    },
  ],
  blocks: [
    {
      range: '192.0.2.9/32',
      reason: "Honey-token 'FINANCE_DECOY_KEY' tripped",
      blockedAtUtc: '2026-11-07T22:30:00Z',
    },
  ],
};
let backend: HttpTestingController;
const assign = vi.fn();

function button(page: ParentNode, name: string): HTMLButtonElement {
  const found = [...page.querySelectorAll('button')].find(
    (item) => item.textContent?.trim() === name,
  );
  if (!found) throw new Error(`No button named ${name}`);
  return found;
}
function fill(page: ParentNode, label: string, value: string): void {
  const input = page.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`);
  if (!input) throw new Error(`No input named ${label}`);
  input.value = value;
  input.dispatchEvent(new Event('input'));
}
function submit(page: ParentNode): void {
  const form = page.querySelector('form');
  if (!form) throw new Error('No form');
  form.dispatchEvent(new Event('submit', { cancelable: true }));
}
function enterProof(page: ParentNode, code: string): void {
  const proof = page.querySelector('app-step-up-dialog');
  const input = proof?.querySelector('input');
  if (!proof || !input) throw new Error('No authenticator code dialog');
  input.value = code;
  submit(proof);
}
function status(page: ParentNode, role: 'status' | 'alert'): string {
  return page.querySelector(`app-status-line [role="${role}"]`)?.textContent?.trim() ?? '';
}
function render() {
  history.replaceState(null, '', '/Admin/Networks');
  TestBed.configureTestingModule({
    providers: [
      ...appConfig.providers,
      provideHttpClientTesting(),
      { provide: PAGE_LOCATION, useValue: { assign } },
    ],
  });
  backend = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(NetworksPage);
  fixture.detectChanges();
  const page: HTMLElement = fixture.nativeElement;
  document.body.append(page);
  page.querySelectorAll('dialog').forEach((dialog) => {
    dialog.showModal = () => dialog.setAttribute('open', '');
    dialog.close = () => dialog.removeAttribute('open');
  });
  return { fixture, page };
}
async function loaded(networks = listed) {
  const rendered = render();
  backend.expectOne(path).flush(networks);
  await rendered.fixture.whenStable();
  return rendered;
}
async function reload(fixture: ComponentFixture<unknown>, networks = listed) {
  fixture.detectChanges();
  backend.expectOne(path).flush(networks);
  await fixture.whenStable();
}
function stepUpRequired(url: string, proof: 'code' | 'sign_in' = 'code') {
  backend
    .expectOne(url)
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

describe('Networks', () => {
  it('shows loading, then an error with retry, then the empty states', async () => {
    const { page, fixture } = render();
    expect(page.textContent).toContain('Loading networks…');
    backend.expectOne(path).flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(page.textContent).toContain('Could not load networks. Try again.');
    button(page, 'Try again').click();
    await reload(fixture, empty);
    expect(page.querySelector('.current-ip-value')?.textContent).toContain('10.8.0.5');
    expect(page.querySelector('.current-ip-value .badge')?.textContent).toBe('Trusted');
    expect(page.textContent).not.toContain('Trust this address');
    expect(page.textContent).toContain(
      'No runtime-trusted networks yet. Entries added here apply immediately and are audited.',
    );
    expect(page.textContent).toContain('No ranges are currently blocked.');
    expect([...page.querySelectorAll('.cidr-list li')].map((item) => item.textContent)).toEqual([
      '127.0.0.1/32',
      '10.0.0.0/8',
    ]);
  });

  it('lists runtime networks and blocks with their UTC times and the decoy name', async () => {
    const { page } = await loaded();
    expect(page.querySelector('.current-ip-value .badge')?.textContent).toBe('Not trusted');
    expect(button(page, 'Trust this address')).toBeTruthy();
    const tables = [...page.querySelectorAll('table')];
    expect([...tables[0]!.querySelectorAll('th')].map((cell) => cell.textContent)).toEqual([
      'Range',
      'Label',
      'Added by',
      'Added (UTC)',
      '',
    ]);
    expect([...tables[0]!.querySelectorAll('td')].map((cell) => cell.textContent?.trim())).toEqual([
      '203.0.113.0/24',
      'Office VPN',
      'vault-admin',
      '2026-03-04 05:06',
      'Remove',
    ]);
    expect([...tables[1]!.querySelectorAll('th')].map((cell) => cell.textContent)).toEqual([
      'Range',
      'Reason',
      'Since (UTC)',
      '',
    ]);
    expect([...tables[1]!.querySelectorAll('td')].map((cell) => cell.textContent?.trim())).toEqual([
      '192.0.2.9/32',
      "Honey-token 'FINANCE_DECOY_KEY' tripped",
      '11-07 22:30',
      'Unblock',
    ]);
  });

  it('trusts the current address without confirmation and reloads with the Legacy notice', async () => {
    const { page, fixture } = await loaded();
    button(page, 'Trust this address').click();
    const request = backend.expectOne(`${path}/trust-current-address`);
    expect(request.request.method).toBe('POST');
    request.flush({ range: '198.51.100.7/32' });
    await reload(fixture, { ...listed, currentAddressTrusted: true });
    expect(status(page, 'status')).toBe(
      'Your current address 198.51.100.7/32 is now trusted. Access rules apply immediately.',
    );
    expect(page.textContent).not.toContain('Trust this address');
    expect(page.querySelector('dialog[open]')).toBeNull();
  });

  it('adds the entered range and label, clears the form and reloads', async () => {
    const { page, fixture } = await loaded();
    fill(page, 'IP or CIDR', ' 10.8.0.0/24 ');
    fill(page, 'Label', 'Office VPN');
    await fixture.whenStable();
    submit(page);
    const request = backend.expectOne(path);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ range: ' 10.8.0.0/24 ', label: 'Office VPN' });
    request.flush({ range: '10.8.0.0/24' });
    await reload(fixture);
    expect(status(page, 'status')).toBe('Trusted network 10.8.0.0/24 added.');
    expect(page.querySelector<HTMLInputElement>('input[aria-label="IP or CIDR"]')?.value).toBe('');
    expect(page.querySelector<HTMLInputElement>('input[aria-label="Label"]')?.value).toBe('');
  });

  it('shows a refused range in the StatusLine and still reloads', async () => {
    const { page, fixture } = await loaded();
    fill(page, 'IP or CIDR', '10.0.0.0/7');
    submit(page);
    const message = "'10.0.0.0/7' is broader than /8 — refusing to trust a range that large.";
    backend
      .expectOne(path)
      .flush(
        { code: 'validation_failed', title: message, errors: { range: [message] } },
        { status: 400, statusText: 'Bad Request' },
      );
    await reload(fixture);
    expect(status(page, 'alert')).toBe(message);
    expect(page.querySelector<HTMLInputElement>('input[aria-label="IP or CIDR"]')?.value).toBe(
      '10.0.0.0/7',
    );
  });

  it('asks for a code once, then retries the add exactly once', async () => {
    const { page, fixture } = await loaded();
    fill(page, 'IP or CIDR', '203.0.113.7');
    submit(page);
    stepUpRequired(path);
    await reload(fixture);
    expect(page.querySelector('app-step-up-dialog dialog[open]')).not.toBeNull();
    expect(button(page, 'Trust').matches(':disabled')).toBe(true);
    enterProof(page, '123456');
    const proof = backend.expectOne('/api/ui/step-up');
    expect(proof.request.body).toEqual({ code: '123456' });
    proof.flush(null);
    const retry = backend.expectOne(path);
    expect(retry.request.body).toEqual({ range: '203.0.113.7', label: '' });
    retry.flush({ range: '203.0.113.7/32' });
    await reload(fixture);
    expect(status(page, 'status')).toBe('Trusted network 203.0.113.7/32 added.');
  });

  it('cancelling the proof restores focus and sends nothing more', async () => {
    const { page, fixture } = await loaded();
    const opener = button(page, 'Trust this address');
    opener.focus();
    opener.click();
    stepUpRequired(`${path}/trust-current-address`);
    await reload(fixture);
    button(page.querySelector('app-step-up-dialog')!, 'Cancel').click();
    await fixture.whenStable();
    expect(document.activeElement).toBe(opener);
    expect(page.querySelector('dialog[open]')).toBeNull();
    expect(button(page, 'Trust this address').matches(':disabled')).toBe(false);
  });

  it('ends the stale session before following the sign-in fallback back to Networks', async () => {
    const { page, fixture } = await loaded();
    button(page, 'Trust this address').click();
    stepUpRequired(`${path}/trust-current-address`, 'sign_in');
    await reload(fixture);
    button(page, 'Sign in again').click();
    backend.expectOne('/Account/Logout').flush('');
    await fixture.whenStable();
    expect(assign).toHaveBeenCalledWith('/Account/Login?ReturnUrl=%2FAdmin%2FNetworks');
  });

  it('removes a range only after the exact Legacy confirmation', async () => {
    const { page, fixture } = await loaded();
    button(page, 'Remove').click();
    await fixture.whenStable();
    expect(page.querySelector('[role="alertdialog"]')?.textContent).toContain(
      'Stop trusting 203.0.113.0/24? Requests from it will fail the network rule for Confidential+ secrets.',
    );
    button(page.querySelector('[role="alertdialog"]')!, 'Cancel').click();
    backend.expectNone(`${path}/${networkId}`);
    button(page, 'Remove').click();
    await fixture.whenStable();
    button(page, 'Continue').click();
    const request = backend.expectOne(`${path}/${networkId}`);
    expect(request.request.method).toBe('DELETE');
    request.flush(null);
    await reload(fixture, { ...listed, trustedNetworks: [] });
    expect(status(page, 'status')).toBe('Trusted network removed.');
  });

  it('shows a range that is already gone as the Legacy error and reloads', async () => {
    const { page, fixture } = await loaded();
    button(page, 'Remove').click();
    await fixture.whenStable();
    button(page, 'Continue').click();
    backend
      .expectOne(`${path}/${networkId}`)
      .flush(
        { code: 'network_not_found', title: 'Network entry not found.' },
        { status: 404, statusText: 'Not Found' },
      );
    await reload(fixture, { ...listed, trustedNetworks: [] });
    expect(status(page, 'alert')).toBe('Network entry not found.');
  });

  it('lifts a block after the exact confirmation and one proof', async () => {
    const { page, fixture } = await loaded();
    button(page, 'Unblock').click();
    await fixture.whenStable();
    expect(page.querySelector('[role="alertdialog"]')?.textContent).toContain(
      'Lift the intrusion-defence block on 192.0.2.9/32?',
    );
    button(page, 'Continue').click();
    stepUpRequired(`${path}/blocks/lift`);
    await reload(fixture);
    enterProof(page, '654321');
    backend.expectOne('/api/ui/step-up').flush(null);
    const retry = backend.expectOne(`${path}/blocks/lift`);
    expect(retry.request.body).toEqual({ range: '192.0.2.9/32' });
    retry.flush(null);
    await reload(fixture, { ...listed, blocks: [] });
    expect(status(page, 'status')).toBe('Block on 192.0.2.9/32 lifted.');
    expect(page.textContent).toContain('No ranges are currently blocked.');
  });

  it.each([
    { code: 'block_not_found', title: 'That range is not currently blocked.', httpStatus: 404 },
    {
      code: 'audit_unavailable',
      title: 'The audit trail is unavailable, so the vault refused the request.',
      httpStatus: 503,
    },
    {
      code: 'unblock_unrecorded',
      title:
        'The block was lifted, but recording and restoring it failed. Check the range before retrying.',
      httpStatus: 503,
    },
  ])('reports a $code lift without claiming it succeeded', async ({ code, title, httpStatus }) => {
    const { page, fixture } = await loaded();
    button(page, 'Unblock').click();
    await fixture.whenStable();
    button(page, 'Continue').click();
    backend
      .expectOne(`${path}/blocks/lift`)
      .flush({ code, title }, { status: httpStatus, statusText: '' });
    await reload(fixture);
    expect(status(page, 'alert')).toBe(title);
    expect(status(page, 'status')).toBe('');
  });

  it('blocks every write after a lost response', async () => {
    const { page, fixture } = await loaded();
    button(page, 'Trust this address').click();
    backend.expectOne(`${path}/trust-current-address`).error(new ProgressEvent('error'));
    await reload(fixture);
    expect(status(page, 'alert')).toBe(
      'The response was lost. Reload the page before trying again.',
    );
    expect(button(page, 'Trust this address').matches(':disabled')).toBe(true);
    expect(button(page, 'Trust').matches(':disabled')).toBe(true);
    expect(button(page, 'Remove').matches(':disabled')).toBe(true);
    expect(button(page, 'Unblock').matches(':disabled')).toBe(true);
  });
});
