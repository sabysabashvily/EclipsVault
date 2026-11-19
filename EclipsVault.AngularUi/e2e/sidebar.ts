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

/** Places people visit, then the You group, which My access opens. Sharing folds until it becomes tabs. */
const everyone = [
  ['Overview', '/', ''],
  ['Secrets', '/Secrets', ''],
  ['Dynamic secrets', '/DynamicSecrets', ''],
  ['Sharing', 'group', ''],
  ['Shared with me', '/Secrets/SharedWithMe', ''],
  ['Shared by me', '/Secrets/SharedByMe', ''],
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

/**
 * The account identity and destinations remain shared by both shells.
 */
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

/** Crop to navigation content; the account menu is compared structurally, and the palette is compared in palette.ts. */
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

/** Check the Angular sidebar against the Legacy UI on the same session, in each theme, then cross over. */
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

  for (const theme of ['dark', 'light']) {
    await page.context().addCookies([
      {
        name: 'EclipsVault.Theme',
        value: theme,
        url: new URL('/', page.url()).href,
        secure: true,
      },
    ]);
    const legacy = await page.goto('/Account/Denied');
    expect(legacy?.headers()['content-security-policy']).not.toContain('trusted-types');
    // Access denied is in no sidebar entry, so the Legacy document marks none active.
    await expect(page.locator('.sidenav a.active')).toHaveCount(0);
    // Compare the Legacy styles in the same selected state as My access. The real active states
    // of both documents are asserted separately; the deleted Razor page is no longer a reference.
    await page.locator('.sidenav').evaluate((nav) => {
      nav
        .querySelectorAll('.active, .has-active')
        .forEach((item) => item.classList.remove('active', 'has-active'));
      nav.querySelectorAll('details').forEach((group) => group.removeAttribute('open'));
      const access = nav.querySelector('a[href="/Access"]')!;
      access.classList.add('active');
      access.closest('details')!.setAttribute('open', '');
      access.closest('details')!.querySelector('summary')!.classList.add('has-active');
    });
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
    const referenceMenu = await accountMenu(page);
    const referenceNotices = await notices(page);
    await screenshot(page, info, `${account}-${theme}-legacy`);

    const angular = await page.goto('/aCcEsS/INDEX');
    expect(angular?.headers()['content-security-policy']).toContain(
      "require-trusted-types-for 'script'",
    );
    await expect(page.getByRole('heading', { name: 'My access', exact: true })).toBeVisible();
    // The boot script applied the theme from the cookie the Legacy UI reads.
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
    await expect(page.locator('.sidenav a.active')).toHaveAttribute('aria-current', 'page');
    // The Angular sidebar groups its pages differently from the Legacy one, by design.
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
    await expect(shell.sharing).not.toHaveAttribute('open');
    await expect(shell.sharing.getByRole('link')).toHaveCount(0);
    await shell.sharing.getByText('Sharing', { exact: true }).click();
    await expect(shell.sharing).toHaveAttribute('open', '');
    await expect(shell.sharing.getByRole('link')).toHaveText(['Shared with me', 'Shared by me']);
    await shell.sharing.getByText('Sharing', { exact: true }).click();
    expect(await accountMenu(page)).toEqual(referenceMenu);
    expect(await notices(page)).toEqual(referenceNotices);
    if (account === 'standard') {
      // Only someone who can act on the licence is told about it, and the server decides who.
      expect(referenceNotices).toEqual([]);
      expect(await page.content()).not.toMatch(/unlicensed|notice-warn/i);
      await expect(page.locator('.sidenav')).not.toContainText('Administration');
      await expect(page.locator('.sidenav a')).toHaveCount(12);
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
      expect(referenceNotices).toEqual([
        expect.stringMatching(/^Running unlicensed\. .+ View license$/),
      ]);
      await expect(page.locator('.sidenav a')).toHaveCount(19);
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

  // A Legacy document's sidebar link replaces the document. The return reuses the same session.
  const legacyDocument = await page.goto('/Account/Denied');
  expect(legacyDocument?.headers()['content-security-policy']).not.toContain('trusted-types');
  await expect(page.getByRole('heading', { name: 'Access denied', exact: true })).toBeVisible();
  await page.getByText('Access', { exact: true }).click();
  const angularDocument = page.waitForResponse((response) =>
    response.request().isNavigationRequest(),
  );
  await page.getByRole('link', { name: 'My access', exact: true }).click();
  expect((await angularDocument).headers()['content-security-policy']).toContain(
    "require-trusted-types-for 'script'",
  );
  await expect(page).toHaveURL(/\/Access$/);
  await expect(page.locator('.sidenav a.active')).toHaveText('My access');
  expect(documents.map((url) => new URL(url).pathname)).toEqual(['/Account/Denied', '/Access']);
  expect(signInRequests).toEqual([]);
  expect(
    (await page.context().cookies()).find((cookie) => cookie.name === 'EclipsVault.Session')?.value,
  ).toBe(session?.value);
}
