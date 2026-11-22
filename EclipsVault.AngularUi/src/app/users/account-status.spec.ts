import { accountStatus } from './account-status';

const now = new Date('2026-11-23T12:00:00Z');

describe('accountStatus', () => {
  it.each([
    [{ status: 'active', awaitingActivation: false }, 'Active', 'tone-ok'],
    [{ status: 'locked', awaitingActivation: false }, 'Locked', 'tone-warn'],
    [{ status: 'disabled', awaitingActivation: true }, 'Disabled', 'tone-danger'],
    [{ status: 'active', awaitingActivation: true }, 'Invitation expired', 'tone-danger'],
  ] as const)('reads %o as %s', (user, label, tone) => {
    expect(accountStatus({ ...user, invitationExpiresAtUtc: null }, now)).toEqual({ label, tone });
  });

  it('dates a usable invitation in UTC, with the year only when it is not this one', () => {
    const invited = { status: 'active', awaitingActivation: true } as const;
    expect(
      accountStatus({ ...invited, invitationExpiresAtUtc: '2026-11-26T09:05:00Z' }, now),
    ).toEqual({ label: 'Invited · expires 26 Nov, 09:05 UTC', tone: 'tone-info' });
    expect(
      accountStatus({ ...invited, invitationExpiresAtUtc: '2027-01-01T23:59:00Z' }, now).label,
    ).toBe('Invited · expires 1 Jan 2027, 23:59 UTC');
  });
});
