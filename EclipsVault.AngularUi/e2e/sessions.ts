import type { Page } from '@playwright/test';
import type { components } from '../src/app/ui-api/schema.generated';

export type Session = components['schemas']['SessionResponse'];

export class SignedInDevices {
  readonly heading;
  readonly rows;
  readonly signOutOthers;
  readonly confirmation;
  readonly cancel;
  readonly continue;
  readonly status;
  readonly error;

  constructor(readonly page: Page) {
    this.heading = page.getByRole('heading', { name: 'Signed-in devices', exact: true });
    this.rows = page.getByRole('main').getByRole('listitem');
    this.signOutOthers = page.getByRole('button', {
      name: 'Sign out all other sessions',
      exact: true,
    });
    this.confirmation = page.getByRole('alertdialog');
    this.cancel = this.confirmation.getByRole('button', { name: 'Cancel', exact: true });
    this.continue = this.confirmation.getByRole('button', { name: 'Continue', exact: true });
    this.status = page.getByRole('main').getByRole('status');
    this.error = page.getByRole('main').getByRole('alert');
  }

  device(label: string) {
    return this.rows.filter({ hasText: label });
  }

  async liveSessions(): Promise<Session[]> {
    const response = await this.page.request.get('/api/ui/sessions');
    if (!response.ok()) throw new Error(`Listing sessions answered ${response.status()}`);
    const body: { sessions: Session[] } = await response.json();
    return body.sessions;
  }

  async signOutSession(sessionId: string): Promise<void> {
    const sessions = await this.liveSessions();
    const row = sessions.findIndex((session) => session.sessionId === sessionId);
    if (row < 0) throw new Error('The device to sign out is no longer listed');
    await this.rows.nth(row).getByRole('button', { name: 'Sign out', exact: true }).click();
  }

  async presentation() {
    return this.rows.evaluateAll((rows) =>
      rows.map((row) => {
        // Icons and the exact-time tooltip have no accessible text; inspect them only for visual parity.
        const icon = row.querySelector('.feed-icon')!;
        const style = getComputedStyle(icon);
        return {
          device: row.querySelector('.feed-title')?.textContent?.trim(),
          badges: [...row.querySelectorAll('.badge')].map((badge) => badge.textContent?.trim()),
          meta: row.querySelector('.feed-meta')?.textContent?.replace(/\s+/g, ' ').trim(),
          action: row.querySelector('.feed-action')?.textContent?.trim(),
          tooltip: row.querySelector('[title]')?.getAttribute('title'),
          shapes: [...icon.querySelector('svg')!.children].map(
            (shape) =>
              `${shape.tagName}:${[...shape.attributes].map((attribute) => `${attribute.name}=${attribute.value}`).join(',')}`,
          ),
          accent: [style.color, style.backgroundColor],
        };
      }),
    );
  }
}
