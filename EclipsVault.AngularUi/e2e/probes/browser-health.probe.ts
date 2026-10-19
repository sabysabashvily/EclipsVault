import { test, expect } from '../fixtures';

// Intentionally failing tests, run only by e2e:verify-guards. The driver checks that each failed
// specifically in the shared browser-health fixture, rather than accepting any nonzero exit.
test('CSP violation', async ({ page }) => {
  await page.goto('/Account/Login');
  await page.evaluate(
    () =>
      new Promise<void>((resolve) => {
        document.addEventListener('securitypolicyviolation', () => resolve(), { once: true });
        const script = document.createElement('script');
        script.textContent = 'window.inlineScriptRan = true';
        document.body.append(script);
      }),
  );
  await expect.poll(() => page.evaluate(() => 'inlineScriptRan' in window)).toBe(false);
});

test('an expected CSP violation excuses nothing else', async ({ browserHealth, page }) => {
  await page.goto('/Account/Login');
  browserHealth.expectCspViolation({
    effectiveDirective: 'script-src-elem',
    blockedURI: 'inline',
    sample: '',
    documentURI: page.url(),
    disposition: 'enforce',
  });
  // The first violation satisfies the expectation; the identical second one must still fail.
  for (let i = 0; i < 2; i++) {
    await page.evaluate(
      () =>
        new Promise<void>((resolve) => {
          document.addEventListener('securitypolicyviolation', () => resolve(), { once: true });
          const script = document.createElement('script');
          script.textContent = 'window.inlineScriptRan = true';
          document.body.append(script);
        }),
    );
  }
});

test('an expected CSP violation that never occurs', async ({ browserHealth, page }) => {
  await page.goto('/Account/Login');
  browserHealth.expectCspViolation({
    effectiveDirective: 'require-trusted-types-for',
    blockedURI: 'trusted-types-sink',
    sample: 'Element innerHTML|<p>tt-unsafe-assignment</p>',
    documentURI: page.url(),
    disposition: 'enforce',
  });
});

test('console.error after navigation in a second page', async ({ context, page }) => {
  await page.goto('/Account/Login');
  const other = await context.newPage();
  await other.goto('/Account/Login');
  await other.evaluate(() => console.error('e2e console sentinel'));
  await other.close();
});

test('uncaught browser exception', async ({ page }) => {
  await page.goto('/Account/Login');
  const error = page.waitForEvent('pageerror');
  await page.evaluate(() => {
    setTimeout(() => {
      throw new Error('e2e exception sentinel');
    }, 0);
  });
  await error;
});

test('console.error in a second browser context', async ({ browserHealth }) => {
  const other = await (await browserHealth.newContext()).newPage();
  await other.goto('/Account/Login');
  await other.evaluate(() => console.error('e2e second-context sentinel'));
});

test('an expected console error excuses nothing else', async ({ browserHealth, page }) => {
  browserHealth.expectConsoleError({ text: /status of 401/, url: /\/api\/ui\/me$/ });
  await page.goto('/Account/Login');
  // The excused error does occur (signed out, the UI API answers 401), so only the other one fails.
  await page.evaluate(() => fetch('/api/ui/me'));
  await page.evaluate(() => console.error('e2e unexcused sentinel'));
});

test('an expected console error that never occurs', async ({ browserHealth, page }) => {
  browserHealth.expectConsoleError({ text: /status of 401/, url: /\/api\/ui\/me$/ });
  await page.goto('/Account/Login');
});
