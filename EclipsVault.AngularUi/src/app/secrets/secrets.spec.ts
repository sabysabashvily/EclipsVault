import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { appConfig } from '../app.config';
import type { Me } from '../account/me';
import { PAGE_LOCATION } from '../shell/command-palette';
import type { components } from '../ui-api/schema.generated';
import { NewSecretPage } from './new-secret-page';
import { SecretsPage } from './secrets-page';

type Secret = components['schemas']['SecretListItemResponse'];

const now = new Date('2026-11-15T12:00:00Z');
const expiringSoon: Secret = {
  secretId: '00000000-0000-4000-8000-000000000001',
  name: 'Phoenix_Db_Password',
  project: 'PHOENIX',
  environment: 'Production',
  sensitivity: 3,
  sensitivityLabel: 'Secret',
  createdAtUtc: '2026-11-01T08:00:00Z',
  expiresAtUtc: '2026-11-20T09:30:00Z',
};
const expiringLater: Secret = {
  secretId: '00000000-0000-4000-8000-000000000002',
  name: 'Billing_Api_Key',
  project: 'BILLING',
  environment: 'Staging',
  sensitivity: 1,
  sensitivityLabel: 'Internal',
  createdAtUtc: '2026-11-02T08:00:00Z',
  expiresAtUtc: '2026-12-31T00:00:00Z',
};
const neverExpiring: Secret = {
  secretId: '00000000-0000-4000-8000-000000000003',
  name: 'Ci_Token',
  project: 'PHOENIX',
  environment: 'Development',
  sensitivity: 4,
  sensitivityLabel: 'Top Secret',
  createdAtUtc: '2026-11-03T08:00:00Z',
  expiresAtUtc: null,
};
const me: Me = {
  username: 'alice',
  displayName: 'Alice',
  clearance: 'Elevated',
  project: 'PHOENIX',
  areas: ['overview', 'secrets'],
  avatarUrl: '/Profile/Avatar',
  licenseNotice: null,
};

let backend: HttpTestingController;
const assign = vi.fn();

/** The element's words as a reader sees them. */
function text(element: Element | null | undefined): string {
  return (element?.textContent ?? '').replace(/\s+/g, ' ').trim();
}
function configure() {
  TestBed.configureTestingModule({
    providers: [
      ...appConfig.providers,
      provideHttpClientTesting(),
      { provide: PAGE_LOCATION, useValue: { assign } },
    ],
  });
  backend = TestBed.inject(HttpTestingController);
}
function render<T>(page: Type<T>) {
  configure();
  const fixture = TestBed.createComponent(page);
  fixture.detectChanges();
  return { fixture, page: fixture.nativeElement as HTMLElement };
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(now);
});
afterEach(() => {
  try {
    backend.verify();
  } finally {
    TestBed.resetTestingModule();
    vi.useRealTimers();
    assign.mockReset();
  }
});

