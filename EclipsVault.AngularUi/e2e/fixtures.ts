import { test as base, expect, type BrowserContext } from '@playwright/test';

declare global {
  interface Window {
    reportCspViolation: (details: CspViolation) => Promise<void>;
  }
}

/** A console error a test causes on purpose, such as the browser logging an expected 401. */
interface ExpectedConsoleError {
  text: RegExp;
  url: RegExp;
}

interface CspViolation {
  effectiveDirective: string;
  blockedURI: string;
  sample: string;
  documentURI: string;
  disposition: string;
}

export interface BrowserHealth {
  /**
   * Excuses console errors matching both `text` and the URL they are logged for. Nothing else is
   * excused, and the test fails if no such error occurs.
   */
  expectConsoleError(expected: ExpectedConsoleError): void;
  /** Expects exactly one deliberate violation, matching every field; all other violations fail. */
  expectCspViolation(expected: CspViolation): void;
  /** Another browser context (another browser, in effect), held to the same checks and closed after the test. */
  newContext(): Promise<BrowserContext>;
}

// Every spec imports this test. Install before the first document, and keep collecting through
// navigations and popups. Assertions in teardown make even an otherwise passing test fail.
export const test = base.extend<{ browserHealth: BrowserHealth }>({
  browserHealth: [
    async ({ browser, context }, use) => {
      const problems: string[] = [];
      const expected: (ExpectedConsoleError & { seen: boolean })[] = [];
      const expectedCsp: { details: CspViolation; seen: boolean }[] = [];
      const others: BrowserContext[] = [];

      async function watch(watched: BrowserContext) {
        watched.on('console', (message) => {
          if (message.type() !== 'error') return;
          const excuse = expected.find(
            (e) => e.text.test(message.text()) && e.url.test(message.location().url),
          );
          if (excuse) {
            excuse.seen = true;
          } else {
            problems.push(`console.error: ${message.text()}`);
          }
        });
        watched.on('weberror', (error) => problems.push(`pageerror: ${error.error().message}`));
        await watched.exposeBinding('reportCspViolation', (_source, details: CspViolation) => {
          const excuse = expectedCsp.find(
            (e) =>
              !e.seen &&
              e.details.effectiveDirective === details.effectiveDirective &&
              e.details.blockedURI === details.blockedURI &&
              e.details.sample === details.sample &&
              e.details.documentURI === details.documentURI &&
              e.details.disposition === details.disposition,
          );
          if (excuse) {
            excuse.seen = true;
          } else {
            problems.push(
              `CSP violation: ${details.effectiveDirective}: ${details.blockedURI} (${details.sample})`,
            );
          }
        });
        await watched.addInitScript(() => {
          document.addEventListener('securitypolicyviolation', (event) => {
            void window.reportCspViolation({
              effectiveDirective: event.effectiveDirective,
              blockedURI: event.blockedURI,
              sample: event.sample,
              documentURI: event.documentURI,
              disposition: event.disposition,
            });
          });
        });
      }

      await watch(context);
      await use({
        expectConsoleError: (error) => expected.push({ ...error, seen: false }),
        expectCspViolation: (details) => expectedCsp.push({ details, seen: false }),
        newContext: async () => {
          const other = await browser.newContext();
          others.push(other);
          await watch(other);
          return other;
        },
      });

      for (const other of others) await other.close();
      expect.soft(problems, '[browser-health] CSP violations and browser errors').toEqual([]);
      expect
        .soft(
          expected.filter((e) => !e.seen).map((e) => `${e.text} at ${e.url}`),
          '[browser-health] expected console errors that never occurred',
        )
        .toEqual([]);
      expect
        .soft(
          expectedCsp.filter((e) => !e.seen).map((e) => e.details),
          '[browser-health] expected CSP violations that never occurred',
        )
        .toEqual([]);
    },
    { auto: true },
  ],
});

export { expect };
