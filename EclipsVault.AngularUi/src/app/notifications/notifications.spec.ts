import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../app.config';
import type { components } from '../ui-api/schema.generated';
import { NotificationsPage } from './notifications-page';

const empty: components['schemas']['NotificationsResponse'] = {
  delivery: 'off',
  smtpTarget: null,
  outbox: [],
};
const listed: components['schemas']['NotificationsResponse'] = {
  delivery: 'smtp',
  smtpTarget: 'mail.example.test:2525',
  outbox: [
    {
      sentAtUtc: '2026-01-02T03:04:00Z',
      to: 'reader@example.test',
      subject: 'Your account is ready',
      body: 'Welcome, Reader',
      eventType: 'UserProvisioned',
      transport: 'Smtp',
      status: 'sent',
      error: null,
    },
    {
      sentAtUtc: '2026-01-01T23:45:00Z',
      to: 'owner@example.test',
      subject: 'FINANCE_DECOY_KEY expires',
      body: 'A secret expires soon',
      eventType: 'SecretExpiring',
      transport: 'Smtp',
      status: 'failed',
      error: 'Mail server refused delivery',
    },
    {
      sentAtUtc: '2026-01-01T00:00:00Z',
      to: 'other@example.test',
      subject: 'Password changed',
      body: 'Your password changed',
      eventType: 'PasswordChanged',
      transport: 'Log',
      status: 'suppressed',
      error: null,
    },
  ],
};
let backend: HttpTestingController;
function render() {
  TestBed.configureTestingModule({
    providers: [...appConfig.providers, provideHttpClientTesting()],
  });
  backend = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(NotificationsPage);
  fixture.detectChanges();
  const page: HTMLElement = fixture.nativeElement;
  return { fixture, page };
}
async function loaded(response = listed) {
  const rendered = render();
  backend.expectOne('/api/ui/notifications').flush(response);
  await rendered.fixture.whenStable();
  return rendered;
}
afterEach(() => {
  backend.verify();
  TestBed.resetTestingModule();
});
it('announces that notifications are loading', () => {
  const { page } = render();
  expect(page.querySelector('[role="status"]')?.textContent).toBe('Loading notifications…');
  backend.expectOne('/api/ui/notifications').flush(empty);
});
it('retries a failed load and shows the empty outbox', async () => {
  const { page, fixture } = render();
  backend.expectOne('/api/ui/notifications').flush({}, { status: 503, statusText: 'Unavailable' });
  await fixture.whenStable();
  expect(page.querySelector('[role="alert"]')?.textContent).toBe(
    'Could not load notifications. Try again.',
  );
  page.querySelector('button')?.click();
  fixture.detectChanges();
  backend.expectOne('/api/ui/notifications').flush(empty);
  await fixture.whenStable();
  expect(page.querySelector('h3')?.textContent).toBe('No notifications sent yet');
  expect(page.textContent).toContain(
    'Domain events send email automatically — approving or rejecting an access request, a password change, or provisioning a user. Each one is recorded here with its delivery outcome.',
  );
});
it.each([
  {
    delivery: 'off',
    tone: 'warn',
    notice:
      '⚠ Delivery is off. Notifications are recorded as suppressed and never sent. Set Email:Enabled=true to dispatch them.',
  },
  {
    delivery: 'smtp',
    tone: 'ok',
    notice:
      '✓ Delivering via SMTP → mail.example.test:2525. Composed emails are sent to your mail server and recorded below.',
  },
  {
    delivery: 'log',
    tone: 'warn',
    notice:
      'Development log transport. Emails are composed and recorded below but not actually sent. Point Email:Sender=Smtp at an SMTP server (e.g. the bundled Mailpit) to deliver them.',
  },
] satisfies {
  delivery: components['schemas']['NotificationsResponse']['delivery'];
  tone: string;
  notice: string;
}[])('shows the exact $delivery delivery notice', async ({ delivery, tone, notice }) => {
  const { page } = await loaded({ ...listed, delivery });
  expect(
    [...page.querySelectorAll('.notice > strong, .notice > span')]
      .map((part) => part.textContent)
      .join(' ')
      .replace(/\s+/g, ' ')
      .trim(),
  ).toBe(notice);
  expect(page.querySelector('.notice')?.classList.contains(`notice-${tone}`)).toBe(true);
});
it('lists each delivery outcome with UTC time and the body and failure tooltips', async () => {
  const { page } = await loaded();
  expect([...page.querySelectorAll('th')].map((cell) => cell.textContent)).toEqual([
    'Sent (UTC)',
    'To',
    'Subject',
    'Event',
    'Transport',
    'Status',
  ]);
  expect(
    [...page.querySelectorAll('tbody tr:first-child td')].map((cell) => cell.textContent?.trim()),
  ).toEqual([
    '2026-01-02 03:04',
    'reader@example.test',
    'Your account is ready',
    'UserProvisioned',
    'Smtp',
    'Sent',
  ]);
  expect(page.querySelector('td[title="Welcome, Reader"]')?.textContent).toBe(
    'Your account is ready',
  );
  expect(page.querySelector('.tone-danger')?.textContent).toBe('Failed');
  expect(
    page.querySelector('span[title="Mail server refused delivery"]')?.textContent?.trim(),
  ).toBe('⚠');
  expect(page.textContent).toContain('Suppressed');
});
it.each([
  { query: 'READER@', subjects: ['Your account is ready'] },
  { query: 'finance_decoy', subjects: ['FINANCE_DECOY_KEY expires'] },
  { query: 'passwordchanged', subjects: ['Password changed'] },
  { query: '2026-01-01 23:45', subjects: ['FINANCE_DECOY_KEY expires'] },
  { query: 'log', subjects: ['Password changed'] },
  { query: 'FAILED', subjects: ['FINANCE_DECOY_KEY expires'] },
  { query: 'suppressed', subjects: ['Password changed'] },
  { query: '⚠', subjects: ['FINANCE_DECOY_KEY expires'] },
  { query: 'Welcome', subjects: [] },
])('filters the visible row text, as Legacy did, with $query', async ({ query, subjects }) => {
  const { page, fixture } = await loaded();
  const filter = page.querySelector<HTMLInputElement>('input[aria-label="Filter notifications"]');
  if (!filter) throw new Error('No notification filter');
  filter.value = query;
  filter.dispatchEvent(new Event('input'));
  await fixture.whenStable();
  expect([...page.querySelectorAll('tbody .cell-strong')].map((cell) => cell.textContent)).toEqual(
    subjects,
  );
});
