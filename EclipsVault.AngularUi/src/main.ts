import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { SignInApp, signInConfig, signInPaths } from './app/sign-in/sign-in-app';

// The sign-in pages are their own app: no shell, and no account to load before anyone has signed in.
const signingIn = signInPaths.includes(location.pathname.toLowerCase().replace(/\/$/, ''));
(signingIn
  ? bootstrapApplication(SignInApp, signInConfig)
  : bootstrapApplication(App, appConfig)
).catch((err) => console.error(err));
