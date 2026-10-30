import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { Component, DOCUMENT, ElementRef, inject, input, signal } from '@angular/core';
import { Me } from '../account/me';
import { currentTheme, otherTheme, switchTheme, Theme } from './theme';

/**
 * Ends the session through the existing sign-out endpoint, which revokes it on the server, then
 * leaves for sign-in. Angular's XSRF support adds the antiforgery header (ADR-0004).
 */
export function signOut(
  http: HttpClient,
  location: Pick<Location, 'assign'>,
  failed: () => void,
): void {
  http.post('/Account/Logout', null, { responseType: 'text' }).subscribe({
    next: () => location.assign('/Account/Login'),
    error: () => failed(),
  });
}

/** The Legacy UI's account menu: who is signed in, their profile, the theme and sign-out. */
@Component({
  selector: 'app-account-menu',
  imports: [RouterLink],
  templateUrl: './account-menu.html',
  host: {
    '(document:click)': 'closeUnlessInside($event)',
    '(document:keydown.escape)': 'closeAndRefocus()',
  },
})
export class AccountMenu {
  readonly me = input.required<Me>();
  private readonly document = inject(DOCUMENT);
  private readonly http = inject(HttpClient);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  protected readonly theme = signal<Theme>(currentTheme(this.document));
  protected readonly signOutFailed = signal(false);

  protected nextTheme(): Theme {
    return otherTheme(this.theme());
  }

  protected switchTheme(): void {
    this.theme.set(switchTheme(this.document));
  }

  protected signOut(): void {
    this.signOutFailed.set(false);
    signOut(this.http, this.document.location, () => this.signOutFailed.set(true));
  }

  // As the Legacy UI's menus do: a click elsewhere closes it, and Escape returns focus to its summary.
  protected closeUnlessInside(event: Event): void {
    if (!this.host.nativeElement.contains(event.target as Node))
      this.menu()?.removeAttribute('open');
  }

  protected closeAndRefocus(): void {
    const menu = this.menu();
    if (!menu?.open) return;
    const focusWasInside = menu.contains(this.document.activeElement);
    menu.removeAttribute('open');
    if (focusWasInside) menu.querySelector('summary')?.focus();
  }

  private menu(): HTMLDetailsElement | null {
    return this.host.nativeElement.querySelector('details');
  }
}