describe('Secrets', () => {
  async function loaded(secrets: Secret[]) {
    const rendered = render(SecretsPage);
    backend.expectOne('/api/ui/secrets').flush({ secrets });
    await rendered.fixture.whenStable();
    return rendered;
  }
  const rows = (page: HTMLElement) =>
    [...page.querySelectorAll('tbody tr')].map((row) =>
      [...row.querySelectorAll('td')].map((cell) => text(cell)),
    );

  it('shows loading until the list arrives', async () => {
    const { page, fixture } = render(SecretsPage);
    expect(text(page.querySelector('p[role="status"]'))).toBe('Loading secrets…');
    backend.expectOne('/api/ui/secrets').flush({ secrets: [] });
    await fixture.whenStable();
    expect(page.querySelector('p[role="status"]')).toBeNull();
  });

  it('shows a failed load with Try again, which loads the list', async () => {
    const { page, fixture } = render(SecretsPage);
    backend.expectOne('/api/ui/secrets').flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(text(page.querySelector('p[role="alert"]'))).toBe('Could not load secrets. Try again.');
    [...page.querySelectorAll('button')].find((button) => text(button) === 'Try again')!.click();
    fixture.detectChanges();
    backend.expectOne('/api/ui/secrets').flush({ secrets: [neverExpiring] });
    await fixture.whenStable();
    expect(page.querySelector('p[role="alert"]')).toBeNull();
    expect(rows(page)).toHaveLength(1);
  });

  it('lists each secret as Legacy did, marking one that expires within 7 days', async () => {
    const { page } = await loaded([expiringSoon, expiringLater, neverExpiring]);

    expect(text(page.querySelector('.page-sub'))).toBe(
      '3 active secrets. Opening one records an audit entry.',
    );
    expect([...page.querySelectorAll('th')].map((header) => text(header))).toEqual([
      'Name',
      'Project',
      'Environment',
      'Sensitivity',
      'Expires (UTC)',
      '',
    ]);
    expect(rows(page)).toEqual([
      ['Phoenix_Db_Password', 'PHOENIX', 'Production', 'Secret', '2026-11-20 09:30 soon', 'Open'],
      ['Billing_Api_Key', 'BILLING', 'Staging', 'Internal', '2026-12-31 00:00', 'Open'],
      ['Ci_Token', 'PHOENIX', 'Development', 'Top Secret', '—', 'Open'],
    ]);
    expect([...page.querySelectorAll('tbody a')].map((link) => link.getAttribute('href'))).toEqual([
      '/Secrets/Details/00000000-0000-4000-8000-000000000001',
      '/Secrets/Details/00000000-0000-4000-8000-000000000002',
      '/Secrets/Details/00000000-0000-4000-8000-000000000003',
    ]);
    expect(page.querySelector('.page-actions a')?.getAttribute('href')).toBe('/Secrets/Create');
  });

  it('opens a secret when its row is clicked anywhere but a control, as Legacy rows did', async () => {
    const { page } = await loaded([expiringSoon, neverExpiring]);
    const [first, second] = [...page.querySelectorAll<HTMLElement>('tbody tr')];

    first.querySelector('td')!.click();
    expect(assign).toHaveBeenCalledExactlyOnceWith(
      '/Secrets/Details/00000000-0000-4000-8000-000000000001',
    );

    assign.mockReset();
    const open = second.querySelector('a')!;
    open.addEventListener('click', (event) => event.preventDefault());
    open.click();
    expect(assign).not.toHaveBeenCalled();
  });

  it('shows a deletion once and drops it from the address', async () => {
    configure();
    const harness = await RouterTestingHarness.create('/Secrets?deleted=1');
    backend.expectOne('/api/ui/secrets').flush({ secrets: [] });
    await harness.fixture.whenStable();

    expect(text(harness.routeNativeElement?.querySelector('div[role="status"]'))).toBe(
      'Secret deleted. The action was recorded in the audit trail.',
    );
    expect(TestBed.inject(Router).url).toBe('/Secrets');
  });

  it('counts a single secret in the singular', async () => {
    const { page } = await loaded([neverExpiring]);
    expect(text(page.querySelector('.page-sub'))).toBe(
      '1 active secret. Opening one records an audit entry.',
    );
  });

  it('filters rows by what they show and keeps the count of every secret', async () => {
    const { page, fixture } = await loaded([expiringSoon, expiringLater, neverExpiring]);
    const filter = page.querySelector<HTMLInputElement>('input[type="search"]')!;
    expect(filter.getAttribute('aria-label')).toBe('Filter secrets');
    expect(filter.placeholder).toBe('Filter by name, project, environment…');

    filter.value = 'phoenix';
    filter.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(rows(page).map(([name]) => name)).toEqual(['Phoenix_Db_Password', 'Ci_Token']);

    filter.value = ' SOON ';
    filter.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    expect(rows(page).map(([name]) => name)).toEqual(['Phoenix_Db_Password']);
    expect(text(page.querySelector('.page-sub'))).toBe(
      '3 active secrets. Opening one records an audit entry.',
    );
  });

  it('shows the empty vault with a way to store the first secret', async () => {
    const { page } = await loaded([]);
    expect(text(page.querySelector('.page-sub'))).toBe(
      '0 active secrets. Opening one records an audit entry.',
    );
    expect(page.querySelector('table')).toBeNull();
    expect(text(page.querySelector('.empty-state h3'))).toBe('The vault is empty');
    expect(text(page.querySelector('.empty-state p'))).toBe(
      "Store your first secret — it's sealed with AES-256-GCM envelope encryption before it ever touches the database.",
    );
    const store = page.querySelector('.empty-state a');
    expect([text(store), store?.getAttribute('href')]).toEqual([
      'Store a secret',
      '/Secrets/Create',
    ]);
  });
});

