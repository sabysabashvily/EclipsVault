import type { Page, TestInfo } from '@playwright/test';
import { expect } from './fixtures';
import { verifyMyAccess } from './access';

/** Observable navigation, including the exact SVG geometry and styles a person sees. */
async function navigation(page: Page) {
  return page.locator('.sidenav').evaluate((nav) => ({
    sections: [...nav.querySelectorAll('.nav-section')].map((section) =>
      section.textContent?.trim(),
    ),
    items: [...nav.querySelectorAll('a, summary')].map((item) => {
      const style = getComputedStyle(item);
      return {
        label: item.textContent?.trim(),
        url: item.getAttribute('href'),
        active: item.classList.contains('active'),
        groupActive: item.classList.contains('has-active'),
        icon: [...item.querySelector('svg')!.children].map((shape) => ({
          tag: shape.tagName,
          attributes: [...shape.attributes].map((attribute) => [attribute.name, attribute.value]),
        })),
        style: [
          style.color,
          style.backgroundColor,
          style.fontFamily,
          style.fontSize,
          style.fontWeight,
          style.padding,
          style.gap,
          style.borderRadius,
        ],
      };
    }),
    groups: [...nav.querySelectorAll('details')].map((group) => group.open),
  }));
}

/**
 * The account card and its open menu as a person sees them: who, which avatar, each entry and its
 * styles. Leaves the menu closed again.
 */
async function accountMenu(page: Page) {
  const footer = page.locator('.sidebar-footer');
  await footer.locator('summary.user-card').click();
  const menu = await footer.evaluate((root) => {
    const look = (element: Element) => {
      const style = getComputedStyle(element);
      return [
        style.color,
        style.fontFamily,
        style.fontSize,
        style.fontWeight,
        style.padding,
        style.gap,
      ];
    };
    return {
      title: root.querySelector('summary')?.getAttribute('title'),
      avatar: root.querySelector('img.avatar-img')?.getAttribute('src'),
      who: [...root.querySelectorAll('.user-name, .user-clearance')].map((part) => [
        part.textContent?.trim(),
        look(part),
      ]),
      items: [...root.querySelectorAll('[role="menuitem"]')].map((item) => ({
        label: item.textContent?.trim(),
        href: item.getAttribute('href'),
        style: look(item),
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

/** Compare the two real UIs on the same session, in each theme, and then use their links to cross over. */
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
    const legacy = await page.goto('/Secrets');
    expect(legacy?.headers()['content-security-policy']).not.toContain('trusted-types');
    await expect(page.locator('.sidenav a.active')).toHaveText('Secrets');
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
    const reference = await navigation(page);
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
    expect(await navigation(page)).toEqual(reference);
    expect(await accountMenu(page)).toEqual(referenceMenu);
    expect(await notices(page)).toEqual(referenceNotices);
    if (account === 'standard') {
      // Only someone who can act on the licence is told about it, and the server decides who.
      expect(referenceNotices).toEqual([]);
      expect(await page.content()).not.toMatch(/unlicensed|notice-warn/i);
      await expect(page.locator('.sidenav')).not.toContainText('Administration');
      await expect(page.locator('.sidenav a')).toHaveCount(13);
      await expect(
        page.locator(
          'a[href^="/Admin"], a[href="/ServiceAccounts"], a[href="/Audit"], a[href="/Notifications"], a[href="/Encryption"], a[href="/License"]',
        ),
      ).toHaveCount(0);
    } else {
      expect(referenceNotices).toEqual([
        expect.stringMatching(/^Running unlicensed\. .+ View license$/),
      ]);
      await expect(page.locator('.sidenav .nav-section')).toHaveText([
        'Vault',
        'Account',
        'Administration',
      ]);
      await expect(page.locator('.sidenav a')).toHaveCount(20);
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

  // Legacy links replace the document. The return link reuses the same authenticated session.
  const legacyDocument = page.waitForResponse((response) =>
    response.request().isNavigationRequest(),
  );
  await page.getByRole('link', { name: 'Secrets', exact: true }).click();
  expect((await legacyDocument).headers()['content-security-policy']).not.toContain(
    'trusted-types',
  );
  await expect(page).toHaveURL(/\/Secrets$/);
  await expect(page.getByRole('heading', { name: 'Secrets', exact: true })).toBeVisible();
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
  expect(documents.map((url) => new URL(url).pathname)).toEqual(['/Secrets', '/Access']);
  expect(signInRequests).toEqual([]);
  expect(
    (await page.context().cookies()).find((cookie) => cookie.name === 'EclipsVault.Session')?.value,
  ).toBe(session?.value);
}
