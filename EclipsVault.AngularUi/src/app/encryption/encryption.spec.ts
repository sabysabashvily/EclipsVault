import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import type { components } from '../ui-api/schema.generated';
import { EncryptionPage } from './encryption-page';

type Encryption = components['schemas']['EncryptionResponse'];

const path = '/api/ui/encryption';
const rotatePath = `${path}/rotate`;
const settled: Encryption = {
  currentKekId: 'kek-1a2b3c4d',
  loadedKekIds: ['kek-1a2b3c4d'],
  keys: [{ kekId: 'kek-1a2b3c4d', status: 'current', secrets: 12, versions: 3 }],
  awaitingRotation: 0,
  onMissingKeys: 0,
};
const pending: Encryption = {
  currentKekId: 'kek-1a2b3c4d',
  loadedKekIds: ['kek-1a2b3c4d', 'kek-5e6f7a8b'],
  keys: [
    { kekId: 'kek-1a2b3c4d', status: 'current', secrets: 12, versions: 3 },
    { kekId: 'kek-5e6f7a8b', status: 'retired', secrets: 4, versions: 1 },
  ],
  awaitingRotation: 5,
  onMissingKeys: 0,
};
const stranded: Encryption = {
  currentKekId: 'kek-1a2b3c4d',
  loadedKekIds: ['kek-1a2b3c4d', 'kek-5e6f7a8b'],
  keys: [
    { kekId: 'kek-1a2b3c4d', status: 'current', secrets: 12, versions: 3 },
    { kekId: 'kek-5e6f7a8b', status: 'retired', secrets: 4, versions: 1 },
    { kekId: 'kek-9c0d1e2f', status: 'not_loaded', secrets: 2, versions: 0 },
  ],
  awaitingRotation: 5,
  onMissingKeys: 2,
};
const confirmation =
  "Re-wrap every secret's data key under the current KEK now? This is safe and can be repeated.";
let backend: HttpTestingController;

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
function render() {
  TestBed.configureTestingModule({
    providers: [...appConfig.providers, provideHttpClientTesting()],
  });
  backend = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(EncryptionPage);
  fixture.detectChanges();
  const page: HTMLElement = fixture.nativeElement;
  document.body.append(page);
  page.querySelectorAll('dialog').forEach((dialog) => {
    dialog.showModal = () => dialog.setAttribute('open', '');
    dialog.close = () => dialog.removeAttribute('open');
  });
  return { fixture, page };
}
async function loaded(encryption: Encryption) {
  const rendered = render();
  backend.expectOne(path).flush(encryption);
  await rendered.fixture.whenStable();
  return rendered;
}
async function reload(fixture: ComponentFixture<unknown>, encryption: Encryption) {
  fixture.detectChanges();
  backend.expectOne(path).flush(encryption);
  await fixture.whenStable();
}
async function confirmRotation(page: HTMLElement, fixture: ComponentFixture<unknown>) {
  button(page, 'Re-wrap under current KEK').click();
  await fixture.whenStable();
  button(page, 'Continue').click();
  fixture.detectChanges();
  return backend.expectOne(rotatePath);
}

afterEach(() => {
  try {
    backend.verify();
  } finally {
    TestBed.resetTestingModule();
  }
});

