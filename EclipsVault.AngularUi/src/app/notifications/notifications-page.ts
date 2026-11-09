import { DatePipe, formatDate } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, computed, signal } from '@angular/core';
import { EmptyState } from '../shared/empty-state';
import { TableFilter } from '../shared/table-filter';
import type { components } from '../ui-api/schema.generated';

type Notification = components['schemas']['NotificationResponse'];

@Component({
  selector: 'app-notifications-page',
  imports: [DatePipe, EmptyState, TableFilter],
  templateUrl: './notifications-page.html',
})
export class NotificationsPage {
  protected readonly notifications = httpResource<components['schemas']['NotificationsResponse']>(
    () => '/api/ui/notifications',
  );
  protected readonly query = signal('');
  protected readonly filtered = computed(() => {
    const query = this.query().trim().toLowerCase();
    return (this.notifications.value()?.outbox ?? []).filter((email) =>
      [
        formatDate(email.sentAtUtc, 'yyyy-MM-dd HH:mm', 'en-US', 'UTC'),
        email.to,
        email.subject,
        email.eventType,
        email.transport,
        this.badges[email.status].label,
        email.status === 'failed' && email.error ? '⚠' : '',
      ]
        .join(' ')
        .toLowerCase()
        .includes(query),
    );
  });
  protected readonly badges: Record<Notification['status'], { tone: string; label: string }> = {
    sent: { tone: 'ok', label: 'Sent' },
    failed: { tone: 'danger', label: 'Failed' },
    suppressed: { tone: 'muted', label: 'Suppressed' },
  };
}
