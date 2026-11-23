import { pillDate } from '../shared/expiry';
import { utcTimestamp } from '../shared/relative-time';
import type { components } from '../ui-api/schema.generated';

type User = components['schemas']['UserResponse'];

export interface Status {
  label: string;
  tone: string;
}
const statuses: Record<User['status'], Status> = {
  active: { label: 'Active', tone: 'tone-ok' },
  locked: { label: 'Locked', tone: 'tone-warn' },
  disabled: { label: 'Disabled', tone: 'tone-danger' },
};

/** The user page has no sign-in status; only an invited account shows its pill there. */
type InvitationState = Pick<User, 'awaitingActivation' | 'invitationExpiresAtUtc'> & {
  status?: User['status'];
};

// Where "HH:MM" sits in utcTimestamp's "YYYY-MM-DD HH:MM:SS UTC".
const hourStart = 11;
const minuteEnd = 16;

/** An account's status pill. An invited account shows its link's state until it is disabled. */
export function accountStatus(user: InvitationState, now = new Date()): Status {
  const status = user.status ?? 'active';
  if (status === 'disabled' || !user.awaitingActivation) return statuses[status];
  if (!user.invitationExpiresAtUtc) return { label: 'Invitation expired', tone: 'tone-danger' };
  const time = utcTimestamp(user.invitationExpiresAtUtc).slice(hourStart, minuteEnd);
  return {
    label: `Invited · expires ${pillDate(user.invitationExpiresAtUtc, now)}, ${time} UTC`,
    tone: 'tone-info',
  };
}

/** What a resend says: the old link is gone either way, so a failed delivery is an error. */
export function resendOutcome(
  sent: components['schemas']['InvitationResponse'],
  email: string,
): { message: string; error: boolean } {
  return sent.invitation === 'sent'
    ? { message: `Invitation sent to ${email}. It expires in 72 hours.`, error: false }
    : {
        message: 'A new link was made, but it could not be delivered. Resend it to try again.',
        error: true,
      };
}

/** The resend confirmation, the same on the Users list and the user page. */
export function resendQuestion(user: { username: string; email: string }) {
  return {
    message: `Send a new invitation to ${user.email}? The previous link stops working.`,
    options: {
      title: `Resend invitation to ${user.username}?`,
      confirm: 'Resend invitation',
      tone: 'primary' as const,
      note: 'Saved to the audit log',
    },
  };
}
