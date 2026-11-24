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
    const group = page.querySelector('details')!;
    return { fixture, page, group, trigger: group.querySelector('summary')!, link };
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

  it('opens the collapsed group beside the rail and marks the current page', async () => {
    const { fixture, group, trigger, link } = await renderAt('/Admin/Users', true);

    const secrets = link('Secrets');
    expect(secrets.getAttribute('aria-label')).toBe('Secrets');
    expect(secrets.getAttribute('title')).toBe('Secrets');
    expect(trigger.getAttribute('aria-label')).toBe('Administration, current section');
    expect(trigger.getAttribute('title')).toBe('Administration');
    expect(group.open).toBe(false);

    trigger.click();
    await fixture.whenStable();

    expect(group.open).toBe(true);
    expect(link('Users').getAttribute('aria-current')).toBe('page');
    expect(link('Networks').getAttribute('aria-current')).toBeNull();
  });

  it('moves through a rail flyout with arrows and returns focus on Escape', async () => {
    const { fixture, group, trigger, link } = await renderAt('/Secrets', true);
    const press = (target: HTMLElement, key: string) =>
      target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));

    trigger.focus();
    press(trigger, 'ArrowDown');
    await fixture.whenStable();

    expect(group.open).toBe(true);
    expect(document.activeElement).toBe(link('Users'));
    press(link('Users'), 'ArrowDown');
    expect(document.activeElement).toBe(link('Service accounts'));
    press(link('Service accounts'), 'ArrowUp');
    expect(document.activeElement).toBe(link('Users'));

    press(link('Users'), 'Escape');
    await fixture.whenStable();

    expect(group.open).toBe(false);
    expect(document.activeElement).toBe(trigger);
  });

  it('returns focus to the collapsed group when navigation closes its flyout', async () => {
    const { fixture, group, trigger, link } = await renderAt('/Secrets', true);
    trigger.click();
    await fixture.whenStable();
    link('Users').focus();

    await TestBed.inject(Router).navigateByUrl('/Admin/Users');
    await fixture.whenStable();

    expect(group.open).toBe(false);
    expect(document.activeElement).toBe(trigger);
    expect(trigger.getAttribute('aria-label')).toBe('Administration, current section');
  });

  it('closes an open rail group on an outside click and refocuses its trigger', async () => {
    const { fixture, group, trigger, link } = await renderAt('/Admin/Users', true);
    trigger.click();
    await fixture.whenStable();
    link('Users').focus();

    document.body.click();
    await fixture.whenStable();

    expect(group.open).toBe(false);
    expect(document.activeElement).toBe(trigger);
  });

  it('closes the active group disclosure while collapsed and restores it when expanded', async () => {
    const { fixture, group } = await renderAt('/Admin/Users', true);

    expect(group.open).toBe(false);
    // The rail hides the current link, so the group's own name carries the current section.
    expect(group.querySelector('summary')!.getAttribute('aria-label')).toBe(
      'Administration, current section',
    );

    fixture.componentRef.setInput('collapsed', false);
    await fixture.whenStable();

    expect(group.open).toBe(true);
  });
});
