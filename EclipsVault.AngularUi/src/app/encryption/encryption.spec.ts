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
  keyService: false,
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
  keyService: false,
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
  keyService: false,
};
// As the server reports Vault Transit: each key is a version of the service's key, the latest current.
const transitSettled: Encryption = {
  currentKekId: 'vault:eclipsvault:v1',
  loadedKekIds: ['vault:eclipsvault:v1'],
  keys: [{ kekId: 'vault:eclipsvault:v1', status: 'current', secrets: 12, versions: 3 }],
  awaitingRotation: 0,
  onMissingKeys: 0,
  keyService: true,
};
const transitPending: Encryption = {
  currentKekId: 'vault:eclipsvault:v3',
  loadedKekIds: ['vault:eclipsvault:v3', 'vault:eclipsvault:v2', 'vault:eclipsvault:v1'],
  keys: [
    { kekId: 'vault:eclipsvault:v3', status: 'current', secrets: 3, versions: 0 },
    { kekId: 'vault:eclipsvault:v1', status: 'retired', secrets: 10, versions: 2 },
    { kekId: 'vault:eclipsvault:v2', status: 'retired', secrets: 0, versions: 0 },
  ],
  awaitingRotation: 12,
  onMissingKeys: 0,
  keyService: true,
};
const transitStranded: Encryption = {
  currentKekId: 'vault:eclipsvault:v3',
  loadedKekIds: ['vault:eclipsvault:v3', 'vault:eclipsvault:v2'],
  keys: [
    { kekId: 'vault:eclipsvault:v3', status: 'current', secrets: 3, versions: 0 },
    { kekId: 'vault:eclipsvault:v2', status: 'retired', secrets: 5, versions: 0 },
    { kekId: 'vault:eclipsvault:v1', status: 'not_loaded', secrets: 2, versions: 0 },
  ],
  awaitingRotation: 5,
  onMissingKeys: 2,
  keyService: true,
};
// The pending vault after a re-wrap moved its 4 secrets and stopped before its 1 version.
const pendingAfterStop: Encryption = {
  ...pending,
  keys: [
    { kekId: 'kek-1a2b3c4d', status: 'current', secrets: 16, versions: 3 },
    { kekId: 'kek-5e6f7a8b', status: 'retired', secrets: 0, versions: 1 },
  ],
  awaitingRotation: 1,
};
const emptied: Encryption = {
  ...settled,
  loadedKekIds: ['kek-1a2b3c4d', 'kek-5e6f7a8b'],
  keys: [...settled.keys, { kekId: 'kek-5e6f7a8b', status: 'retired', secrets: 0, versions: 0 }],
};
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
function verdict(page: ParentNode): string {
  return text(page.querySelector('.title-row .badge'));
}
function notices(page: ParentNode): string[] {
  return [...page.querySelectorAll('.notice h2')].map((title) => text(title));
}
/** The page's Re-wrap buttons by name, leaving out the confirmation dialog's own. */
function rewrapButtons(page: ParentNode): string[] {
  return [...page.querySelectorAll('button')]
    .filter((item) => !item.closest('dialog'))
    .map((item) => text(item))
    .filter((name) => name.startsWith('Re-wrap'));
}
function alerts(page: ParentNode): string[] {
  return [...page.querySelectorAll('[role="alert"]')]
    .map((alert) => text(alert))
    .filter((said) => said !== '');
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
async function confirmRotation(
  page: HTMLElement,
  fixture: ComponentFixture<unknown>,
  name = 'Re-wrap 5 items',
) {
  button(page, name).click();
  await fixture.whenStable();
  const dialog = page.querySelector('dialog[open]');
  if (!dialog) throw new Error('No confirmation');
  button(dialog, name).click();
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
    expect(verdict(page)).toBe('All current');
  });

  it('all current: says so, offers no re-wrap and shows how to move to a new key', async () => {
    const { page } = await loaded(settled);
    expect(verdict(page)).toBe('All current');
    expect(text(page.querySelector('.page-sub'))).toBe(
      "Every secret's data key is wrapped by the current master key. Nothing to re-wrap.",
    );
    expect(notices(page)).toEqual([]);
    expect(page.querySelector('.action-strip')).toBeNull();
    expect([...page.querySelectorAll('.step-card li')].map((step) => text(step))).toEqual([
      'Set the new key as ECLIPSVAULT_KEK.',
      'Move the current one to ECLIPSVAULT_KEK_RETIRED and restart.',
      'Re-wrap here. Keep the retired key loaded while the Data Protection key ring needs it.',
    ]);
  });

  it('waiting: counts what waits, keeps the retired key loaded, and offers the re-wrap', async () => {
    const { page } = await loaded(pending);
    expect(verdict(page)).toBe('Re-wrap pending');
    expect(text(page.querySelector('.page-sub'))).toBe(
      '5 items still use the retired master key. Re-wrap them under the current key; keep the retired key loaded while the Data Protection key ring needs it.',
    );
    expect(notices(page)).toEqual([]);
    expect(
      [...page.querySelectorAll('.stat-card')].map((figure) => [
        text(figure.querySelector('.stat-label')),
        text(figure.querySelector('.stat-value')),
        text(figure.querySelector('.stat-detail')),
      ]),
    ).toEqual([
      ['Waiting to re-wrap', '5', '4 secrets and 1 version'],
      ['On the current key', '15', 'of 20 items in the vault'],
    ]);
    expect(rewrapButtons(page)).toEqual(['Re-wrap 5 items']);
    expect(page.querySelector('.step-card')).toBeNull();
  });

  it('missing key: names the key to load, puts what cannot be read first, and skips it', async () => {
    const { page } = await loaded(stranded);
    expect(verdict(page)).toBe('Action needed');
    expect(text(page.querySelector('.page-sub'))).toBe(
      "2 items use a master key this server doesn't have, so they can't be read. 5 more are waiting to be re-wrapped.",
    );
    expect(notices(page)).toEqual(['Load kek-9c0d1e2f on this server']);
    expect(text(page.querySelector('.notice'))).not.toContain('remove');
    expect([...page.querySelectorAll('.stat-label')].map((label) => text(label))).toEqual([
      "Can't be read",
      'Waiting to re-wrap',
    ]);
    expect(text(page.querySelector('.action-tile p'))).toBe(
      'Nothing changes until you confirm. The 2 items on the missing key are skipped.',
    );
    expect(rewrapButtons(page)).toEqual(['Re-wrap 5 items']);
  });

  it('key service, all current: says so and offers no re-wrap', async () => {
    const { page } = await loaded(transitSettled);
    expect(verdict(page)).toBe('All current');
    expect(rewrapButtons(page)).toEqual([]);
  });

  it('key service, all current: the latest version wraps every new secret', async () => {
    const { page } = await loaded(transitSettled);
    expect(
      [...page.querySelectorAll('tbody tr')].map((row) =>
        [...row.querySelectorAll('.mono, .key-role, .badge')].map((cell) => text(cell)),
      ),
    ).toEqual([['vault:eclipsvault:v1', 'Wraps every new secret', 'Current']]);
  });

  it('key service, all current: moving to a new key starts in the key service', async () => {
    const { page } = await loaded(transitSettled);
    expect(text(page.querySelector('.step-card p'))).toBe(
      'The master key lives in the key service, so the first step happens there.',
    );
    expect([...page.querySelectorAll('.step-card li')].map((step) => text(step))).toEqual([
      'Rotate the key in the key service. New secrets use its latest version at once.',
      'Re-wrap here, which moves older items to the latest version.',
    ]);
  });

  it('key service versions: counts items on earlier versions as waiting for the latest', async () => {
    const { page } = await loaded(transitPending);
    expect(verdict(page)).toBe('Re-wrap pending');
    expect(text(page.querySelector('.page-sub'))).toBe(
      '12 items still use an earlier key version. Re-wrap them under the latest version.',
    );
    expect(notices(page)).toEqual([]);
    expect(
      [...page.querySelectorAll('.stat-card')].map((figure) => [
        text(figure.querySelector('.stat-label')),
        text(figure.querySelector('.stat-value')),
        text(figure.querySelector('.stat-detail')),
      ]),
    ).toEqual([
      ['Waiting to re-wrap', '12', '10 secrets and 2 versions'],
      ['On the current key', '3', 'of 15 items in the vault'],
    ]);
    expect(rewrapButtons(page)).toEqual(['Re-wrap 12 items']);
    expect(
      [...page.querySelectorAll('tbody tr')].map((row) =>
        [...row.querySelectorAll('.key-role, .badge')].map((cell) => text(cell)),
      ),
    ).toEqual([
      ['Wraps every new secret', 'Current'],
      [
        'Held by the key service · re-wrap moves its items to the latest version',
        'Earlier version',
      ],
      ['Held by the key service · wraps no secrets', 'Earlier version'],
    ]);
  });

  it('key service version it will not unwrap: says how to allow it, and skips its items', async () => {
    const { page } = await loaded(transitStranded);
    expect(verdict(page)).toBe('Action needed');
    expect(text(page.querySelector('.page-sub'))).toBe(
      "2 items use a key version the key service won't unwrap, so they can't be read. 5 more are waiting to be re-wrapped.",
    );
    expect(notices(page)).toEqual(['Let the key service unwrap vault:eclipsvault:v1']);
    expect([...page.querySelectorAll('.notice li')].map((step) => text(step))).toEqual([
      "If the key service still has this version, allow decryption with it again; in Vault Transit, lower the key's min_decryption_version.",
      'If the version was trimmed, it is gone: only a backup of the key from before the trim can open these items.',
      'Re-wrap here, which moves its items to the latest version.',
    ]);
    expect(text(page.querySelector('tbody tr:last-child .key-role'))).toBe(
      "The key service won't unwrap it · its items can't be read",
    );
    expect(rewrapButtons(page)).toEqual(['Re-wrap 5 items']);
  });

  it('key service version it will not unwrap: the counts and the action name the version', async () => {
    const { page } = await loaded(transitStranded);
    expect(
      [...page.querySelectorAll('.stat-card')].map((figure) => [
        text(figure.querySelector('.stat-label')),
        text(figure.querySelector('.stat-detail')),
      ]),
    ).toEqual([
      ["Can't be read", 'until the key service unwraps its version again'],
      ['Waiting to re-wrap', '5 secrets and 0 versions'],
    ]);
    expect(text(page.querySelector('.action-tile p'))).toBe(
      'Nothing changes until you confirm. The 2 items on the unusable version are skipped.',
    );
  });

  it('key service version it will not unwrap: the confirmation skips the unusable version', async () => {
    const { page, fixture } = await loaded(transitStranded);
    button(page, 'Re-wrap 5 items').click();
    await fixture.whenStable();
    const dialog = page.querySelector('dialog[open]');
    if (!dialog) throw new Error('No confirmation');
    expect(
      [...dialog.querySelectorAll('.facts div')].map((fact) => [
        text(fact.querySelector('dt')),
        text(fact.querySelector('dd')),
      ]),
    ).toEqual([
      ['Current key', 'vault:eclipsvault:v3'],
      ['To re-wrap', '5 secrets and 0 versions'],
      ['Skipped, version not usable', '2 items'],
    ]);
    button(dialog, 'Cancel').click();
    backend.expectNone(rotatePath);
  });

  it('key service: a re-wrap that leaves items on an unusable version says so', async () => {
    const { page, fixture } = await loaded(transitStranded);
    (await confirmRotation(page, fixture)).flush({
      currentKekId: 'vault:eclipsvault:v3',
      secretsRewrapped: 5,
      versionsRewrapped: 0,
      onMissingKeys: 2,
    });
    await reload(fixture, { ...transitStranded, awaitingRotation: 0 });
    expect(text(page.querySelector('.notice-ok'))).toContain(
      "2 items remain on versions the key service won't unwrap.",
    );
  });

  it('re-wrapping: the button says so and is disabled while the request is in flight', async () => {
    const { page, fixture } = await loaded(pending);
    const request = await confirmRotation(page, fixture);
    await fixture.whenStable();
    expect(verdict(page)).toBe('Re-wrapping');
    expect(button(page, 'Re-wrapping…').disabled).toBe(true);
    expect(page.querySelector('.busy-bar')).not.toBeNull();
    request.flush({
      currentKekId: 'kek-1a2b3c4d',
      secretsRewrapped: 4,
      versionsRewrapped: 1,
      onMissingKeys: 0,
    });
    await reload(fixture, settled);
  });

  it('done: reports what moved, takes the verdict from the reloaded keys, and hides the steps', async () => {
    const { page, fixture } = await loaded(pending);
    const request = await confirmRotation(page, fixture);
    expect(request.request.method).toBe('POST');
    request.flush({
      currentKekId: 'kek-1a2b3c4d',
      secretsRewrapped: 4,
      versionsRewrapped: 1,
      onMissingKeys: 0,
    });
    await reload(fixture, emptied);
    expect(verdict(page)).toBe('All current');
    expect(notices(page)).toEqual(['Re-wrapped 4 secrets and 1 version under the current key']);
    expect(page.querySelector('.step-card')).toBeNull();
    expect(status(page, 'status')).toBe('');
  });

  it('stopped by auditing: says what each committed in an alert, with the verdict "Stopped partway"', async () => {
    const { page, fixture } = await loaded(pending);
    (await confirmRotation(page, fixture)).flush(
      {
        code: 'audit_unavailable',
        title:
          'Rotation stopped because the audit trail could not record it. 4 item(s) were re-wrapped before it stopped; run it again to continue.',
        secretsRewrapped: 4,
        versionsRewrapped: 0,
      },
      { status: 503, statusText: 'Service Unavailable' },
    );
    await reload(fixture, pendingAfterStop);
    expect(verdict(page)).toBe('Stopped partway');
    expect(alerts(page)).toEqual([
      'Re-wrap stopped: the audit log could not record it 4 secrets and 0 versions were re-wrapped before it stopped. Run it again to continue.',
    ]);
    expect(rewrapButtons(page)).toEqual(['Re-wrap 1 item']);
  });

  it('stopped by an error: says what each committed in an alert, with the verdict "Stopped partway"', async () => {
    const { page, fixture } = await loaded(pending);
    (await confirmRotation(page, fixture)).flush(
      {
        code: 'rotation_interrupted',
        title:
          'Rotation stopped because of an error. 4 item(s) were re-wrapped before it stopped; run it again to continue.',
        secretsRewrapped: 4,
        versionsRewrapped: 0,
      },
      { status: 500, statusText: 'Internal Server Error' },
    );
    await reload(fixture, pendingAfterStop);
    expect(verdict(page)).toBe('Stopped partway');
    expect(text(page.querySelector('.page-sub'))).toBe(
      'The last re-wrap stopped partway. 1 item still uses the retired master key.',
    );
    expect(alerts(page)).toEqual([
      'Re-wrap stopped partway because of an error 4 secrets and 0 versions were re-wrapped before it stopped. Run it again to continue.',
    ]);
  });

  it('stopped by an error: the re-wrap button sends another re-wrap without a reload', async () => {
    const { page, fixture } = await loaded(pending);
    (await confirmRotation(page, fixture)).flush(
      {
        code: 'rotation_interrupted',
        title:
          'Rotation stopped because of an error. 4 item(s) were re-wrapped before it stopped; run it again to continue.',
        secretsRewrapped: 4,
        versionsRewrapped: 0,
      },
      { status: 500, statusText: 'Internal Server Error' },
    );
    await reload(fixture, pendingAfterStop);
    const again = await confirmRotation(page, fixture, 'Re-wrap 1 item');
    expect(again.request.method).toBe('POST');
    again.flush({
      currentKekId: 'kek-1a2b3c4d',
      secretsRewrapped: 0,
      versionsRewrapped: 1,
      onMissingKeys: 0,
    });
    await reload(fixture, settled);
    expect(notices(page)).toEqual(['Re-wrapped 0 secrets and 1 version under the current key']);
  });

  it('stopped by an error, then the keys fail to load: still says what the re-wrap committed', async () => {
    const { page, fixture } = await loaded(pending);
    (await confirmRotation(page, fixture)).flush(
      {
        code: 'rotation_interrupted',
        title:
          'Rotation stopped because of an error. 4 item(s) were re-wrapped before it stopped; run it again to continue.',
        secretsRewrapped: 4,
        versionsRewrapped: 1,
      },
      { status: 500, statusText: 'Internal Server Error' },
    );
    fixture.detectChanges();
    backend.expectOne(path).flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(alerts(page)).toEqual([
      'Re-wrap stopped partway because of an error 4 secrets and 1 version were re-wrapped before it stopped. Run it again to continue.',
      'Could not load encryption keys. Try again.',
    ]);
  });

  it('already running: says nothing changed, and keeps the verdict from the keys', async () => {
    const { page, fixture } = await loaded(pending);
    (await confirmRotation(page, fixture)).flush(
      {
        code: 'rotation_running',
        title: 'Another rotation is already running. Try again when it finishes.',
      },
      { status: 409, statusText: 'Conflict' },
    );
    await reload(fixture, pending);
    expect(verdict(page)).toBe('Re-wrap pending');
    expect(notices(page)).toEqual(['Another re-wrap is already running']);
    expect(text(page.querySelector('.notice-info p'))).toBe(
      'Nothing was changed by this attempt. Try again when the other run finishes.',
    );
    expect(rewrapButtons(page)).toEqual(['Re-wrap 5 items']);
  });

  it('lists each key in the order sent, with its status, what it is for and its counts', async () => {
    const { page } = await loaded({
      ...stranded,
      keys: [...stranded.keys, emptied.keys[1]!],
    });
    expect([...page.querySelectorAll('th')].map((cell) => cell.textContent)).toEqual([
      'Key',
      'Status',
      'Secrets',
      'Versions',
    ]);
    expect(
      [...page.querySelectorAll('tbody tr')].map((row) =>
        [row.querySelector('.mono'), ...row.querySelectorAll('.key-role, .badge, .num')].map(
          (cell) => text(cell),
        ),
      ),
    ).toEqual([
      ['kek-1a2b3c4d', 'Wraps every new secret', 'Current', '12', '3'],
      [
        'kek-5e6f7a8b',
        'Loaded so its items can be read · re-wrap moves them to the current key',
        'Retired',
        '4',
        '1',
      ],
      ['kek-9c0d1e2f', "Not on this server · its items can't be read", 'Not loaded', '2', '0'],
      [
        'kek-5e6f7a8b',
        'Wraps no secrets · the Data Protection key ring may still need it',
        'Retired',
        '0',
        '0',
      ],
    ]);
  });

  it('asks before re-wrapping, with the count, the facts and the audit note', async () => {
    const { page, fixture } = await loaded(stranded);
    button(page, 'Re-wrap 5 items').click();
    await fixture.whenStable();
    const dialog = page.querySelector('dialog[open]');
    if (!dialog) throw new Error('No confirmation');
    expect(text(dialog.querySelector('h2'))).toBe('Re-wrap 5 items?');
    expect(text(dialog.querySelector('h2 + p'))).toBe(
      "Each item's data key is wrapped again by the current master key. Secret values are not decrypted or re-encrypted, and it is safe to run again.",
    );
    expect(
      [...dialog.querySelectorAll('.facts div')].map((fact) => [
        text(fact.querySelector('dt')),
        text(fact.querySelector('dd')),
      ]),
    ).toEqual([
      ['Current key', 'kek-1a2b3c4d'],
      ['To re-wrap', '4 secrets and 1 version'],
      ['Skipped, key not loaded', '2 items'],
    ]);
    expect(text(dialog.querySelector('.dialog-note'))).toBe('Saved to the audit log');
    button(dialog, 'Cancel').click();
    backend.expectNone(rotatePath);
    expect(page.querySelector('dialog[open]')).toBeNull();
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
    expect(text(page.querySelector('.notice-ok'))).toContain(
      '2 items remain on keys that are not loaded.',
    );
  });

  it('says nothing needed re-wrapping when a pass moved nothing', async () => {
    const { page, fixture } = await loaded(pending);
    (await confirmRotation(page, fixture)).flush({
      currentKekId: 'kek-1a2b3c4d',
      secretsRewrapped: 0,
      versionsRewrapped: 0,
      onMissingKeys: 0,
    });
    await reload(fixture, settled);
    expect(text(page.querySelector('.notice-ok'))).toContain('Nothing needed re-wrapping.');
  });

  it('closes re-wrapping until the page is reloaded when the response is lost', async () => {
    const { page, fixture } = await loaded(pending);
    (await confirmRotation(page, fixture)).error(new ProgressEvent('error'));
    await reload(fixture, pending);
    expect(status(page, 'alert')).toBe(
      'The response was lost. Reload the page before trying again.',
    );
    expect(button(page, 'Re-wrap 5 items').disabled).toBe(true);
  });
});
