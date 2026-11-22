import type { Page, TestInfo } from '@playwright/test';
import { expect } from './fixtures';
import { verifyMyAccess } from './access';
import { AppShell } from './app-shell';

/**
 * Inventory every authorized destination, including folded links. Expansion is checked separately.
 */
async function navigation(page: Page) {
  // Summaries have no distinct role; this inventory includes their hidden children in document order.
  return new AppShell(page).navigation.evaluate((nav) => ({
    sections: [...nav.querySelectorAll('.nav-section')].map((section) =>
      section.textContent?.trim(),
    ),
    items: [...nav.querySelectorAll('a, summary')].map((item) => [
      item.textContent?.trim(),
      item.getAttribute('href') ?? 'group',
      item.getAttribute('aria-current') === 'page'
        ? 'current'
        : item.tagName === 'SUMMARY' && item.parentElement?.hasAttribute('open')
          ? 'open'
          : '',
    ]),
  }));
}

/** Places people visit, then the You group, which My access opens. */
const everyone = [
  ['Overview', '/', ''],
  ['Secrets', '/Secrets', ''],
  ['Dynamic secrets', '/DynamicSecrets', ''],
  ['Access requests', '/AccessRequests', ''],
  ['You', 'group', 'open'],
  ['My access', '/Access', 'current'],
  ['Security checkup', '/Security', ''],
  ['Sign-in history', '/SignIns', ''],
  ['Signed-in devices', '/Sessions', ''],
  ['My activity', '/Activity', ''],
  ['Your data', '/Data', ''],
];

const administration = [
  ['Administration', 'group', ''],
  ['Users', '/Admin/Users', ''],
  ['Service accounts', '/ServiceAccounts', ''],
  ['Networks', '/Admin/Networks', ''],
  ['Encryption keys', '/Encryption', ''],
  ['Audit log', '/Audit', ''],
  ['Notifications', '/Notifications', ''],
  ['License', '/License', ''],
];

const expectedNavigation = {
  standard: { sections: ['Vault'], items: everyone },
  administrator: { sections: ['Vault'], items: [...everyone, ...administration] },
};

/** The account identity each account's menu shows: display name, then clearance. */
const identity = {
  standard: ['Standard Sidebar', 'Standard'],
  administrator: ['Vault Administrator', 'Top Secret'],
};

/** The account menu's identity and entries, opened and closed again. */
async function accountMenu(page: Page) {
  const footer = page.locator('.sidebar-footer');
  await footer.locator('summary.user-card').click();
  const menu = await footer.evaluate((root) => {
    return {
      title: root.querySelector('summary')?.getAttribute('title'),
      avatar: root.querySelector('img.avatar-img')?.getAttribute('src'),
      who: [...root.querySelectorAll('.user-name, .user-clearance')].map((part) =>
        part.textContent?.trim(),
      ),
      items: [...root.querySelectorAll('[role="menuitem"]')].map((item) => ({
        label: item.textContent?.trim(),
        href: item.getAttribute('href'),
      })),
    };
  });
  await footer.locator('summary.user-card').click();
  return menu;
}

/** The licensing notices above the page, as text. */
async function notices(page: Page) {
  return (await page.locator('.content > .notice').allInnerTexts()).map((text) =>
    text.replace(/\s+/g, ' ').trim(),
  );
}

/** Crop to navigation content; the account menu is checked structurally, and the palette in palette.ts. */
async function screenshot(page: Page, info: TestInfo, name: string) {
  await page.evaluate(() => document.fonts.ready);
  const clip = await page.locator('.sidenav').evaluate((nav) => {
    const bounds = nav.getBoundingClientRect();
    const last = nav.lastElementChild!.getBoundingClientRect();
    return { x: bounds.x, y: bounds.y, width: bounds.width, height: last.bottom - bounds.y };
  });
  const path = info.outputPath(`${name}.png`);
  await page.screenshot({ path, clip });
  await info.attach(name, { path, contentType: 'image/png' });
}

