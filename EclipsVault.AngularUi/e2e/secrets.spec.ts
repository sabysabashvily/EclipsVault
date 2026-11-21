import type { Page } from '@playwright/test';
import { savedAdministratorCookies } from './administrator-cookies';
import { AppShell } from './app-shell';
import { test, expect } from './fixtures';
import { expectLegacyShell, legacyShell } from './legacy-shell';
import { NewSecretForm } from './new-secret';
import { openPalette, searchSecrets } from './palette';
import { SecretDetails } from './secret-details';
import { SecretsList } from './secrets-list';

test.describe.configure({ mode: 'serial' });
const created = {
  name: 'E2e_Secrets_Created',
  project: 'GLOBAL',
  sensitivity: 'Internal',
  ttlDays: '3',
};
const typed = 'E2e_Secrets_Typed';
const detailsUrl = /\/Secrets\/Details\/[0-9a-f-]{36}$/;

/** Records each document the browser loads, so a test can tell an in-app link from a page load. */
function recordDocuments(page: Page): string[] {
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(new URL(request.url()).pathname);
  });
  return documents;
}

test('New secret stores a secret and opens its details with the success message', async ({
  page,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const form = new NewSecretForm(page);
  const document = await page.goto('/Secrets/Create');
  expect(document?.headers()['content-security-policy']).toContain(
    "require-trusted-types-for 'script'",
  );
  await expect(page).toHaveTitle('New secret — EclipsVault');
  // The administrator's own project is the default.
  await expect(form.project).toHaveValue('GLOBAL');

  await form.create(created);

  await expect(page).toHaveURL(detailsUrl);
  const details = new SecretDetails(page, created.name);
  await expect(details.heading).toBeVisible();
  await expect(details.storedMessage).toBeVisible();
});

test('a taken name is refused on the form, which keeps what was typed', async ({
  page,
  browserHealth,
}) => {
  // The browser logs the refusal's 409 as a failed request.
  browserHealth.expectConsoleError({ text: /status of 409/, url: /\/api\/ui\/secrets$/ });
  await page.context().addCookies(await savedAdministratorCookies());
  const form = new NewSecretForm(page);
  await form.open();

  await form.create({ ...created, ttlDays: '9' });

  await expect(form.refusal).toHaveText('A secret with that name already exists.');
  await expect(page).toHaveURL(/\/Secrets\/Create$/);
  await expect(form.name).toHaveValue(created.name);
  await expect(form.value).toHaveValue(`e2e-${created.name}`);
  await expect(form.ttlDays).toHaveValue('9');
  await expect(form.submit).toBeEnabled();
});

for (const [ttlDays, refusal] of [
  ['1.5', 'stepMismatch'],
  ['-1', 'rangeUnderflow'],
  ['3651', 'rangeOverflow'],
] as const) {
  test(`the browser refuses a TTL of ${ttlDays} days before anything is sent, as on Legacy`, async ({
    page,
  }) => {
    await page.context().addCookies(await savedAdministratorCookies());
    const form = new NewSecretForm(page);
    const sent: string[] = [];
    page.on('request', (request) => {
      if (request.method() === 'POST' && new URL(request.url()).pathname === '/api/ui/secrets')
        sent.push(request.url());
    });
    await form.open();

    await form.create({
      name: 'E2e_Secrets_Bad_Ttl',
      project: 'GLOBAL',
      sensitivity: 'Internal',
      ttlDays,
    });

    expect(
      await form.ttlDays.evaluate((input: HTMLInputElement, key) => input.validity[key], refusal),
    ).toBe(true);
    await expect(form.ttlDays).toBeFocused();
    await expect(form.refusal).toHaveText('');
    expect(sent).toEqual([]);
  });
}

test('the sidebar opens the Secrets list in place, marking the new secret as expiring soon', async ({
  page,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  await page.goto('/Access');
  await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
  const documents = recordDocuments(page);
  const list = new SecretsList(page);

  await new AppShell(page).secrets.click();

  await expect(list.heading).toBeVisible();
  await expect(page).toHaveURL(/\/Secrets$/);
  await expect(page).toHaveTitle('Secrets — EclipsVault');
  expect(documents).toEqual([]);
  await expect(list.introduction).toHaveText(
    /^\d+ active secrets?(?: · \d+ expires? within 7 days)?\. Opening one records an audit entry\.$/,
  );
  await expect(list.tab('All secrets')).toHaveAttribute('aria-current', 'page');
  const row = list.secret(created.name);
  await expect(list.secretCellOf(row)).toContainText('GLOBAL');
  await expect(list.environmentBadgeOf(row, 'Development')).toBeVisible();
  await expect(list.sensitivityBadgeOf(row, 'Internal')).toBeVisible();
  await expect(list.expiresOf(row)).toContainText(/^In [23] days · /);

  await list.filter.fill('e2e_secrets_c');
  await expect(list.rows).toHaveCount(2);
  await expect(row).toBeVisible();

  await list.openOf(row).click();
  await expect(new SecretDetails(page, created.name).heading).toBeVisible();
  expect(documents).toEqual([new URL(page.url()).pathname]);
});

for (const shell of ['/Access', legacyShell]) {
  test(`the palette on ${shell} finds the secret by name, and Enter opens its details`, async ({
    page,
    browserHealth,
  }) => {
    expectLegacyShell(browserHealth, shell);
    await page.context().addCookies(await savedAdministratorCookies());
    await page.goto(shell);
    await openPalette(page);
    expect(await searchSecrets(page, 'E2e_Secrets_Cr')).toEqual({
      status: 200,
      names: [created.name],
    });
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(detailsUrl);
    await expect(new SecretDetails(page, created.name).heading).toBeVisible();
  });
}

const destinations = {
  Overview: {
    url: /\/$/,
    landmark: (page: Page) => page.getByRole('heading', { name: 'Overview', exact: true }),
  },
  Secrets: { url: /\/Secrets$/, landmark: (page: Page) => new SecretsList(page).heading },
  'New secret': {
    url: /\/Secrets\/Create$/,
    landmark: (page: Page) => new NewSecretForm(page).submit,
  },
};

const sidebarLinks = [
  [legacyShell, 'Overview'],
  [legacyShell, 'Secrets'],
  [legacyShell, 'New secret'],
  ['/Access', 'Overview'],
  ['/Access', 'Secrets'],
] as const;

for (const [shell, label] of sidebarLinks) {
  test(`the sidebar on ${shell} opens ${label}`, async ({ page, browserHealth }) => {
    expectLegacyShell(browserHealth, shell);
    await page.context().addCookies(await savedAdministratorCookies());
    await page.goto(shell);

    await page.getByRole('link', { name: label, exact: true }).click();

    await expect(page).toHaveURL(destinations[label].url);
    await expect(destinations[label].landmark(page)).toBeVisible();
  });
}

// New secret left the Angular sidebar for the palette. Legacy still lists it.
test('the sidebar on /Access omits New secret', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  await page.goto('/Access');

  await expect(page.getByRole('link', { name: 'New secret', exact: true })).toHaveCount(0);
});

for (const shell of [legacyShell, '/Access']) {
  for (const [label, { url, landmark }] of Object.entries(destinations)) {
    test(`the palette's Go to ${label} on ${shell} opens it`, async ({ page, browserHealth }) => {
      expectLegacyShell(browserHealth, shell);
      await page.context().addCookies(await savedAdministratorCookies());
      await page.goto(shell);
      await openPalette(page);

      await page
        .getByRole('option')
        .filter({ has: page.getByText(label, { exact: true }) })
        .click();

      await expect(page).toHaveURL(url);
      await expect(landmark(page)).toBeVisible();
    });
  }
}

test("a secret's Back to list opens the Secrets list", async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const form = new NewSecretForm(page);
  await form.open();
  await form.create({ ...created, name: 'E2e_Secrets_Back_To_List' });
  await expect(new SecretDetails(page, 'E2e_Secrets_Back_To_List').heading).toBeVisible();

  await page.getByRole('link', { name: 'Back to list', exact: true }).click();

  await expect(page).toHaveURL(/\/Secrets$/);
  await expect(new SecretsList(page).heading).toBeVisible();
});

