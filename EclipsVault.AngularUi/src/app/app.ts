import { NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender,
  Component,
  computed,
  DestroyRef,
  DOCUMENT,
  ElementRef,
  inject,
  Injector,
  signal,
  viewChild,
} from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { CurrentAccount } from './account/current-account';
import { AccountMenu } from './shell/account-menu';
import { CommandPalette, opensPalette } from './shell/command-palette';
import { PageActions } from './shell/page-actions';
import { shortcutsFor } from './shell/sidebar';
import { SidebarNav } from './shell/sidebar-nav';
import { APPLE_PLATFORM, chordLabel, chordName, chords, pressed, typing } from './shell/shortcuts';
import { UiApiError } from './ui-api/ui-api-errors';

const sidebarCollapsedKey = 'eclipsvault.sidebar.collapsed';
const sidebarPeekKey = 'eclipsvault.sidebar.peek';

@Component({
  imports: [RouterOutlet, SidebarNav, AccountMenu, CommandPalette, NgTemplateOutlet],
  selector: 'app-root',
  host: { '(document:keydown)': 'shortcut($event)' },
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
            <ng-container [ngTemplateOutlet]="sidebarContent" />
          </div>
        </dialog>
      } @else {
        <aside
          #sidebar
          class="sidebar"
          [class.is-collapsed]="railCollapsed()"
          [class.is-peek]="peeking()"
          (pointerenter)="peekIn($event)"
          (pointerleave)="leaveSidebar($event)"
        >
          <ng-container [ngTemplateOutlet]="sidebarContent" />
        </aside>
      }
      <main class="content">
        <!-- Below 1200px the sidebar is a drawer, so the bar carries its button and the logo. The
             search pill is the palette's visible way in at every width. -->
        <header class="topbar">
          @if (compact()) {
            <button
              #navigationToggle
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
              [title]="searchTitle"
              [attr.aria-keyshortcuts]="searchKeyNames"
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
          @if (compact() && pageActions.primary(); as action) {
            <div class="topbar-action">
              <ng-container [ngTemplateOutlet]="action" />
            </div>
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
      <div class="sidebar-header">
        <a
          class="brand sidebar-brand"
          href="/"
          [attr.aria-label]="railCollapsed() ? 'EclipsVault' : null"
          [attr.title]="railCollapsed() ? 'EclipsVault' : null"
        >
          <span class="brand-mark" aria-hidden="true"></span>
          <span class="brand-name">EclipsVault</span>
        </a>
        @if (compact()) {
          <button
            type="button"
            class="button navigation-close"
            aria-label="Close navigation"
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
        } @else if (!sidebarCollapsed()) {
          <ng-container [ngTemplateOutlet]="sidebarToggle" />
        }
      </div>
      @if (me.hasValue()) {
        <app-sidebar-nav [areas]="me.value().areas" [collapsed]="railCollapsed()" />
      } @else if (me.error(); as error) {
        <p class="sidebar-error" role="alert">
          Could not load your account ({{ problemCode(error) }}).
        </p>
      }
      <div class="sidebar-footer">
        @if (desktopSidebarCollapsed()) {
          <ng-container [ngTemplateOutlet]="sidebarToggle" />
        }
        @if (me.hasValue()) {
          <app-account-menu [me]="me.value()" [rail]="desktopSidebarCollapsed()" />
        }
      </div>
    </ng-template>
    <ng-template #sidebarToggle>
      <button
        type="button"
        class="sidebar-toggle"
        [class.rail-control]="sidebarCollapsed()"
        [attr.aria-label]="sidebarCollapsed() ? 'Expand sidebar' : 'Collapse sidebar'"
        [attr.title]="toggleTitle()"
        [attr.aria-keyshortcuts]="compactKeyName"
        [attr.aria-expanded]="!sidebarCollapsed()"
        (click)="toggleSidebar()"
      >
        <svg
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.8"
          stroke-linecap="round"
          stroke-linejoin="round"
          aria-hidden="true"
        >
          <rect width="18" height="18" x="3" y="3" rx="2" />
          <path d="M9 3v18" />
        </svg>
      </button>
    </ng-template>
    <p class="visually-hidden" role="status">{{ peekStatus() }}</p>
    @if (me.hasValue()) {
      @if (sidebar(); as region) {
        <app-command-palette
          [sidebar]="region.nativeElement"
          [shortcuts]="shortcuts()"
          [desktop]="!compact()"
        />
      }
    }
  `,
})
export class App {
  protected readonly me = inject(CurrentAccount).me;
  protected readonly palette = viewChild(CommandPalette);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private readonly apple = inject(APPLE_PLATFORM);
  private readonly viewport = this.document.defaultView?.matchMedia?.('(max-width: 1199px)');
  protected readonly pageActions = inject(PageActions);
  protected readonly compact = this.pageActions.compact;
  protected readonly navigationOpen = signal(false);
  protected readonly sidebarCollapsed = signal(this.readFlag(sidebarCollapsedKey));
  protected readonly desktopSidebarCollapsed = computed(
    () => !this.compact() && this.sidebarCollapsed(),
  );
  private readonly peekEnabled = signal(this.readFlag(sidebarPeekKey));
  protected readonly peeking = signal(false);
  private mouseOverSidebar = false;
  /** The rail as drawn: collapsed, unless a peek is showing the full sidebar over the page. */
  protected readonly railCollapsed = computed(
    () => this.desktopSidebarCollapsed() && !this.peeking(),
  );
  protected readonly peekStatus = signal('');
  protected readonly shortcuts = computed(() =>
    this.me.hasValue() ? shortcutsFor(this.me.value().areas) : [],
  );
  protected readonly sidebar = viewChild<ElementRef<HTMLElement>>('sidebar');
  protected readonly navigationDialog =
    viewChild<ElementRef<HTMLDialogElement>>('navigationDialog');
  protected readonly navigationToggle =
    viewChild<ElementRef<HTMLButtonElement>>('navigationToggle');
  protected readonly chord = chordLabel(chords.search, this.apple);
  protected readonly searchTitle = `Search (${this.chord} or ${chordLabel(chords.searchAlt, this.apple)})`;
  protected readonly searchKeyNames = `${chordName(chords.search, this.apple)} ${chordName(chords.searchAlt, this.apple)}`;
  private readonly compactChord = chordLabel(chords.compact, this.apple);
  private readonly peekChord = chordLabel(chords.peek, this.apple);
  protected readonly compactKeyName = chordName(chords.compact, this.apple);
  protected readonly toggleTitle = computed(
    () =>
      `${this.sidebarCollapsed() ? 'Expand' : 'Collapse'} sidebar (${this.compactChord}) · Peek on hover (${this.peekChord})`,
  );

  constructor() {
    this.compact.set(this.viewport?.matches ?? false);
    const resized = (event: MediaQueryListEvent) => {
      const hadFocus = this.document.activeElement !== this.document.body;
      this.closeNavigation();
      this.mouseOverSidebar = false;
      this.endPeek();
      this.compact.set(event.matches);
      this.keepFocusOnToggle(hadFocus);
    };
    this.viewport?.addEventListener('change', resized);
    inject(DestroyRef).onDestroy(() => this.viewport?.removeEventListener('change', resized));
  }

  /**
   * Swapping the navigation markup (crossing 1200px, or moving the toggle between the header and the
   * rail's foot) removes the focused control; focus goes to the toggle that replaces it.
   */
  private keepFocusOnToggle(hadFocus: boolean): void {
    afterNextRender(
      () => {
        if (!hadFocus || this.document.activeElement !== this.document.body) return;
        const toggle = this.compact()
          ? this.navigationToggle()?.nativeElement
          : this.sidebar()?.nativeElement.querySelector<HTMLElement>('.sidebar-toggle');
        toggle?.focus();
      },
      { injector: this.injector },
    );
  }

  /** ⌘B and ⌘. (Ctrl off Apple platforms) work the desktop sidebar, never while typing. */
  protected shortcut(event: KeyboardEvent): void {
    if (this.compact() || event.repeat || event.isComposing || typing(event.target)) return;
    if (pressed(event, chords.compact, this.apple)) {
      event.preventDefault();
      this.toggleSidebar();
    } else if (pressed(event, chords.peek, this.apple)) {
      event.preventDefault();
      this.togglePeek();
    }
  }

  protected toggleSidebar(): void {
    const hadFocus = this.document.activeElement !== this.document.body;
    const next = !this.sidebarCollapsed();
    this.peeking.set(false);
    this.sidebarCollapsed.set(next);
    this.writeFlag(sidebarCollapsedKey, next);
    this.keepFocusOnToggle(hadFocus);
  }

  private togglePeek(): void {
    const next = !this.peekEnabled();
    this.peekEnabled.set(next);
    this.writeFlag(sidebarPeekKey, next);
    if (!next) this.endPeek();
    else if (this.mouseOverSidebar && this.desktopSidebarCollapsed()) this.peeking.set(true);
    this.peekStatus.set(
      next
        ? 'Peek on hover is on: hovering the collapsed sidebar shows it in full.'
        : 'Peek on hover is off.',
    );
  }

  protected peekIn(event: PointerEvent): void {
    if (event.pointerType !== 'mouse') return;
    this.mouseOverSidebar = true;
    if (this.desktopSidebarCollapsed() && this.peekEnabled()) this.peeking.set(true);
  }

  protected leaveSidebar(event: PointerEvent): void {
    if (event.pointerType !== 'mouse') return;
    this.mouseOverSidebar = false;
    this.endPeek();
  }

  private endPeek(): void {
    if (!this.peeking()) return;
    // A folded group hides its child links when the preview ends.
    this.document.activeElement
      ?.closest('.nav-group-items')
      ?.parentElement?.querySelector('summary')
      ?.focus();
    this.peeking.set(false);
  }

  private readFlag(key: string): boolean {
    try {
      return this.document.defaultView?.localStorage?.getItem(key) === '1';
    } catch {
      return false;
    }
  }

  private writeFlag(key: string, on: boolean): void {
    try {
      this.document.defaultView?.localStorage?.setItem(key, on ? '1' : '0');
    } catch {
      // Storage may be unavailable in private browsing or restricted iframes.
    }
  }

  protected openNavigation(): void {
    const dialog = this.navigationDialog()?.nativeElement;
    if (!dialog) return;
    dialog.showModal();
    dialog.querySelector<HTMLButtonElement>('.navigation-close')?.focus();
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
    if (opensPalette(event, this.apple)) {
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
