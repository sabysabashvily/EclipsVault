import type { Page } from '@playwright/test';
import { savedAdministratorCookies } from './administrator-cookies';
import { AppShell } from './app-shell';
import { test, expect } from './fixtures';
import { NewSecretForm } from './new-secret';
import { openPalette, searchSecrets } from './palette';
import { SecretsList } from './secrets-list';

test.describe.configure({ mode: 'serial' });
const created = {
  name: 'E2e_Secrets_Created',
  project: 'GLOBAL',
  sensitivity: 'Internal',
  ttlDays: '3',
};
const typed = 'E2e_Secrets_Typed';
const details = /\/Secrets\/Details\/[0-9a-f-]{36}$/;

/** Records each document the browser loads, so a test can tell an in-app link from a page load. */
function recordDocuments(page: Page): string[] {
  const documents: string[] = [];
  page.on('request', (request) => {
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(new URL(request.url()).pathname);
  });
  return documents;
}

test('New secret stores a secret and opens its details with the Legacy success message', async ({
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

  await expect(page).toHaveURL(details);
  await expect(page.getByRole('heading', { name: created.name, exact: true })).toBeVisible();
  await expect(
    page.getByText(`Secret '${created.name}' was envelope-encrypted and stored.`),
  ).toBeVisible();
});

test('a taken name is refused on the form, which keeps what was typed', async ({ page }) => {
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
    /^\d+ active secrets?\. Opening one records an audit entry\.$/,
  );
  const row = list.secret(created.name);
  await expect(list.projectOf(row)).toHaveText('GLOBAL');
  await expect(list.environmentBadgeOf(row, 'Development')).toBeVisible();
  await expect(list.sensitivityBadgeOf(row, 'Internal')).toBeVisible();
  await expect(list.expiresOf(row)).toContainText('soon');

  await list.filter.fill('e2e_secrets_c');
  await expect(page.getByRole('row')).toHaveCount(2);
  await expect(row).toBeVisible();

  await list.openOf(row).click();
  await expect(page.getByRole('heading', { name: created.name, exact: true })).toBeVisible();
  expect(documents).toEqual([new URL(page.url()).pathname]);
});

test('both palettes find the secret by name, and Enter opens its details', async ({ page }) => {
  await page.context().addCookies(await savedAdministratorCookies());
  for (const shell of ['/Access', '/DynamicSecrets']) {
    await page.goto(shell);
    await openPalette(page);
    expect(await searchSecrets(page, 'E2e_Secrets_Cr')).toEqual({
      status: 200,
      names: [created.name],
    });
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(details);
    await expect(page.getByRole('heading', { name: created.name, exact: true })).toBeVisible();
  }
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
  await expect(page.getByRole('heading', { name: typed, exact: true })).toBeVisible();

  const list = new SecretsList(page);
  await list.open();
  await list.filter.focus();
  await page.keyboard.type(typed);
  await page.keyboard.press('Tab');
  await expect(list.openOf(list.secret(typed))).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(details);
  await expect(page.getByRole('heading', { name: typed, exact: true })).toBeVisible();
});

const now = Date.now();
const day = 24 * 3_600_000;
const highlighted = {
  secrets: [
    {
      secretId: '00000000-0000-4000-8000-0000000000f1',
      name: 'E2E_PRODUCTION_SOON',
      project: 'GLOBAL',
      environment: 'Production',
      sensitivity: 3,
      sensitivityLabel: 'Secret',
      createdAtUtc: new Date(now - day).toISOString(),
      expiresAtUtc: new Date(now + 2 * day).toISOString(),
    },
  ],
};
const palettes = {
  dark: {
    danger: 'rgb(255, 69, 58)',
    warn: 'rgb(255, 214, 10)',
    warnSoft: 'rgba(255, 214, 10, 0.12)',
  },
  light: {
    danger: 'rgb(215, 0, 21)',
    warn: 'rgb(138, 106, 0)',
    warnSoft: 'rgba(180, 140, 0, 0.15)',
  },
} as const;

for (const theme of ['dark', 'light'] as const) {
  test(`visual parity: the list marks production and expiry, and New secret shows a refusal, in ${theme}`, async ({
    page,
  }) => {
    const colours = palettes[theme];
    await page.route('**/api/ui/secrets', (route) =>
      route.request().method() === 'GET'
        ? route.fulfill({ json: highlighted })
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
    await page.context().addCookies([{ name: 'EclipsVault.Theme', value: theme, url: page.url() }]);
    await page.reload();
    // eslint-disable-next-line playwright/no-raw-locators -- The theme is an attribute of the document element.
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
    const row = list.secret('E2E_PRODUCTION_SOON');
    await expect(list.environmentBadgeOf(row, 'Production')).toHaveCSS('color', colours.danger);
    const soon = list.expiresOf(row).getByText('soon', { exact: true });
    await expect(soon).toHaveCSS('color', colours.warn);
    await expect(soon).toHaveCSS('background-color', colours.warnSoft);

    await list.newSecret.click();
    const form = new NewSecretForm(page);
    await form.create({
      name: 'E2E_REFUSED',
      project: 'GLOBAL',
      sensitivity: 'Internal',
      ttlDays: '0',
    });
    await expect(form.refusal).toHaveText('A secret with that name already exists.');
    await expect(form.refusal.getByText('A secret with that name already exists.')).toHaveCSS(
      'color',
      colours.danger,
    );
  });
}