test('the keyboard fills New secret in order, stores it, and opens it from the list', async ({
  page,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const form = new NewSecretForm(page);
  await form.open();
  await form.name.focus();

  await page.keyboard.type(typed);
  await page.keyboard.press('Tab');
  await expect(form.value).toBeFocused();
  await page.keyboard.type('e2e-typed-value');
  await page.keyboard.press('Tab');
  await expect(form.project).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(form.ttlDays).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(form.environment).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(form.sensitivity).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(form.submit).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(new SecretDetails(page, typed).heading).toBeVisible();

  const list = new SecretsList(page);
  await list.open();
  await list.filter.focus();
  await page.keyboard.type(typed);
  await expect(list.rows).toHaveCount(2);
  await page.keyboard.press('Tab');
  await expect(list.openOf(list.secret(typed))).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(detailsUrl);
  await expect(new SecretDetails(page, typed).heading).toBeVisible();
});

const day = 24 * 3_600_000;
const highlightedSecretId = '00000000-0000-4000-8000-0000000000f1';
function highlighted() {
  const now = Date.now();
  return {
    secrets: [
      {
        secretId: highlightedSecretId,
        name: 'E2E_PRODUCTION_SOON',
        project: 'GLOBAL',
        environment: 'Production',
        sensitivity: 2,
        sensitivityLabel: 'Confidential',
        createdAtUtc: new Date(now - day).toISOString(),
        expiresAtUtc: new Date(now + 2.5 * day).toISOString(),
      },
    ],
  };
}
test('the list and the palette show sensitivity, production and expiry, and New secret shows a refusal', async ({
  page,
  browserHealth,
}) => {
  browserHealth.expectConsoleError({ text: /status of 409/, url: /\/api\/ui\/secrets$/ });
  await page.route('**/api/ui/secrets', (route) =>
    route.request().method() === 'GET'
      ? route.fulfill({ json: highlighted() })
      : route.fulfill({
          status: 409,
          contentType: 'application/problem+json',
          json: {
            status: 409,
            code: 'name_taken',
            title: 'A secret with that name already exists.',
          },
        }),
  );
  await page.context().addCookies(await savedAdministratorCookies());
  const list = new SecretsList(page);
  await list.open();
  const row = list.secret('E2E_PRODUCTION_SOON');
  await expect(list.environmentBadgeOf(row, 'Production')).toBeVisible();
  await expect(list.sensitivityBadgeOf(row, 'Confidential')).toBeVisible();
  await expect(list.expiresOf(row).getByText(/^In 2 days · /)).toBeVisible();

  await page.route('**/api/ui/secrets/search?*', (route) =>
    route.fulfill({
      json: [
        {
          id: highlightedSecretId,
          name: 'E2E_PRODUCTION_SOON',
          project: 'GLOBAL',
          environment: 'Production',
          sensitivity: 2,
          sensitivityName: 'Confidential',
          url: `/Secrets/Details/${highlightedSecretId}`,
        },
      ],
    }),
  );
  await openPalette(page);
  await page.getByRole('combobox').fill('E2E_PRODUCTION');
  const found = page.getByRole('option', { name: /E2E_PRODUCTION_SOON/ });
  // Confidential, unlike Secret, cannot be confused with the option's "Secret" kind label.
  await expect(found.getByText('Confidential', { exact: true })).toBeVisible();
  await page.keyboard.press('Escape');

  await list.newSecret.click();
  const form = new NewSecretForm(page);
  await form.create({
    name: 'E2E_REFUSED',
    project: 'GLOBAL',
    sensitivity: 'Internal',
    ttlDays: '0',
  });
  await expect(form.refusal).toHaveText('A secret with that name already exists.');
});

test('the Secrets card tabs switch between All secrets, Shared with me and Shared by me in place', async ({
  page,
}) => {
  await page.context().addCookies(await savedAdministratorCookies());
  const list = new SecretsList(page);
  await list.open();
  await expect(list.tab('All secrets')).toHaveAttribute('aria-current', 'page');
  const documents = recordDocuments(page);

  await list.tab('Shared with me').click();
  await expect(page).toHaveURL(/\/Secrets\/SharedWithMe$/);
  await expect(list.tab('Shared with me')).toHaveAttribute('aria-current', 'page');

  await list.tab('Shared by me').click();
  await expect(page).toHaveURL(/\/Secrets\/SharedByMe$/);
  await expect(list.tab('Shared by me')).toHaveAttribute('aria-current', 'page');

  await list.tab('All secrets').click();
  await expect(page).toHaveURL(/\/Secrets$/);
  await expect(list.tab('All secrets')).toHaveAttribute('aria-current', 'page');
  expect(documents).toEqual([]);
});

for (const width of [300, 2000] as const) {
  test(`the Secrets list has no sideways scroll at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.route('**/api/ui/secrets', (route) => route.fulfill({ json: highlighted() }));
    await page.context().addCookies(await savedAdministratorCookies());
    const list = new SecretsList(page);
    await list.open();
    await expect(list.secret('E2E_PRODUCTION_SOON')).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(width);
  });

  test(`New secret has no sideways scroll at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.context().addCookies(await savedAdministratorCookies());
    const form = new NewSecretForm(page);
    await form.open();
    await expect(form.submit).toBeVisible();
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth)).toBe(width);
  });
}
