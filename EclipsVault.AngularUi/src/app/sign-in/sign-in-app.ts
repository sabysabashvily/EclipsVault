import {
  ApplicationConfig,
  Component,
  DOCUMENT,
  inject,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import {
  provideHttpClient,
  withFetch,
  withInterceptors,
  withXsrfConfiguration,
} from '@angular/common/http';
import { provideRouter, RouterOutlet, Routes } from '@angular/router';
import { legacyPath } from '../app.routes';
import { switchTheme } from '../shell/theme';
import { signInApiErrors } from '../ui-api/ui-api-errors';

/** The sign-in pages, which the server serves at these URLs only to the visitor each one is for. */
export const signInPaths = [
  '/account/login',
  '/account/totp',
  '/account/recoverycode',
  '/account/enrolltotp',
  '/account/recover',
  '/account/activate',
];

const routes: Routes = [
  {
    matcher: legacyPath('Account/Login'),
    loadComponent: () => import('./login-page').then((m) => m.LoginPage),
    title: 'Sign in — EclipsVault',
  },
  {
    matcher: legacyPath('Account/Totp'),
    loadComponent: () => import('./totp-page').then((m) => m.TotpPage),
    title: 'Two-factor verification — EclipsVault',
  },
  {
    matcher: legacyPath('Account/RecoveryCode'),
    loadComponent: () => import('./recovery-code-page').then((m) => m.RecoveryCodePage),
    title: 'Use a recovery code — EclipsVault',
  },
  {
    matcher: legacyPath('Account/EnrollTotp'),
    loadComponent: () => import('./enroll-totp-page').then((m) => m.EnrollTotpPage),
    title: 'Set up two-factor authentication — EclipsVault',
  },
  {
    matcher: legacyPath('Account/Recover'),
    loadComponent: () => import('./recover-page').then((m) => m.RecoverPage),
    title: 'Recover access — EclipsVault',
  },
  {
    matcher: legacyPath('Account/Activate'),
    loadComponent: () => import('./activate-page').then((m) => m.ActivatePage),
    title: 'Activate your account — EclipsVault',
  },
];

/** The sign-in pages need no account, so they never call `/api/ui/me` (ADR-0032). */
export const signInConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(
      withFetch(),
      withInterceptors([signInApiErrors]),
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'RequestVerificationToken' }),
    ),
    provideRouter(routes),
  ],
};

/** Anonymous sign-in layout: solid canvas, brand bar with theme switch, centered card, trust note. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  template: `
    <main class="auth-wrap">
      <div class="auth-header">
        <a class="brand auth-brand" href="/">
          <span class="brand-mark" aria-hidden="true"></span>
          <span class="brand-name">EclipsVault</span>
        </a>
        <button
          type="button"
          class="auth-theme-toggle theme-toggle"
          title="Switch between light and dark"
          aria-label="Switch color theme"
          (click)="switchTheme()"
        >
          <svg
            class="icon-sun"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.8"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <circle cx="12" cy="12" r="4" />
            <path
              d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"
            />
          </svg>
          <svg
            class="icon-moon"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.8"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8Z" />
          </svg>
        </button>
      </div>
      <router-outlet />
      <p class="auth-footnote">
        <svg
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.8"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <rect x="4" y="10" width="16" height="10" rx="2" />
          <path d="M8 10V7a4 4 0 0 1 8 0v3" />
        </svg>
        <span>Envelope-encrypted secret management. Everything is audited.</span>
      </p>
    </main>
  `,
})
export class SignInApp {
  private readonly document = inject(DOCUMENT);

  constructor() {
    this.document.body.classList.add('auth-body');
  }

  protected switchTheme(): void {
    switchTheme(this.document);
  }
}
