import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, input } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, map } from 'rxjs';
import { NavIcon } from './nav-icon';
import { sidebarFor, SidebarGroup, SidebarLink } from './sidebar';

/** Create and detail pages belong to their list's sidebar item, so its group stays open on them. */
const subpages: Readonly<Record<string, RegExp>> = {
  '/admin/users': /^\/admin\/(createuser|edituser\/[^/]+)$/,
  '/serviceaccounts': /^\/serviceaccounts\/(create|details\/[^/]+)$/,
};

@Component({
  selector: 'app-sidebar-nav',
  imports: [NgTemplateOutlet, RouterLink, NavIcon],
  templateUrl: './sidebar-nav.html',
})
export class SidebarNav {
  readonly areas = input.required<readonly string[]>();
  protected readonly sections = computed(() => sidebarFor(this.areas()));
  private readonly router = inject(Router);
  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map((event) => event.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

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
}
