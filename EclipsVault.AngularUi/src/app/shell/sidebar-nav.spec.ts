import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { SidebarNav } from './sidebar-nav';

@Component({ template: '' })
class Blank {}

describe('SidebarNav', () => {
  async function renderAt(url: string, collapsed = false) {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: '**', component: Blank }])],
    });
    await TestBed.inject(Router).navigateByUrl(url);
    const fixture = TestBed.createComponent(SidebarNav);
    fixture.componentRef.setInput('areas', ['secrets', 'users', 'service-accounts', 'networks']);
    fixture.componentRef.setInput('collapsed', collapsed);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    const link = (label: string) =>
      [...page.querySelectorAll('a')].find((anchor) => anchor.textContent?.trim() === label)!;
    return { fixture, page, group: page.querySelector('details')!, link };
  }

  it('keeps Secrets active on its Sharing tabs, New secret and detail pages', async () => {
    for (const url of [
      '/Secrets/SharedWithMe',
      '/Secrets/SharedByMe',
      '/Secrets/Create',
      '/Secrets/Details/3f2504e0-4f89-11d3-9a0c-0305e82c3301',
    ]) {
      TestBed.resetTestingModule();
      const { link } = await renderAt(url);
      expect(link('Secrets').classList).toContain('active');
      expect(link('Secrets').getAttribute('aria-current')).toBe('true');
    }
  });

  it('keeps Administration open on a create page, marking its list as the current section', async () => {
    const { group, link } = await renderAt('/Admin/CreateUser');

    expect(group.open).toBe(true);
    expect(link('Users').classList).toContain('active');
    expect(link('Users').getAttribute('aria-current')).toBe('true');
    expect(link('Networks').getAttribute('aria-current')).toBeNull();
  });

  it('keeps Administration open on a detail page', async () => {
    const { group, link } = await renderAt(
      '/ServiceAccounts/Details/3f2504e0-4f89-11d3-9a0c-0305e82c3301',
    );

    expect(group.open).toBe(true);
    expect(link('Service accounts').getAttribute('aria-current')).toBe('true');
  });

  it('marks the list itself as the current page', async () => {
    const { link } = await renderAt('/Admin/Users');

    expect(link('Users').getAttribute('aria-current')).toBe('page');
  });

  it('adds accessible labels and tooltips to rail items when collapsed, and asks to expand when a group is clicked', async () => {
    const { fixture, group, link } = await renderAt('/Secrets', true);
    const expanded = vi.fn();
    fixture.componentInstance.expandRequested.subscribe(expanded);

    const secrets = link('Secrets');
    const summary = group.querySelector<HTMLElement>('summary')!;
    expect(secrets.getAttribute('aria-label')).toBe('Secrets');
    expect(secrets.getAttribute('title')).toBe('Secrets');
    expect(summary.getAttribute('aria-label')).toBe('Administration');
    expect(summary.getAttribute('title')).toBe('Administration');
    expect(group.open).toBe(false);

    summary.click();
    await fixture.whenStable();

    expect(group.open).toBe(true);
    expect(expanded).toHaveBeenCalledTimes(1);
  });
});
