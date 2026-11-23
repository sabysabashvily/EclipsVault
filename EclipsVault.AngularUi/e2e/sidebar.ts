import type { BrowserContext, Page, TestInfo } from '@playwright/test';
import { AppShell } from './app-shell';

/**
 * Inventory every authorized destination, including folded links. Expansion is checked separately.
 */
export async function sidebarNavigation(page: Page) {
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

export const expectedNavigation = {
  standard: { sections: ['Vault'], items: everyone },
  administrator: { sections: ['Vault'], items: [...everyone, ...administration] },
};

/** The account menu's identity and entries, opened and closed again. */
export async function readAccountMenu(page: Page) {
  const footer = page.locator('.sidebar-footer');
  const trigger = page.getByTitle(/^Your account /);
  await trigger.click();
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
  await trigger.click();
  return menu;
}

/** Crop to navigation content; the account menu is checked structurally, and the palette in palette.ts. */
export async function captureSidebar(page: Page, info: TestInfo, name: string) {
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

export async function openMyAccessBookmark(
  page: Page,
  cookies: Awaited<ReturnType<BrowserContext['cookies']>>,
  baseURL: string | undefined,
  theme: 'dark' | 'light',
) {
  if (!baseURL) throw new Error('Run with the vault URL configured');
  await page.setViewportSize({ width: 1440, height: 1200 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.context().addCookies(cookies);
  await page
    .context()
    .addCookies([{ name: 'EclipsVault.Theme', value: theme, url: baseURL, secure: true }]);
  await page.goto('/aCcEsS/INDEX');
}
