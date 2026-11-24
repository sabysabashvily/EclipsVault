import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { Component, DOCUMENT, ElementRef, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { Me } from '../account/me';
import { SessionSignOut } from '../ui-api/ui-api-errors';
import { NavIcon } from './nav-icon';
import { currentTheme, otherTheme, switchTheme, Theme } from './theme';

/** The Legacy UI's account menu: who is signed in, their profile, the theme and sign-out. */
@Component({
  selector: 'app-account-menu',
  imports: [RouterLink, NavIcon],
  templateUrl: './account-menu.html',
  host: {
    '(document:click)': 'closeUnlessInside($event)',
    '(document:keydown.escape)': 'closeAndRefocus()',
  },
})
export class AccountMenu {
  readonly me = input.required<Me>();
  readonly rail = input(false);
  private readonly document = inject(DOCUMENT);
  private readonly sessionSignOut = inject(SessionSignOut);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  protected readonly theme = signal<Theme>(currentTheme(this.document));
  protected readonly signOutFailed = signal(false);

  constructor() {
    inject(Router)
      .events.pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.close());
  }

  protected nextTheme(): Theme {
    return otherTheme(this.theme());
  }

  protected switchTheme(): void {
    this.theme.set(switchTheme(this.document));
  }

  protected signOut(): void {
    this.signOutFailed.set(false);
    this.sessionSignOut.end().subscribe({ error: () => this.signOutFailed.set(true) });
  }

  protected close(): void {
    this.menu()?.removeAttribute('open');
  }

  // As the Legacy UI's menus do: a click elsewhere closes it, and Escape returns focus to its summary.
  protected closeUnlessInside(event: Event): void {
    if (!this.host.nativeElement.contains(event.target as Node)) this.close();
  }

  /** Profile calls this too: on the page already open nothing navigates, and its link is hidden. */
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