/** Check the sidebar, account menu and notices on the same session, in each theme, then navigate in-app. */
export async function verifySidebar(
  page: Page,
  info: TestInfo,
  account: 'administrator' | 'standard',
) {
  await page.setViewportSize({ width: 1440, height: 1200 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  const session = (await page.context().cookies()).find(
    (cookie) => cookie.name === 'EclipsVault.Session',
  );
  expect(session).toBeDefined();

  for (const theme of ['dark', 'light'] as const) {
    await page.context().addCookies([
      {
        name: 'EclipsVault.Theme',
        value: theme,
        url: new URL('/', page.url()).href,
        secure: true,
      },
    ]);
    const angular = await page.goto('/aCcEsS/INDEX');
    expect(angular?.headers()['content-security-policy']).toContain(
      "require-trusted-types-for 'script'",
    );
    await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
    // The boot script applied the theme from the cookie.
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
    await expect(page.locator('.sidenav a.active')).toHaveAttribute('aria-current', 'page');
    expect(await navigation(page)).toEqual(expectedNavigation[account]);
    const shell = new AppShell(page);
    await expect(shell.youGroup).toHaveAttribute('open', '');
    await expect(shell.youGroup.getByRole('link')).toHaveText([
      'My access',
      'Security checkup',
      'Sign-in history',
      'Signed-in devices',
      'My activity',
      'Your data',
    ]);
    await shell.you.click();
    await expect(shell.youGroup).not.toHaveAttribute('open');
    await expect(shell.youGroup.getByRole('link')).toHaveCount(0);
    await shell.you.click();
    await expect(shell.myAccess).toBeVisible();
    const [name, clearance] = identity[account];
    expect(await accountMenu(page)).toEqual({
      title: `Your account — ${clearance} clearance`,
      avatar: expect.any(String),
      who: [name, clearance],
      items: [
        { label: 'Profile', href: '/Profile' },
        { label: `Switch to ${theme === 'dark' ? 'light' : 'dark'}`, href: null },
        { label: 'Sign out', href: null },
      ],
    });
    const shown = await notices(page);
    if (account === 'standard') {
      // Only someone who can act on the licence is told about it, and the server decides who.
      expect(shown).toEqual([]);
      expect(await page.content()).not.toMatch(/unlicensed|notice-warn/i);
      await expect(page.locator('.sidenav')).not.toContainText('Administration');
      await expect(page.locator('.sidenav a')).toHaveCount(10);
      await expect(
        page.locator(
          'a[href^="/Admin"], a[href="/ServiceAccounts"], a[href="/Audit"], a[href="/Notifications"], a[href="/Encryption"], a[href="/License"]',
        ),
      ).toHaveCount(0);
    } else {
      await expect(shell.administrationGroup).not.toHaveAttribute('open');
      await expect(shell.administrationGroup.getByRole('link')).toHaveCount(0);
      await shell.administration.click();
      await expect(shell.administrationGroup).toHaveAttribute('open', '');
      await expect(shell.administrationGroup.getByRole('link')).toHaveText([
        'Users',
        'Service accounts',
        'Networks',
        'Encryption keys',
        'Audit log',
        'Notifications',
        'License',
      ]);
      await shell.administration.click();
      expect(shown).toEqual([expect.stringMatching(/^Running unlicensed\. .+ View license$/)]);
      await expect(page.locator('.sidenav a')).toHaveCount(17);
    }
    await expect(page.locator('style, [style]')).toHaveCount(0);
    await screenshot(page, info, `${account}-${theme}-angular`);
    await verifyMyAccess(page, info, account, theme);
  }

  const signInRequests: string[] = [];
  const documents: string[] = [];
  page.on('request', (request) => {
    if (/\/Account\/(Login|Totp|EnrollTotp)/i.test(new URL(request.url()).pathname))
      signInRequests.push(request.url());
    if (request.isNavigationRequest() && request.resourceType() === 'document')
      documents.push(request.url());
  });

  // Angular-owned links keep the document; aliases still mark My access and its parent active.
  await page.getByRole('link', { name: 'My access', exact: true }).click();
  await expect(page).toHaveURL(/\/Access$/);
  await expect(page.locator('.sidenav a.active')).toHaveText('My access');
  expect(documents).toEqual([]);

  expect(signInRequests).toEqual([]);
  expect(
    (await page.context().cookies()).find((cookie) => cookie.name === 'EclipsVault.Session')?.value,
  ).toBe(session?.value);
}
