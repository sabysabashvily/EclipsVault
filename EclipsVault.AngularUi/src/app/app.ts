import { NgTemplateOutlet } from '@angular/common';
import {
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { CurrentAccount } from './account/current-account';
import { AccountMenu } from './shell/account-menu';
import { CommandPalette, commandChord } from './shell/command-palette';
import { shortcutsFor } from './shell/sidebar';
import { SidebarNav } from './shell/sidebar-nav';
import { UiApiError } from './ui-api/ui-api-errors';

@Component({
  imports: [RouterOutlet, SidebarNav, AccountMenu, CommandPalette, NgTemplateOutlet],
  selector: 'app-root',
  template: `
    <div class="shell">
      @if (compact()) {
        <dialog
          #navigationDialog
          id="mobile-navigation"
          class="mobile-navigation"
          aria-label="Navigation"
          tabindex="-1"
          (click)="navigationClicked($event)"
          (keydown)="navigationKeydown($event)"
          (close)="navigationOpen.set(false)"
        >
          <div #sidebar class="sidebar">
            <button
              type="button"
              class="button navigation-close"
              aria-label="Close navigation"
              autofocus
              (click)="closeNavigation()"
            >
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="1.8"
                stroke-linecap="round"
                aria-hidden="true"
              >
                <path d="m6 6 12 12M18 6 6 18" />
              </svg>
            </button>
            <ng-container [ngTemplateOutlet]="sidebarContent" />
          </div>
        </dialog>
      } @else {
        <aside #sidebar class="sidebar">
          <ng-container [ngTemplateOutlet]="sidebarContent" />
        </aside>
      }
      <main class="content">
        <!-- Below 1200px the sidebar is a drawer, so the bar carries its button and the logo. The
             search pill is the palette's visible way in at every width. -->
        <header class="topbar">
          @if (compact()) {
            <button
              type="button"
              class="button navigation-toggle"
              aria-label="Open navigation"
              aria-haspopup="dialog"
              aria-controls="mobile-navigation"
              [attr.aria-expanded]="navigationOpen()"
              (click)="openNavigation()"
            >
              <svg
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="1.8"
                stroke-linecap="round"
                aria-hidden="true"
              >
                <path d="M4 6h16M4 12h16M4 18h16" />
              </svg>
            </button>
            <a class="brand topbar-brand" href="/">
              <span class="brand-mark" aria-hidden="true"></span>
              <span class="brand-name">EclipsVault</span>
            </a>
          }
          @if (me.hasValue()) {
            <button
              type="button"
              class="search-pill"
              aria-label="Search secrets or jump to a page"
              (click)="openSearch()"
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
              <span class="search-pill-label">Search or jump to…</span>
              <kbd>{{ chord }}</kbd>
            </button>
          }
        </header>
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
    <ng-template #sidebarContent>
      <a class="brand workspace-card" href="/">
        <span class="brand-mark" aria-hidden="true"></span>
        <span class="brand-name">EclipsVault</span>
      </a>
      @if (me.hasValue()) {
        <app-sidebar-nav [areas]="me.value().areas" />
        <div class="sidebar-footer">
          <app-account-menu [me]="me.value()" />
        </div>
      } @else if (me.error(); as error) {
        <p class="sidebar-error" role="alert">
          Could not load your account ({{ problemCode(error) }}).
        </p>
      }
    </ng-template>
    @if (me.hasValue()) {
      @if (sidebar(); as region) {
        <app-command-palette [sidebar]="region.nativeElement" [shortcuts]="shortcuts()" />
      }
    }
  `,
})
export class App {
  protected readonly me = inject(CurrentAccount).me;
  protected readonly palette = viewChild(CommandPalette);
  private readonly document = inject(DOCUMENT);
  private readonly navigator = this.document.defaultView?.navigator;
  private readonly viewport = this.document.defaultView?.matchMedia?.('(max-width: 1199px)');
  protected readonly compact = signal(this.viewport?.matches ?? false);
  protected readonly navigationOpen = signal(false);
  protected readonly shortcuts = computed(() =>
    this.me.hasValue() ? shortcutsFor(this.me.value().areas) : [],
  );
  protected readonly sidebar = viewChild<ElementRef<HTMLElement>>('sidebar');
  protected readonly navigationDialog =
    viewChild<ElementRef<HTMLDialogElement>>('navigationDialog');
  protected readonly chord = commandChord(
    this.navigator?.platform || this.navigator?.userAgent || '',
  );

  constructor() {
    const resized = (event: MediaQueryListEvent) => {
      this.closeNavigation();
      this.compact.set(event.matches);
    };
    this.viewport?.addEventListener('change', resized);
    inject(DestroyRef).onDestroy(() => this.viewport?.removeEventListener('change', resized));
  }

  protected openNavigation(): void {
    const dialog = this.navigationDialog()?.nativeElement;
    if (!dialog) return;
    dialog.showModal();
    this.navigationOpen.set(true);
  }

  protected closeNavigation(): void {
    this.navigationDialog()?.nativeElement.close();
    this.navigationOpen.set(false);
  }

  protected openSearch(): void {
    this.closeNavigation();
    this.palette()?.open();
  }

  protected navigationClicked(event: MouseEvent): void {
    if (
      event.target === event.currentTarget ||
      (event.target instanceof Element && event.target.closest('a[href]'))
    ) {
      this.closeNavigation();
    }
  }

  protected navigationKeydown(event: KeyboardEvent): void {
    if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      event.stopPropagation();
      this.openSearch();
    } else if (event.key === 'Tab') {
      this.keepFocusInNavigation(event);
    }
  }

  /** A native modal dialog can tab into browser chrome; wrap focus at its first and last control. */
  private keepFocusInNavigation(event: KeyboardEvent): void {
    const dialog = this.navigationDialog()?.nativeElement;
    if (!dialog) return;
    // A closed group keeps layout boxes for its hidden children; checkVisibility skips them, as Tab does.
    const controls = [
      ...dialog.querySelectorAll<HTMLElement>('a[href], button:not([disabled]), summary'),
    ].filter((control) => control.checkVisibility());
    const first = controls[0];
    const last = controls.at(-1);
    if (!first || !last) return;
    const active = this.document.activeElement;
    if (event.shiftKey && active === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }

  protected problemCode(error: Error): string {
    return error instanceof UiApiError ? error.code : 'unknown';
  }
}
