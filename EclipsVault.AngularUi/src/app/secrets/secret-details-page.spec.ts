import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, convertToParamMap, Router } from '@angular/router';
import { of } from 'rxjs';
import { appConfig } from '../app.config';
import type { components } from '../ui-api/schema.generated';
import { SecretDetailsPage } from './secret-details-page';

type Details = components['schemas']['SecretDetailsResponse'];
type Managed = components['schemas']['ManagedSecretResponse'];

const id = '6d1b2c55-0000-4000-8000-000000000001';
const path = `/api/ui/secrets/${id}`;
const version: components['schemas']['SecretVersionResponse'] = {
  versionId: '6d1b2c55-0000-4000-8000-000000000002',
  versionNumber: 1,
  archivedAtUtc: '2026-01-05T09:00:00Z',
  archivedBy: 'vault-admin',
  changeNote: 'quarterly rotation',
};
const share: components['schemas']['SecretShareResponse'] = {
  shareId: '6d1b2c55-0000-4000-8000-000000000003',
  recipient: 'colleague',
  issuer: 'vault-admin',
  sinceUtc: '2026-10-20T10:05:00Z',
  expiresAtUtc: null,
};
const secret: Details = {
  secretId: id,
  name: 'Deployment_key',
  project: 'GLOBAL',
  environment: 'Production',
  sensitivity: 2,
  sensitivityLabel: 'Confidential',
  algorithm: 'AES-256-GCM',
  createdAtUtc: '2026-10-20T10:05:00Z',
  updatedAtUtc: null,
  expiresAtUtc: null,
  managed: null,
  versions: [version],
  canChange: true,
  canShare: true,
  shares: [share],
  stepUpMaxAgeMinutes: 10,
};
const recipientView: Details = { ...secret, canChange: false, canShare: false, shares: null };
const managed: Managed = {
  principal: 'app_login',
  licensed: true,
  evaluationKnown: true,
  evaluationDaysRemaining: null,
};
const noContent = { status: 204, statusText: 'No Content' };
const forbidden = { status: 403, statusText: 'Forbidden' };
const conflict = { status: 409, statusText: 'Conflict' };
const staleProof = {
  code: 'step_up_required',
  title: "Confirm it's you before revealing this secret.",
  proof: 'code',
};
const rotated = 'Secret rotated. The previous value was archived to version history.';

