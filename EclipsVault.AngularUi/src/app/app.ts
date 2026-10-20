import { Component, DOCUMENT, inject, viewChild } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { CurrentAccount } from './account/current-account';
import { AccountMenu } from './shell/account-menu';
import { CommandPalette, commandChord } from './shell/command-palette';
import { SidebarNav } from './shell/sidebar-nav';
import { UiApiError } from './ui-api/ui-api-errors';

@Component({
  imports: [RouterOutlet, SidebarNav, AccountMenu, CommandPalette],
  selector: 'app-root',
  template: `
    <div class="shell">
      <aside #sidebar class="sidebar">
        <a class="brand" href="/">
          <span class="brand-mark" aria-hidden="true"></span>
          <span class="brand-name">EclipsVault</span>
        </a>
        @if (me.hasValue()) {
          <!-- The palette's visible way in, as in the Legacy UI: a shortcut nobody can see is a rumour. -->
          <button
            type="button"
            class="command-trigger"
            aria-label="Search secrets or jump to a page"
            (click)="palette()?.open()"
          >
            <svg
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="1.8"
              stroke-linecap="round"
              aria-hidden="true"
            >
              <circle cx="11" cy="11" r="7" />
              <path d="m20 20-3.5-3.5" />
            </svg>
            <span class="command-trigger-label">Search…</span>
            <kbd>{{ chord }}</kbd>
          </button>
          <app-sidebar-nav [areas]="me.value().areas" />
          <div class="sidebar-footer">
            <app-account-menu [me]="me.value()" />
          </div>
        } @else if (me.error(); as error) {
          <p class="sidebar-error" role="alert">
            Could not load your account ({{ problemCode(error) }}).
          </p>
        }
      </aside>
      <main class="content">
        <!-- Shown only when the server sends it: it decides who may see a licence notice (ADR-0012). -->
        @if (me.hasValue() && me.value().licenseNotice; as notice) {
          <div class="notice notice-warn">
            <svg
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="1.8"
              stroke-linecap="round"
              stroke-linejoin="round"
              aria-hidden="true"
            >
              <path
                d="M12 9v4M12 17h.01M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z"
              />
            </svg>
            <span>
              <strong>{{ notice.title }}</strong> {{ notice.text }}
              <a href="/License">View license</a>
            </span>
          </div>
        }
        <router-outlet />
        <footer class="content-footer">
          EclipsVault — envelope-encrypted secret management. Everything is audited.
        </footer>
      </main>
    </div>
    @if (me.hasValue()) {
      <app-command-palette [sidebar]="sidebar" />
    }
  `,
})
export class App {
  protected readonly me = inject(CurrentAccount).me;
  protected readonly palette = viewChild(CommandPalette);
  private readonly navigator = inject(DOCUMENT).defaultView?.navigator;
  protected readonly chord = commandChord(
    this.navigator?.platform || this.navigator?.userAgent || '',
  );

  protected problemCode(error: Error): string {
    return error instanceof UiApiError ? error.code : 'unknown';
  }
}
