import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import type { components } from '../ui-api/schema.generated';
import { attachmentName } from '../shared/attachment';
import { DataPage } from './data-page';

const yours: components['schemas']['DataResponse'] = {
  account: {
    username: 'saba',
    displayName: 'Saba Sabashvili',
    email: 'saba.sabashvili.1@vault.local',
    clearance: 'TopSecret',
    projectKey: 'GLOBAL',
    hasCustomAvatar: false,
  },
  twoStepEnabled: true,
  passkeyCount: 2,
  backupCodesRemaining: 7,
  deviceCount: 3,
  accessRequestCount: 1,
  activityCount: 42,
  notice: 'This file never includes secret values.',
};
const disposition =
  "attachment; filename=eclipsvault-data-saba-20261021.json; filename*=UTF-8''eclipsvault-data-saba-20261021.json";

describe('DataPage', () => {
  let backend: HttpTestingController;
  let saved: { href: string; download: string }[];
  let objectUrlEvents: string[];
  const createObjectURL = vi.fn(() => 'blob:https://vault.local/export');
  const revokeObjectURL = vi.fn((url: string) => objectUrlEvents.push(`revoked ${url}`));

  beforeEach(() => {
    saved = [];
    objectUrlEvents = [];
    createObjectURL.mockClear();
    revokeObjectURL.mockClear();
    // jsdom implements neither, and a real object URL would outlive the test.
    URL.createObjectURL = createObjectURL;
    URL.revokeObjectURL = revokeObjectURL;
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      saved.push({ href: this.href, download: this.download });
      objectUrlEvents.push(`clicked ${this.href}`);
    });
  });

  afterEach(() => {
    backend.verify();
    vi.restoreAllMocks();
  });

  function render() {
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting()],
    });
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(DataPage);
    fixture.detectChanges();
    return { fixture, page: fixture.nativeElement as HTMLElement };
  }

  function button(page: HTMLElement, text: string): HTMLButtonElement {
    return [...page.querySelectorAll('button')].find(
      (candidate) => candidate.textContent?.trim() === text,
    )!;
  }

  function factRows(list: ParentNode): string[][] {
    return [...list.querySelectorAll('dt')].map((term) => [
      term.textContent!.trim(),
      term.nextElementSibling!.textContent!.trim(),
    ]);
  }

  it('shows loading, and offers no download, until the server answers', () => {
    const { page } = render();
    expect(page.querySelector('[role="status"]:not(:empty)')?.textContent).toContain(
      'Loading your data…',
    );
    expect(page.querySelector('dl')).toBeNull();
    expect(page.querySelectorAll('button')).toHaveLength(0);
    backend.expectOne('/api/ui/data').flush(yours);
  });

  it('offers a retry after a failed fetch and shows the data it then receives', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/data').flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(page.textContent).toContain('Could not load your data. Try again.');
    expect([...page.querySelectorAll('button')].map((shown) => shown.textContent?.trim())).toEqual([
      'Try again',
    ]);
    button(page, 'Try again').click();
    fixture.detectChanges();
    backend.expectOne('/api/ui/data').flush(yours);
    await fixture.whenStable();

    expect(page.textContent).not.toContain('Could not load');
    expect(factRows(page.querySelector('dl')!)[0]).toEqual(['Username', 'saba']);
  });

  it('shows the account, what the export includes, the notice and one download', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/data').flush(yours);
    await fixture.whenStable();

    expect([...page.querySelectorAll('h1, h2')].map((heading) => heading.textContent)).toEqual([
      'Your data',
      'Account',
      'What the export includes',
    ]);
    const [account, included] = [...page.querySelectorAll('dl')];
    expect(factRows(account)).toEqual([
      ['Username', 'saba'],
      ['Display name', 'Saba Sabashvili'],
      ['Email', 'saba.sabashvili.1@vault.local'],
      ['Clearance', 'TopSecret'],
      ['Project', 'GLOBAL'],
      ['Profile picture', 'Generated identicon'],
    ]);
    expect(factRows(included)).toEqual([
      ['Two-step sign-in', 'On'],
      ['Passkeys', '2'],
      ['Backup codes', '7'],
      ['Signed-in devices', '3'],
      ['Access requests', '1'],
      ['Activity entries', '42'],
    ]);
    expect(page.textContent).toContain('This file never includes secret values.');
    expect(button(page, 'Download your data (JSON)')).toBeDefined();
  });

  it('shows a custom picture and two-step sign-in turned off', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/data').flush({
      ...yours,
      account: { ...yours.account, hasCustomAvatar: true },
      twoStepEnabled: false,
    });
    await fixture.whenStable();

    const [account, included] = [...page.querySelectorAll('dl')];
    expect(factRows(account).at(-1)).toEqual(['Profile picture', 'Custom image']);
    expect(factRows(included)[0]).toEqual(['Two-step sign-in', 'Off']);
  });

  it('Download your data (JSON) posts with the antiforgery header and saves the file under its server name, then revokes the URL', async () => {
    const { fixture, page } = render();
    document.cookie = 'XSRF-TOKEN=data-request-token; path=/';
    backend.expectOne('/api/ui/data').flush(yours);
    await fixture.whenStable();
    const file = new Blob(['{"SchemaVersion": "1.0"}'], { type: 'application/json' });

    button(page, 'Download your data (JSON)').click();
    fixture.detectChanges();
    const exported = backend.expectOne({ method: 'POST', url: '/api/ui/data/export' });
    expect(exported.request.headers.get('RequestVerificationToken')).toBe('data-request-token');
    expect(button(page, 'Download your data (JSON)').disabled).toBe(true);
    exported.flush(file, { headers: { 'Content-Disposition': disposition } });
    await fixture.whenStable();

    expect(createObjectURL).toHaveBeenCalledExactlyOnceWith(file);
    expect(saved).toEqual([
      {
        href: 'blob:https://vault.local/export',
        download: 'eclipsvault-data-saba-20261021.json',
      },
    ]);
    expect(objectUrlEvents).toEqual([
      'clicked blob:https://vault.local/export',
      'revoked blob:https://vault.local/export',
    ]);
    expect(button(page, 'Download your data (JSON)').disabled).toBe(false);
    document.cookie = 'XSRF-TOKEN=; path=/; max-age=0';
  });

  it('a refused download announces why and saves no file', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/data').flush(yours);
    await fixture.whenStable();
    const refusal = 'The audit trail is unavailable, so the vault refused the request.';

    button(page, 'Download your data (JSON)').click();
    backend
      .expectOne('/api/ui/data/export')
      .flush(
        new Blob([JSON.stringify({ status: 503, code: 'audit_unavailable', title: refusal })]),
        {
          status: 503,
          statusText: 'Unavailable',
        },
      );
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.querySelector('[role="alert"]')?.textContent?.trim()).toBe(refusal);
    });

    expect(createObjectURL).not.toHaveBeenCalled();
    expect(saved).toEqual([]);
    expect(button(page, 'Download your data (JSON)').disabled).toBe(false);
  });

  it('a download that fails without Problem Details shows the generic failure and saves no file', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/data').flush(yours);
    await fixture.whenStable();

    button(page, 'Download your data (JSON)').click();
    backend.expectOne('/api/ui/data/export').error(new ProgressEvent('error'));
    await fixture.whenStable();

    expect(page.querySelector('[role="alert"]')?.textContent?.trim()).toBe(
      'The vault could not complete the request.',
    );
    expect(saved).toEqual([]);
  });

  it('a successful download clears the previous failure', async () => {
    const { fixture, page } = render();
    backend.expectOne('/api/ui/data').flush(yours);
    await fixture.whenStable();
    button(page, 'Download your data (JSON)').click();
    backend.expectOne('/api/ui/data/export').error(new ProgressEvent('error'));
    await fixture.whenStable();

    button(page, 'Download your data (JSON)').click();
    backend
      .expectOne('/api/ui/data/export')
      .flush(new Blob(['{}']), { headers: { 'Content-Disposition': disposition } });
    await fixture.whenStable();

    expect(page.querySelector('[role="alert"]')?.textContent?.trim()).toBe('');
    expect(saved).toHaveLength(1);
  });
});

describe('attachmentName', () => {
  it.each([
    [disposition, 'eclipsvault-data-saba-20261021.json'],
    [
      "attachment; filename*=UTF-8''eclipsvault-data-s%C3%A1ba-20261021.json",
      'eclipsvault-data-sába-20261021.json',
    ],
    [
      'attachment; filename="eclipsvault-data-saba-20261021.json"',
      'eclipsvault-data-saba-20261021.json',
    ],
    ['attachment', ''],
    [null, ''],
  ])('names the file from %s as %s', (header, expected) => {
    expect(attachmentName(header)).toBe(expected);
  });
});