describe('SecretDetailsPage', () => {
  let backend: HttpTestingController;

  function render(prepare: () => void = () => undefined) {
    TestBed.configureTestingModule({
      providers: [
        ...appConfig.providers,
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap({ id })) } },
      ],
    });
    backend = TestBed.inject(HttpTestingController);
    prepare();
    const fixture = TestBed.createComponent(SecretDetailsPage);
    fixture.detectChanges();
    const page = fixture.nativeElement as HTMLElement;
    // The test DOM has no modal dialogs; each dialog only shows whether it is open.
    for (const dialog of page.querySelectorAll('dialog')) {
      dialog.showModal = () => dialog.setAttribute('open', '');
      dialog.close = () => dialog.removeAttribute('open');
    }
    return { fixture, page };
  }

  async function loaded(body: Details = secret, prepare?: () => void) {
    const rendered = render(prepare);
    backend.expectOne(path).flush(body);
    await rendered.fixture.whenStable();
    return rendered;
  }

  function button(scope: ParentNode, text: string): HTMLButtonElement {
    return [...scope.querySelectorAll('button')].find(
      (candidate) => candidate.textContent?.trim() === text,
    )!;
  }

  function buttons(scope: ParentNode): string[] {
    return [...scope.querySelectorAll('button, summary')]
      .filter((candidate) => !candidate.closest('dialog'))
      .map((candidate) => candidate.textContent?.trim() ?? '');
  }

  function panel(page: HTMLElement, heading: string): HTMLElement | undefined {
    return [...page.querySelectorAll<HTMLElement>('section.panel')].find(
      (section) => section.querySelector('h2')?.textContent === heading,
    );
  }

  function type(field: HTMLInputElement | HTMLTextAreaElement, value: string): void {
    field.value = value;
    field.dispatchEvent(new Event('input'));
  }

  function submit(form: HTMLFormElement): void {
    form.dispatchEvent(new Event('submit', { cancelable: true }));
  }

  function confirmation(page: HTMLElement): HTMLDialogElement {
    return page.querySelector('dialog.confirmation-dialog')!;
  }

  function stepUpDialog(page: HTMLElement): HTMLDialogElement {
    return [...page.querySelectorAll('dialog')].find(
      (dialog) => dialog.querySelector('h2')?.textContent === "Confirm it's you",
    )!;
  }

  function announced(page: HTMLElement) {
    return {
      success: page.querySelector('.flash-success')?.textContent ?? '',
      error: page.querySelector('.flash-error')?.textContent ?? '',
    };
  }

  async function confirm(
    fixture: { whenStable(): Promise<unknown> },
    page: HTMLElement,
    label: string,
  ) {
    await fixture.whenStable();
    button(confirmation(page), label).click();
  }

  async function reloaded(
    fixture: { detectChanges(): void; whenStable(): Promise<unknown> },
    body: Details = secret,
  ) {
    fixture.detectChanges();
    backend.expectOne(path).flush(body);
    await fixture.whenStable();
  }

  async function rotate(fixture: { whenStable(): Promise<unknown> }, page: HTMLElement) {
    type(page.querySelector('#newValue')!, 'new-value');
    submit(page.querySelector('form.rotate-form')!);
    await confirm(fixture, page, 'Rotate secret');
  }

  afterEach(() => {
    document.cookie = 'XSRF-TOKEN=; path=/; max-age=0';
    backend.verify();
  });

  it('shows loading until the server answers', () => {
    const { page } = render();
    expect(page.textContent).toContain('Loading secret…');
    backend.expectOne(path).flush(secret);
  });

  it('shows a secret that does not exist as not found', async () => {
    const { fixture, page } = render();
    backend
      .expectOne(path)
      .flush({ code: 'not_found', title: 'Not found.' }, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();

    expect(page.textContent).toContain('Secret not found.');
    expect(page.querySelector('h1')).toBeNull();
  });

  it('sends a refused caller to the Denied page with the secret named and the reasons in memory', async () => {
    let navigate!: ReturnType<typeof vi.spyOn>;
    const { fixture, page } = render(() => {
      navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    });
    backend.expectOne(path).flush(
      {
        code: 'access_denied',
        title: 'The vault refused access to this secret.',
        reasons: ["Clearance 'Standard' is below required sensitivity 'Secret'."],
      },
      forbidden,
    );
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledExactlyOnceWith(`/Account/Denied?secretId=${id}`, {
      state: { reasons: ["Clearance 'Standard' is below required sensitivity 'Secret'."] },
      replaceUrl: true,
    });
    expect(page.textContent).not.toContain('Secret not found.');
  });

  it('shows the record header, trust line, metadata, shares and history, and names the document', async () => {
    const { page } = await loaded();

    expect(page.querySelector('h1')!.textContent).toBe('Deployment_key');
    expect(page.querySelector('a[aria-label="Back to list"]')?.getAttribute('href')).toBe(
      '/Secrets',
    );
    expect(page.querySelector('.trust-line')?.textContent?.trim()).toBe(
      'Secret values · every reveal is logged',
    );
    expect(
      [...page.querySelectorAll('.page-sub .badge')].map((badge) => badge.textContent?.trim()),
    ).toEqual(['Production', 'Confidential', 'GLOBAL']);
    expect(
      [...page.querySelectorAll('.detail-grid dd')].map((value) => value.textContent?.trim()),
    ).toEqual(['AES-256-GCM (envelope)', '2026-10-20 10:05:00', '—', 'Never', '1 archived']);
    expect(page.querySelector('.timeline-head')!.textContent).toBe(
      'Version 1archived 2026-01-05 · by vault-admin',
    );
    expect(page.querySelector('.timeline-note')!.textContent).toBe('quarterly rotation');
    expect([...page.querySelectorAll('tbody td')].map((cell) => cell.textContent?.trim())).toEqual([
      'colleague',
      'vault-admin',
      '2026-10-20 10:05',
      'No expiry',
      'Revoke',
    ]);
    expect(TestBed.inject(Title).getTitle()).toBe('Deployment_key — EclipsVault');
  });

  it('shows a share recipient the value and history with no change controls or sharing', async () => {
    const { page } = await loaded(recipientView);

    expect(buttons(page)).toEqual(['Reveal value', 'Reveal']);
    expect(panel(page, 'Sharing')).toBeUndefined();
    expect(page.querySelector('.danger-zone')).toBeNull();
  });

  it('shows the owner every change control', async () => {
    const { page } = await loaded();

    expect(buttons(page)).toEqual([
      'Reveal value',
      'Rotate value',
      'Rotate secret',
      'Share access',
      'Revoke',
      'Reveal',
      'Restore',
      'Delete',
    ]);
  });

  it('offers upstream rotation for a managed secret and hides Restore', async () => {
    const { page } = await loaded({ ...secret, managed });

    expect(page.querySelector('.notice')!.textContent).toContain(
      'This is the live password of app_login.',
    );
    expect(buttons(page)).toEqual([
      'Reveal value',
      'Rotate upstream',
      'Rotate value manually',
      'Rotate secret',
      'Share access',
      'Revoke',
      'Reveal',
      'Delete',
    ]);
  });

  it.each([
    [{ licensed: true, evaluationKnown: true, evaluationDaysRemaining: null }, ''],
    [
      { licensed: false, evaluationKnown: true, evaluationDaysRemaining: null },
      'Managed rotation is a licensed feature.',
    ],
    [
      { licensed: false, evaluationKnown: true, evaluationDaysRemaining: 1 },
      'Evaluating managed rotation — 1 day left.',
    ],
    [
      { licensed: false, evaluationKnown: true, evaluationDaysRemaining: 12 },
      'Evaluating managed rotation — 12 days left.',
    ],
    [
      { licensed: false, evaluationKnown: true, evaluationDaysRemaining: 0 },
      'The evaluation period for managed rotation has ended.',
    ],
    [{ licensed: false, evaluationKnown: false, evaluationDaysRemaining: null }, ''],
  ])('shows the licence note for %o as "%s"', async (licence, note) => {
    const { page } = await loaded({ ...secret, managed: { principal: 'app_login', ...licence } });

    expect(page.querySelector('.licence-note > strong')?.textContent?.trim() ?? '').toBe(note);
  });

  it('reveals the current value with its audit sentence', async () => {
    const { fixture, page } = await loaded();
    button(page, 'Reveal value').click();
    backend
      .expectOne({ method: 'POST', url: `${path}/reveal` })
      .flush({ label: 'current value', value: 's3cret-value' });
    await fixture.whenStable();

    expect(page.querySelector('.revealed-head strong')!.textContent).toBe(
      'Decrypted current value',
    );
    expect(page.querySelector('#revealed-value')!.textContent).toBe('s3cret-value');
    expect(page.querySelector('.revealed-box p')!.textContent?.replace(/\s+/g, ' ').trim()).toBe(
      'This reveal was recorded in the audit trail before decryption. The value is shown once — reload to hide it.',
    );
  });

  it('reveals an archived version under its number', async () => {
    const { fixture, page } = await loaded();
    button(page, 'Reveal').click();
    backend
      .expectOne({ method: 'POST', url: `${path}/versions/${version.versionId}/reveal` })
      .flush({ label: 'version 1', value: 'old-value' });
    await fixture.whenStable();

    expect(page.querySelector('.revealed-head strong')!.textContent).toBe('Decrypted version 1');
    expect(page.querySelector('#revealed-value')!.textContent).toBe('old-value');
  });

  it('asks for an authenticator code when a reveal needs fresh proof, then reveals once', async () => {
    const { fixture, page } = await loaded();
    button(page, 'Reveal value').click();
    backend.expectOne(`${path}/reveal`).flush(staleProof, forbidden);
    await fixture.whenStable();
    const dialog = stepUpDialog(page);
    expect(dialog.hasAttribute('open')).toBe(true);

    dialog.querySelector('input')!.value = '123456';
    submit(dialog.querySelector('form')!);
    const proof = backend.expectOne({ method: 'POST', url: '/api/ui/step-up' });
    expect(proof.request.body).toEqual({ code: '123456' });
    proof.flush(null, noContent);
    await fixture.whenStable();
    backend.expectOne(`${path}/reveal`).flush({ label: 'current value', value: 's3cret-value' });
    await fixture.whenStable();

    expect(dialog.hasAttribute('open')).toBe(false);
    expect(page.querySelector('#revealed-value')!.textContent).toBe('s3cret-value');
  });

  it('reveals nothing when the authenticator dialog is cancelled', async () => {
    const { fixture, page } = await loaded();
    button(page, 'Reveal value').click();
    backend.expectOne(`${path}/reveal`).flush(staleProof, forbidden);
    await fixture.whenStable();

    button(stepUpDialog(page), 'Cancel').click();
    await fixture.whenStable();

    backend.expectNone(`${path}/reveal`);
    expect(page.querySelector('#revealed-value')).toBeNull();
    expect(announced(page)).toEqual({ success: '', error: '' });
    expect(button(page, 'Reveal value').disabled).toBe(false);
  });

  it('asks for fresh proof only once and announces a second refusal as an error', async () => {
    const { fixture, page } = await loaded();
    button(page, 'Reveal value').click();
    backend.expectOne(`${path}/reveal`).flush(staleProof, forbidden);
    await fixture.whenStable();
    const dialog = stepUpDialog(page);
    dialog.querySelector('input')!.value = '123456';
    submit(dialog.querySelector('form')!);
    backend.expectOne('/api/ui/step-up').flush(null, noContent);
    await fixture.whenStable();

    backend.expectOne(`${path}/reveal`).flush(staleProof, forbidden);
    await fixture.whenStable();

    expect(dialog.hasAttribute('open')).toBe(false);
    expect(announced(page)).toEqual({ success: '', error: staleProof.title });
  });

  it('rotates after confirmation, announces it and reloads without the revealed value', async () => {
    const { fixture, page } = await loaded();
    button(page, 'Reveal value').click();
    backend.expectOne(`${path}/reveal`).flush({ label: 'current value', value: 's3cret-value' });
    await fixture.whenStable();
    type(page.querySelector('#changeNote')!, 'quarterly rotation');
    await rotate(fixture, page);
    expect(confirmation(page).textContent).toContain(
      "Rotate 'Deployment_key'? The current value is archived to history and replaced.",
    );

    const request = backend.expectOne({ method: 'POST', url: `${path}/rotate` });
    expect(request.request.body).toEqual({
      newValue: 'new-value',
      changeNote: 'quarterly rotation',
      renewTtlDays: null,
    });
    request.flush(null, noContent);
    await reloaded(fixture);

    expect(announced(page)).toEqual({ success: rotated, error: '' });
    expect(page.querySelector('#revealed-value')).toBeNull();
  });

  it('rotates with a renewal and names the days', async () => {
    const { fixture, page } = await loaded();
    type(page.querySelector('#renewTtlDays')!, '30');
    await rotate(fixture, page);

    const request = backend.expectOne(`${path}/rotate`);
    expect(request.request.body).toEqual({
      newValue: 'new-value',
      changeNote: '',
      renewTtlDays: 30,
    });
    request.flush(null, noContent);
    await reloaded(fixture);

    expect(announced(page)).toEqual({
      success:
        'Secret rotated and renewed for 30 more day(s). The previous value was archived to version history.',
      error: '',
    });
  });

  it('rotates nothing when the confirmation is cancelled', async () => {
    const { fixture, page } = await loaded();
    type(page.querySelector('#newValue')!, 'new-value');
    submit(page.querySelector('form.rotate-form')!);
    await fixture.whenStable();

    button(confirmation(page), 'Cancel').click();

    backend.expectNone(`${path}/rotate`);
  });

  it('shows a refused rotation’s field messages under their fields', async () => {
    const { fixture, page } = await loaded();
    await rotate(fixture, page);
    backend.expectOne(`${path}/rotate`).flush(
      {
        code: 'validation_failed',
        title: 'Enter the new value to rotate this secret.',
        errors: { renewTtlDays: ['Renew for must be between 1 and 3650.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();

    expect(announced(page)).toEqual({
      success: '',
      error: 'Enter the new value to rotate this secret.',
    });
    expect(
      [...page.querySelectorAll('.rotate-form .field-error')].map((error) => error.textContent),
    ).toEqual(['', '', 'Renew for must be between 1 and 3650.']);
    expect(page.querySelector<HTMLTextAreaElement>('#newValue')!.value).toBe('new-value');
  });

  it('announces a secret that changed meanwhile and changes nothing', async () => {
    const { fixture, page } = await loaded();
    await rotate(fixture, page);
    backend.expectOne(`${path}/rotate`).flush(
      {
        code: 'secret_changed',
        title: 'This secret changed while you were working on it. Reload and try again.',
      },
      conflict,
    );
    await fixture.whenStable();

    expect(announced(page)).toEqual({
      success: '',
      error: 'This secret changed while you were working on it. Reload and try again.',
    });
  });

  it('rotates a managed secret upstream after confirmation', async () => {
    const { fixture, page } = await loaded({ ...secret, managed });
    type(page.querySelector('input[aria-label="Renew for days"]')!, '7');
    submit(page.querySelector('form.inline-form')!);
    await confirm(fixture, page, 'Rotate upstream');
    expect(confirmation(page).textContent).toContain(
      "Rotate the real password of 'app_login'? Anything still using the old password stops working immediately.",
    );
    expect(confirmation(page).querySelector('.dialog-note')?.textContent).toBe(
      'Saved to the audit log',
    );

    const request = backend.expectOne({ method: 'POST', url: `${path}/rotate-managed` });
    expect(request.request.body).toEqual({ renewTtlDays: 7 });
    request.flush(null, noContent);
    await reloaded(fixture, { ...secret, managed });

    expect(announced(page)).toEqual({
      success:
        'Rotated upstream. The vault generated a new password, changed the real credential, and stored it — the previous value was archived to version history.',
      error: '',
    });
  });

  it.each([
    [
      402,
      'premium_not_licensed',
      'The 30-day evaluation period for managed rotation has ended. Rotating the real credential needs a licence — the stored value is unaffected.',
    ],
    [409, 'secret_changed', 'This secret changed while you were working on it. Nothing changed.'],
    [
      409,
      'change_in_progress',
      'Another change to this secret is in progress. Try again in a moment.',
    ],
  ])(
    'announces a managed rotation refused with %i %s by its title',
    async (status, code, title) => {
      const { fixture, page } = await loaded({ ...secret, managed });
      submit(page.querySelector('form.inline-form')!);
      await confirm(fixture, page, 'Rotate upstream');
      backend
        .expectOne(`${path}/rotate-managed`)
        .flush({ code, title }, { status, statusText: 'Refused' });
      await fixture.whenStable();

      expect(announced(page)).toEqual({ success: '', error: title });
    },
  );

  it('restores a version after confirmation', async () => {
    const { fixture, page } = await loaded();
    button(page, 'Restore').click();
    await confirm(fixture, page, 'Restore version');
    expect(confirmation(page).textContent).toContain(
      'Restore version 1 as the live value? The current value will be archived first.',
    );

    backend
      .expectOne({ method: 'POST', url: `${path}/versions/${version.versionId}/restore` })
      .flush(null, noContent);
    await reloaded(fixture);

    expect(announced(page)).toEqual({
      success: 'Secret reverted to the selected version. The value it replaced was archived.',
      error: '',
    });
  });

  it('shares with the recipient as typed and announces it', async () => {
    const { fixture, page } = await loaded();
    type(page.querySelector('input[aria-label="User to share with"]')!, 'Colleague@example.com');
    type(page.querySelector('input[aria-label="Access expires in days"]')!, '14');
    await fixture.whenStable();
    submit(page.querySelector('form.add-network-form')!);

    const request = backend.expectOne({ method: 'POST', url: `${path}/shares` });
    expect(request.request.body).toEqual({ recipient: 'Colleague@example.com', ttlDays: 14 });
    request.flush(null, noContent);
    await reloaded(fixture);

    expect(announced(page)).toEqual({
      success: "Access to 'Deployment_key' was shared with Colleague@example.com.",
      error: '',
    });
    expect(
      page.querySelector<HTMLInputElement>('input[aria-label="User to share with"]')!.value,
    ).toBe('');
  });

  it('asks for fresh proof before a sensitive share, then shares once', async () => {
    const { fixture, page } = await loaded();
    type(page.querySelector('input[aria-label="User to share with"]')!, 'colleague');
    submit(page.querySelector('form.add-network-form')!);
    backend
      .expectOne(`${path}/shares`)
      .flush({ ...staleProof, title: "Confirm it's you before sharing this secret." }, forbidden);
    await fixture.whenStable();
    const dialog = stepUpDialog(page);
    dialog.querySelector('input')!.value = '123456';
    submit(dialog.querySelector('form')!);
    backend.expectOne('/api/ui/step-up').flush(null, noContent);
    await fixture.whenStable();

    const retried = backend.expectOne(`${path}/shares`);
    expect(retried.request.body).toEqual({ recipient: 'colleague', ttlDays: 0 });
    retried.flush(null, noContent);
    await reloaded(fixture);

    expect(announced(page)).toEqual({
      success: "Access to 'Deployment_key' was shared with colleague.",
      error: '',
    });
  });

  it.each([
    ['share_exists', 'They already have a share on this secret.'],
    ['share_refused', "'colleague' does not have the clearance this secret needs."],
  ])(
    'announces a share refused as %s by its title and keeps what was typed',
    async (code, title) => {
      const { fixture, page } = await loaded();
      type(page.querySelector('input[aria-label="User to share with"]')!, 'colleague');
      submit(page.querySelector('form.add-network-form')!);
      backend.expectOne(`${path}/shares`).flush({ code, title }, conflict);
      await fixture.whenStable();

      expect(announced(page)).toEqual({ success: '', error: title });
      expect(
        page.querySelector<HTMLInputElement>('input[aria-label="User to share with"]')!.value,
      ).toBe('colleague');
    },
  );

  it('shows a refused share’s field message under the form', async () => {
    const { fixture, page } = await loaded();
    submit(page.querySelector('form.add-network-form')!);
    backend.expectOne(`${path}/shares`).flush(
      {
        code: 'validation_failed',
        title: 'Enter the username or email of the person to share with.',
        errors: { recipient: ['The Recipient field is required.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();

    expect(page.querySelector('form.add-network-form ~ .field-error')!.textContent).toBe(
      'The Recipient field is required.',
    );
  });

  it('revokes a share after confirmation', async () => {
    const { fixture, page } = await loaded();
    button(page, 'Revoke').click();
    await confirm(fixture, page, 'Revoke access');
    expect(confirmation(page).textContent).toContain(
      "Revoke colleague's access to 'Deployment_key'?",
    );

    backend
      .expectOne({ method: 'DELETE', url: `${path}/shares/${share.shareId}` })
      .flush(null, noContent);
    await reloaded(fixture, { ...secret, shares: [] });

    expect(announced(page)).toEqual({ success: 'Access revoked.', error: '' });
    expect(page.textContent).toContain('This secret is not shared with anyone.');
  });

  it('announces a share that was already gone', async () => {
    const { fixture, page } = await loaded();
    button(page, 'Revoke').click();
    await confirm(fixture, page, 'Revoke access');
    backend
      .expectOne(`${path}/shares/${share.shareId}`)
      .flush(
        { code: 'share_not_found', title: 'That share no longer exists.' },
        { status: 404, statusText: 'Not Found' },
      );
    await fixture.whenStable();

    expect(announced(page)).toEqual({ success: '', error: 'That share no longer exists.' });
  });

  it('deletes after confirmation and returns to the list', async () => {
    const { fixture, page } = await loaded();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    button(page, 'Delete').click();
    await confirm(fixture, page, 'Delete secret');
    expect(confirmation(page).textContent).toContain(
      "Delete 'Deployment_key' and its entire version history permanently? This cannot be undone and will be audited.",
    );

    backend.expectOne({ method: 'DELETE', url: path }).flush(null, noContent);
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledExactlyOnceWith('/Secrets?deleted=1');
  });

  it('announces a refused change by its title', async () => {
    const { fixture, page } = await loaded();
    button(page, 'Delete').click();
    await confirm(fixture, page, 'Delete secret');
    backend.expectOne({ method: 'DELETE', url: path }).flush(
      {
        code: 'not_permitted',
        title: "Only an administrator or a member of this secret's project can change it.",
      },
      forbidden,
    );
    await fixture.whenStable();

    expect(announced(page)).toEqual({
      success: '',
      error: "Only an administrator or a member of this secret's project can change it.",
    });
  });

  it('announces a secret New secret just stored', async () => {
    const { page } = await loaded(secret, () =>
      vi.spyOn(TestBed.inject(Router), 'currentNavigation').mockReturnValue({
        trigger: 'imperative',
        previousNavigation: {},
        extras: { state: { stored: true } },
      } as unknown as ReturnType<Router['currentNavigation']>),
    );

    expect(announced(page)).toEqual({
      success: "Secret 'Deployment_key' was envelope-encrypted and stored.",
      error: '',
    });
  });
});
