import { httpResource } from '@angular/common/http';
import { Component, computed } from '@angular/core';
import { RouterLink } from '@angular/router';
import { EmptyState } from '../shared/empty-state';
import { relativeTime, utcTimestamp } from '../shared/relative-time';
import type { components, paths } from '../ui-api/schema.generated';

type SignIns = paths['/api/ui/sign-ins']['get']['responses'][200]['content']['application/json'];
type SignInEvent = components['schemas']['SignInEventResponse'];

const outcomes: Record<SignInEvent['outcome'], { label: string; tone: string }> = {
  Success: { label: 'Success', tone: 'ok' },
  Failed: { label: 'Failed', tone: 'warn' },
  Blocked: { label: 'Blocked', tone: 'danger' },
  Info: { label: 'Info', tone: 'muted' },
};

const count = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

const methods: Record<SignInEvent['method'], string> = {
  Password: 'Password + 2FA',
  TwoFactor: 'Two-factor',
  Passkey: 'Passkey',
  RecoveryCode: 'Recovery code',
  StepUp: 'Step-up',
  System: 'System',
};

// Nothing worth flagging for a known location or an informational event.
const locations: Record<SignInEvent['locationFlag'], { text: string; tone: string } | null> = {
  None: null,
  FirstSeen: { text: 'New location', tone: 'muted' },
  Unfamiliar: { text: 'Unfamiliar location', tone: 'danger' },
};

/** The signed-in account's recent sign-in events and their summary. The server picks the account. */
@Component({
  selector: 'app-sign-ins-page',
  imports: [EmptyState, RouterLink],
  templateUrl: './sign-ins-page.html',
})
export class SignInsPage {
  protected readonly history = httpResource<SignIns>(() => '/api/ui/sign-ins');

  /** The summary and events as shown, with times measured against the clock when the page arrived. */
  protected readonly view = computed(() => {
    if (!this.history.hasValue()) return null;
    const { summary, events } = this.history.value();
    const now = new Date();
    const when = (timestampUtc: string | null) =>
      timestampUtc === null
        ? null
        : { ago: relativeTime(timestampUtc, now), utc: utcTimestamp(timestampUtc) };
    return {
      summary: {
        ...summary,
        failedCount: Number(summary.failedCount),
        suspiciousCount: Number(summary.suspiciousCount),
        lastSuccess: when(summary.lastSuccessUtc),
        lastFailed: when(summary.lastFailedUtc),
      },
      events: events.map((event) => ({
        ...event,
        outcomeLabel: outcomes[event.outcome].label,
        outcomeTone: outcomes[event.outcome].tone,
        methodLabel: methods[event.method],
        location: locations[event.locationFlag],
        ago: relativeTime(event.timestampUtc, now),
        utc: utcTimestamp(event.timestampUtc),
      })),
    };
  });

  /** The line under the title: what the history holds, or what needs a second look. */
  protected readonly context = computed(() => {
    const model = this.view();
    if (!model || model.events.length === 0)
      return "Every recent attempt to sign in to your account — successful and rejected — newest first. Only your own sign-ins appear here. An attempt from a location you've never signed in from is flagged so you can spot anything you didn't do.";
    const { summary } = model;
    const counts = summary.needsAttention
      ? `${count(summary.suspiciousCount, 'failed attempt', 'failed attempts')} came from places you've never signed in from`
      : `${count(Number(summary.successCount), 'sign-in', 'sign-ins')} and ${count(summary.failedCount, 'rejected attempt', 'rejected attempts')}`;
    return `${counts} · only your own sign-ins appear here.`;
  });
}
