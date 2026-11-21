import type { BrowserHealth } from './fixtures';

/**
 * A signed-in page the Legacy layout still draws, sidebar and palette included. The error page is
 * the last one until Area 9 retires it; it answers with the status it names.
 */
export const legacyShell = '/Home/Error?code=404';

/** The heading the Legacy shell's page shows. */
export const legacyShellHeading = '404 — Not found';

/**
 * Declares the console error the browser logs for the Legacy shell's 404 document. Pass the shell a
 * test opens when it may be the Angular one, which logs nothing.
 */
export function expectLegacyShell(browserHealth: BrowserHealth, shell: string = legacyShell): void {
  if (shell !== legacyShell) return;
  browserHealth.expectConsoleError({
    text: /status of 404/,
    url: new RegExp(`${legacyShell.replace('?', '\\?')}$`),
  });
}
