import {
  afterNextRender,
  Directive,
  DOCUMENT,
  ElementRef,
  Injectable,
  inject,
  signal,
} from '@angular/core';
import { Router } from '@angular/router';
import { PAGE_LOCATION } from '../shell/command-palette';
import { loginUrl, returnUrlOf, UiApiError } from '../ui-api/ui-api-errors';

/**
 * The steps of one sign-in. The pages move between steps inside the app, each call having reissued
 * the request token the next one needs (ADR-0032), and leave with a full page load once the vault
 * has chosen where the sign-in lands.
 */
@Injectable({ providedIn: 'root' })
export class SignInFlow {
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly location = inject(PAGE_LOCATION);

  /** Why the last sign-in had to start again, for Login to say once. */
  readonly restarted = signal('');

  /** The return URL this sign-in carries to its last step, which alone judges it. */
  returnUrl(): string | null {
    return returnUrlOf(this.document.location.search);
  }

  /** The query a step's link carries on. */
  query(): Record<string, string> {
    const returnUrl = this.returnUrl();
    return returnUrl ? { ReturnUrl: returnUrl } : {};
  }

  step(path: string): void {
    void this.router.navigate([path], { queryParams: this.query() });
  }

  /** Leaves for the page the vault chose, so it loads with the new session's document. */
  leave(redirect: string): void {
    this.location.assign(redirect);
  }

  /**
   * A refusal the page shows, or, when the account changed under the sign-in, back to Login with
   * the reason: the vault has already ended the pending sign-in and issued an anonymous token.
   */
  refused(error: unknown): string | null {
    if (error instanceof UiApiError && error.code === 'sign_in_restarted') {
      this.restarted.set(error.title);
      void this.router.navigateByUrl(loginUrl(this.returnUrl()));
      return null;
    }
    if (!(error instanceof UiApiError)) return 'The vault could not complete the request.';
    return Object.values(error.errors)[0]?.[0] ?? error.title;
  }
}

/**
 * Focuses its field once the page has rendered. The `autofocus` attribute alone works only on the
 * first page a document loads, and these pages follow each other inside the app.
 */
@Directive({ selector: '[appSignInFocus]' })
export class SignInFocus {
  constructor() {
    const field = inject<ElementRef<HTMLElement>>(ElementRef);
    afterNextRender(() => field.nativeElement.focus());
  }
}
