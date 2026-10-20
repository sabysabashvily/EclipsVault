import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import {
  provideHttpClient,
  withFetch,
  withInterceptors,
  withXsrfConfiguration,
} from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { uiApiErrors } from './ui-api/ui-api-errors';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(
      withFetch(),
      withInterceptors([uiApiErrors]),
      // ADR-0004: /api/ui/me sets the cookie; same-origin unsafe requests echo it in the header the
      // vault's antiforgery already accepts from the Legacy UI's passkey calls.
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'RequestVerificationToken' }),
    ),
    provideRouter(routes),
  ],
};