describe('New secret', () => {
  async function opened() {
    const rendered = render(NewSecretPage);
    backend.expectOne('/api/ui/me').flush(me);
    await rendered.fixture.whenStable();
    const { page, fixture } = rendered;
    const field = <E extends HTMLElement>(label: string) =>
      page.querySelector<E>(
        `#${[...page.querySelectorAll('label')].find((each) => text(each) === label)!.htmlFor}`,
      )!;
    const fill = async (label: string, value: string) => {
      const input = field<HTMLInputElement>(label);
      input.value = value;
      input.dispatchEvent(new Event(input instanceof HTMLSelectElement ? 'change' : 'input'));
      await fixture.whenStable();
    };
    const submit = async () => {
      page.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
      await fixture.whenStable();
    };
    const errorOf = (label: string) => text(field(label).nextElementSibling);
    return { page, fixture, field, fill, submit, errorOf };
  }
  async function filled() {
    const form = await opened();
    await form.fill('Name', 'Phoenix_Prod_Db_Password');
    await form.fill('Secret value', 's3cr3t');
    await form.fill('Project key', 'billing');
    await form.fill('TTL in days (0 = never expires)', '30');
    await form.fill('Environment', 'Staging');
    await form.fill('Sensitivity', '2');
    return form;
  }
  const values = (form: Awaited<ReturnType<typeof opened>>) =>
    [
      'Name',
      'Secret value',
      'Project key',
      'TTL in days (0 = never expires)',
      'Environment',
      'Sensitivity',
    ].map((label) => form.field<HTMLInputElement>(label).value);

  it('offers the Legacy form, defaulting to the caller’s own project', async () => {
    const form = await opened();
    expect(text(form.page.querySelector('h1'))).toBe('New secret');
    expect(values(form)).toEqual(['', '', 'PHOENIX', '0', 'Development', '1']);
    expect(
      [...form.field<HTMLSelectElement>('Environment').options].map((option) => text(option)),
    ).toEqual(['Development', 'Staging', 'Production']);
    expect(
      [...form.field<HTMLSelectElement>('Sensitivity').options].map((option) => [
        option.value,
        text(option),
      ]),
    ).toEqual([
      ['1', 'Internal'],
      ['2', 'Confidential'],
      ['3', 'Secret'],
      ['4', 'Top Secret'],
    ]);
    expect(text(form.page.querySelector('button[type="submit"]'))).toBe('Encrypt and store');
  });

  it('stores the secret and opens its details page', async () => {
    const form = await filled();
    await form.submit();

    const request = backend.expectOne('/api/ui/secrets');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      name: 'Phoenix_Prod_Db_Password',
      value: 's3cr3t',
      project: 'billing',
      environment: 'Staging',
      sensitivity: 2,
      ttlDays: 30,
    });
    expect(form.page.querySelector('fieldset')!.disabled).toBe(true);
    request.flush(
      { secretId: '00000000-0000-4000-8000-000000000009' },
      { status: 201, statusText: 'Created' },
    );
    await form.fixture.whenStable();

    expect(assign).toHaveBeenCalledExactlyOnceWith(
      '/Secrets/Details/00000000-0000-4000-8000-000000000009',
    );
  });

  it('names a taken name and keeps everything typed', async () => {
    const form = await filled();
    await form.submit();
    backend
      .expectOne('/api/ui/secrets')
      .flush(
        { status: 409, code: 'name_taken', title: 'A secret with that name already exists.' },
        { status: 409, statusText: 'Conflict' },
      );
    await form.fixture.whenStable();

    expect(text(form.page.querySelector('[role="alert"]'))).toBe(
      'A secret with that name already exists.',
    );
    expect(values(form)).toEqual([
      'Phoenix_Prod_Db_Password',
      's3cr3t',
      'billing',
      '30',
      'Staging',
      '2',
    ]);
    expect(form.page.querySelector('fieldset')!.disabled).toBe(false);
    expect(assign).not.toHaveBeenCalled();
  });

  it('shows a policy refusal’s reasons together, as Legacy did', async () => {
    const form = await filled();
    await form.submit();
    backend.expectOne('/api/ui/secrets').flush(
      {
        status: 403,
        code: 'creation_denied',
        title: 'The vault refused to create this secret.',
        reasons: [
          "Clearance 'Standard' is below the requested sensitivity 'Confidential'.",
          "Subject project 'PHOENIX' does not permit creating a secret in project 'billing'.",
        ],
      },
      { status: 403, statusText: 'Forbidden' },
    );
    await form.fixture.whenStable();

    expect(text(form.page.querySelector('.validation-summary'))).toBe(
      "Clearance 'Standard' is below the requested sensitivity 'Confidential'. Subject project 'PHOENIX' does not permit creating a secret in project 'billing'.",
    );
    expect(values(form)[0]).toBe('Phoenix_Prod_Db_Password');
  });

  it('shows server validation messages under the text fields', async () => {
    const form = await opened();
    await form.fill('Name', 'ab');
    await form.fill('TTL in days (0 = never expires)', '');
    await form.submit();

    const request = backend.expectOne('/api/ui/secrets');
    expect(request.request.body).toEqual({
      name: 'ab',
      value: '',
      project: 'PHOENIX',
      environment: 'Development',
      sensitivity: 1,
      ttlDays: null,
    });
    request.flush(
      {
        status: 400,
        code: 'validation_failed',
        title: 'Check the highlighted fields.',
        errors: {
          name: [
            'The field Name must be a string with a minimum length of 3 and a maximum length of 128.',
          ],
          value: ['The Secret value field is required.'],
          ttlDays: ["The value '' is invalid."],
        },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await form.fixture.whenStable();

    expect(form.errorOf('Name')).toBe(
      'The field Name must be a string with a minimum length of 3 and a maximum length of 128.',
    );
    expect(form.errorOf('Secret value')).toBe('The Secret value field is required.');
    expect(form.errorOf('TTL in days (0 = never expires)')).toBe("The value '' is invalid.");
    expect(form.errorOf('Project key')).toBe('');
    expect(form.page.querySelector('.validation-summary')).toBeNull();
  });

  it('reports an unavailable audit trail and clears it on the next attempt', async () => {
    const form = await filled();
    await form.submit();
    backend.expectOne('/api/ui/secrets').flush(
      {
        status: 503,
        code: 'audit_unavailable',
        title: 'The audit trail is unavailable, so the vault refused the request.',
      },
      { status: 503, statusText: 'Unavailable' },
    );
    await form.fixture.whenStable();
    expect(text(form.page.querySelector('.validation-summary'))).toBe(
      'The audit trail is unavailable, so the vault refused the request.',
    );

    await form.submit();
    expect(form.page.querySelector('.validation-summary')).toBeNull();
    backend
      .expectOne('/api/ui/secrets')
      .flush({ secretId: '00000000-0000-4000-8000-00000000000a' });
    await form.fixture.whenStable();
    expect(assign).toHaveBeenCalledOnce();
  });
});
