import { NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender,
  Component,
  computed,
  DOCUMENT,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  linkedSignal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, NavigationSkipped, Router, RouterLink } from '@angular/router';
import { filter } from 'rxjs';
import { NavIcon } from './nav-icon';
import { sidebarFor, SidebarGroup, SidebarLink } from './sidebar';

/** Subpages and sibling tabs belong to their list's sidebar item, so it stays active on them. */
const subpages: Readonly<Record<string, RegExp>> = {
  '/secrets': /^\/secrets\/(create|sharedwithme|sharedbyme|details\/[^/]+)$/,
  '/admin/users': /^\/admin\/(createuser|edituser\/[^/]+)$/,
  '/serviceaccounts': /^\/serviceaccounts\/(create|details\/[^/]+)$/,
};

@Component({
  selector: 'app-sidebar-nav',
  imports: [NgTemplateOutlet, RouterLink, NavIcon],
  templateUrl: './sidebar-nav.html',
  host: { '(document:click)': 'closeFlyoutOutside($event)' },
})
export class SidebarNav {
  readonly areas = input.required<readonly string[]>();
  readonly collapsed = input(false);
  protected readonly sections = computed(() => sidebarFor(this.areas()));
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly navigation = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd || event instanceof NavigationSkipped),
    ),
  );
  private readonly url = computed(() => {
    const navigation = this.navigation();
    return navigation instanceof NavigationEnd ? navigation.urlAfterRedirects : this.router.url;
  });
  // Preserves manual open/close choices while collapsing and expanding the rail on the same route.
  private readonly preferredOpen = linkedSignal({
    source: this.url,
    computation: (): Readonly<Record<string, boolean>> => ({}),
  });
  /** The rail group whose pages fly out beside it; navigating or changing the rail closes it. */
  private readonly flyout = linkedSignal({
    source: () => ({ navigation: this.navigation(), collapsed: this.collapsed() }),
    computation: (): string | null => null,
  });

  constructor() {
    let shown: string | null = null;
    effect(() => {
      const open = this.flyout();
      const closed = shown;
      shown = open;
      if (closed && closed !== open) {
        afterNextRender(() => this.returnFocus(closed), { injector: this.injector });
      }
    });
  }

  private path(): string {
    return this.url().split(/[?#]/)[0].replace(/\/$/, '').toLowerCase();
  }

  /** The link is the current page. */
  protected active(link: SidebarLink): boolean {
    // The Overview's link is the root, which reads as an empty path once its slash is trimmed.
    const target = link.url.replace(/\/$/, '').toLowerCase();
    return this.path() === target || this.path() === `${target}/index`;
  }

  /** The current page is the link's page or one of its create and detail pages. */
  protected holds(link: SidebarLink): boolean {
    return this.active(link) || !!subpages[link.url.toLowerCase()]?.test(this.path());
  }

  protected groupActive(group: SidebarGroup): boolean {
    return group.children.some((link) => this.holds(link));
  }

  /** On the rail a group is open only while its flyout shows, so its state matches what is visible. */
  protected groupExpanded(group: SidebarGroup): boolean {
    if (this.collapsed()) return this.flyout() === group.label;
    return this.preferredOpen()[group.label] ?? this.groupActive(group);
  }

  /** The rail hides the current link, so the group's own name says it holds the current page. */
  protected railLabel(group: SidebarGroup): string | null {
    if (!this.collapsed()) return null;
    return this.groupActive(group) ? `${group.label}, current section` : group.label;
  }

  protected groupSummaryClicked(event: MouseEvent, group: SidebarGroup): void {
    event.preventDefault();
    if (this.collapsed()) {
      this.flyout.update((open) => (open === group.label ? null : group.label));
      return;
    }
    const next = !this.groupExpanded(group);
    this.preferredOpen.update((state) => ({ ...state, [group.label]: next }));
  }

  /** Arrow keys, Home and End move through a flyout; Escape closes it onto its trigger. */
  protected groupKeydown(
    event: KeyboardEvent,
    group: SidebarGroup,
    details: HTMLDetailsElement,
  ): void {
    if (!this.collapsed()) return;
    const links = [...details.querySelectorAll<HTMLElement>('.nav-group-items > a')];
    const at = links.findIndex((link) => link === this.document.activeElement);
    let next: number;
    switch (event.key) {
      case 'Escape':
        if (this.flyout() !== group.label) return;
        this.flyout.set(null);
        details.querySelector<HTMLElement>('summary')?.focus();
        event.preventDefault();
        event.stopPropagation();
        return;
      case 'ArrowDown':
        next = at < 0 ? 0 : (at + 1) % links.length;
        break;
      case 'ArrowUp':
        next = at < 0 ? links.length - 1 : (at - 1 + links.length) % links.length;
        break;
      case 'Home':
      case 'End':
        if (at < 0) return;
        next = event.key === 'Home' ? 0 : links.length - 1;
        break;
      default:
        return;
    }
    event.preventDefault();
    const link = links[next];
    if (this.flyout() === group.label) {
      link?.focus();
    } else {
      this.flyout.set(group.label);
      afterNextRender(
        () => {
          if (details.open) link?.focus();
        },
        { injector: this.injector },
      );
    }
  }

  /** Tabbing out of a flyout closes it. */
  protected groupFocusOut(event: FocusEvent, group: SidebarGroup, details: HTMLElement): void {
    const to = event.relatedTarget;
    if (this.flyout() === group.label && to instanceof Node && !details.contains(to)) {
      this.flyout.set(null);
    }
  }

  protected closeFlyoutOutside(event: MouseEvent): void {
    const open = this.flyout();
    if (!open || (event.target instanceof Node && this.group(open)?.contains(event.target))) return;
    this.flyout.set(null);
  }

  private group(label: string): HTMLDetailsElement | null {
    return this.host.nativeElement.querySelector<HTMLDetailsElement>(
      `details.nav-group[aria-label="${label}"]`,
    );
  }

  /** A closed flyout hides the link that held focus; the trigger takes it back. */
  private returnFocus(label: string): void {
    const details = this.group(label);
    if (!details || details.open) return;
    const focused = this.document.activeElement;
    const trigger = details.querySelector('summary');
    if (focused === this.document.body || details.contains(focused)) trigger?.focus();
  }
}
