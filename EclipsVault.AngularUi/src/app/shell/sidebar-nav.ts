import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, input } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, map } from 'rxjs';
import { NavIcon } from './nav-icon';
import { sidebarFor, SidebarGroup, SidebarLink } from './sidebar';

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

  protected active(link: SidebarLink): boolean {
    const path = this.url().split(/[?#]/)[0].replace(/\/$/, '').toLowerCase();
    const target = link.url.toLowerCase();
    return path === target || path === `${target}/index`;
  }

  protected groupActive(group: SidebarGroup): boolean {
    return group.children.some((link) => this.active(link));
  }
}