describe('Encryption keys', () => {
  it('shows loading until the keys arrive', async () => {
    const { page, fixture } = render();
    expect(page.textContent).toContain('Loading encryption keys…');
    backend.expectOne(path).flush(settled);
    await fixture.whenStable();
  });

  it('shows a failed load with Try again, which loads the keys', async () => {
    const { page, fixture } = render();
    backend.expectOne(path).flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(page.textContent).toContain('Could not load encryption keys. Try again.');
    button(page, 'Try again').click();
    await reload(fixture, settled);
    expect(page.textContent).not.toContain('Could not load encryption keys.');
    expect(text(notice(page))).toContain('✓ All secrets are wrapped under the current KEK.');
  });

  it('explains that rotation leaves the Data Protection key ring unchanged', async () => {
    const { page } = await loaded(settled);
    expect(text(page.querySelector('.page-sub'))).toBe(
      "Each secret's data key is wrapped by a master KEK. Rotation re-wraps secrets and their versions under the current KEK; the payload itself is never re-encrypted. The Data Protection key ring still needs its existing keys.",
    );
  });

  it('shows the ok notice and a disabled Rotate button when nothing awaits rotation', async () => {
    const { page } = await loaded(settled);
    expect(text(notice(page))).toContain('✓ All secrets are wrapped under the current KEK.');
    expect(text(notice(page))).toContain('Nothing to rotate.');
    expect(button(page, 'Re-wrap under current KEK').disabled).toBe(true);
  });

  it('shows the warning notice with the count and an enabled Rotate button when rows await rotation', async () => {
    const { page } = await loaded(pending);
    expect(text(notice(page))).toContain('5 item(s) are still on a retired KEK.');
    expect(text(notice(page))).toContain(
      'Run a rotation to re-wrap them under the current key. Keep retired keys loaded while the Data Protection key ring needs them.',
    );
    expect(button(page, 'Re-wrap under current KEK').disabled).toBe(false);
  });

  it('shows only the critical notice when a key in use is not loaded', async () => {
    const { page } = await loaded(stranded);
    expect(text(notice(page))).toContain('⚠ Some secrets reference a KEK that is not loaded.');
    expect(text(notice(page))).toContain(
      'Provide the missing key as a retired KEK (ECLIPSVAULT_KEK_RETIRED) so it can be decrypted and rotated, or those secrets cannot be read.',
    );
    expect(page.textContent).not.toContain('still on a retired KEK');
  });

  it('lists each key in the order sent, with its status badge and counts', async () => {
    const { page } = await loaded(stranded);
    expect([...page.querySelectorAll('th')].map((cell) => cell.textContent)).toEqual([
      'KEK id',
      'Status',
      'Secrets',
      'Versions',
    ]);
    expect(
      [...page.querySelectorAll('tbody tr')].map((row) =>
        [...row.querySelectorAll('td')].map((cell) => text(cell)),
      ),
    ).toEqual([
      ['kek-1a2b3c4d', 'current', '12', '3'],
      ['kek-5e6f7a8b', 'retired', '4', '1'],
      ['kek-9c0d1e2f', 'not loaded', '2', '0'],
    ]);
  });

  it('shows the current key, the loaded keys and what awaits rotation above the operator steps', async () => {
    const { page } = await loaded(pending);
    expect(
      [...page.querySelectorAll('.detail-grid dt, .detail-grid dd')].map((cell) => text(cell)),
    ).toEqual([
      'Current KEK',
      'kek-1a2b3c4d',
      'Keys loaded',
      'kek-1a2b3c4d, kek-5e6f7a8b',
      'Awaiting rotation',
      '5 item(s)',
    ]);
    expect(text(page.querySelector('.hint'))).toBe(
      'To rotate: generate a new key, set it as ECLIPSVAULT_KEK, move the previous key to ECLIPSVAULT_KEK_RETIRED, restart, then run the rotation below. Rotation re-wraps secrets and their archived versions. It does not re-wrap the Data Protection key ring. Keep retired master keys loaded while the ring needs them; a zero pending count does not mean a retired key can be removed.',
    );
  });

  it('asks before rotating with the exact Legacy confirmation', async () => {
    const { page, fixture } = await loaded(pending);
    button(page, 'Re-wrap under current KEK').click();
    await fixture.whenStable();
    expect(text(page.querySelector('[role="alertdialog"][open] p'))).toBe(confirmation);
  });

  it('cancelling the confirmation sends nothing', async () => {
    const { page, fixture } = await loaded(pending);
    button(page, 'Re-wrap under current KEK').click();
    await fixture.whenStable();
    button(page, 'Cancel').click();
    backend.expectNone(rotatePath);
    expect(page.querySelector('dialog[open]')).toBeNull();
  });

  it('confirming rotates, shows the Legacy counts and reloads the status', async () => {
    const { page, fixture } = await loaded(pending);
    const request = await confirmRotation(page, fixture);
    expect(request.request.method).toBe('POST');
    expect(button(page, 'Re-wrap under current KEK').disabled).toBe(true);
    request.flush({
      currentKekId: 'kek-1a2b3c4d',
      secretsRewrapped: 4,
      versionsRewrapped: 1,
      onMissingKeys: 0,
    });
    await reload(fixture, settled);
    expect(status(page, 'status')).toBe(
      'Rotated 4 secret(s) and 1 version(s) to KEK kek-1a2b3c4d.',
    );
    expect(text(notice(page))).toContain('✓ All secrets are wrapped under the current KEK.');
    expect(button(page, 'Re-wrap under current KEK').disabled).toBe(true);
  });

  it('adds how many items remain on keys that are not loaded', async () => {
    const { page, fixture } = await loaded(stranded);
    (await confirmRotation(page, fixture)).flush({
      currentKekId: 'kek-1a2b3c4d',
      secretsRewrapped: 4,
      versionsRewrapped: 1,
      onMissingKeys: 2,
    });
    await reload(fixture, { ...stranded, awaitingRotation: 0 });
    expect(status(page, 'status')).toBe(
      'Rotated 4 secret(s) and 1 version(s) to KEK kek-1a2b3c4d. 2 item(s) remain on keys that are not loaded.',
    );
  });

  it('says everything is already wrapped when a pass moved nothing', async () => {
    const { page, fixture } = await loaded(pending);
    (await confirmRotation(page, fixture)).flush({
      currentKekId: 'kek-1a2b3c4d',
      secretsRewrapped: 0,
      versionsRewrapped: 0,
      onMissingKeys: 0,
    });
    await reload(fixture, settled);
    expect(status(page, 'status')).toBe(
      'Everything is already wrapped under the current KEK (kek-1a2b3c4d).',
    );
  });

  it('shows that another rotation is running as an error and reloads the status', async () => {
    const { page, fixture } = await loaded(pending);
    (await confirmRotation(page, fixture)).flush(
      {
        code: 'rotation_running',
        title: 'Another rotation is already running. Try again when it finishes.',
      },
      { status: 409, statusText: 'Conflict' },
    );
    await reload(fixture, pending);
    expect(status(page, 'alert')).toBe(
      'Another rotation is already running. Try again when it finishes.',
    );
    expect(button(page, 'Re-wrap under current KEK').disabled).toBe(false);
  });

  it('shows how much moved before the audit trail stopped the rotation, and reloads the status', async () => {
    const { page, fixture } = await loaded(pending);
    const stopped =
      'Rotation stopped because the audit trail could not record it. 4 item(s) were re-wrapped before it stopped; run it again to continue.';
    (await confirmRotation(page, fixture)).flush(
      { code: 'audit_unavailable', title: stopped, secretsRewrapped: 4, versionsRewrapped: 0 },
      { status: 503, statusText: 'Service Unavailable' },
    );
    await reload(fixture, { ...pending, awaitingRotation: 1 });
    expect(status(page, 'alert')).toBe(stopped);
    expect(text(notice(page))).toContain('1 item(s) are still on a retired KEK.');
  });

  it('closes rotation until the page is reloaded when the response is lost', async () => {
    const { page, fixture } = await loaded(pending);
    (await confirmRotation(page, fixture)).error(new ProgressEvent('error'));
    await reload(fixture, pending);
    expect(status(page, 'alert')).toBe(
      'The response was lost. Reload the page before trying again.',
    );
    expect(button(page, 'Re-wrap under current KEK').disabled).toBe(true);
  });
});
