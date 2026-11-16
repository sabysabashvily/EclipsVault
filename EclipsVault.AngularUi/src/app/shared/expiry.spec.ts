import { expiry, pillDate } from './expiry';

describe('expiry', () => {
  const now = new Date('2026-11-16T12:00:00Z');

  it('reads "No expiry" as plain text when there is no end', () => {
    expect(expiry(null, now)).toEqual({ text: 'No expiry', tone: null, title: null });
  });

  it('is red once past and within 3 days, counting hours on the last day', () => {
    expect(expiry('2026-11-16T11:00:00Z', now)).toEqual({
      text: 'Expired · 16 Nov',
      tone: 'danger',
      title: '2026-11-16 11:00:00 UTC',
    });
    expect(expiry('2026-11-16T12:30:00Z', now).text).toBe('In 1 hour · 16 Nov');
    expect(expiry('2026-11-17T09:00:00Z', now).text).toBe('In 21 hours · 17 Nov');
    expect(expiry('2026-11-17T12:00:00Z', now)).toMatchObject({
      text: 'In 1 day · 17 Nov',
      tone: 'danger',
    });
    expect(expiry('2026-11-19T11:59:00Z', now)).toMatchObject({
      text: 'In 2 days · 19 Nov',
      tone: 'danger',
    });
  });

  it('is yellow from 3 days until a week', () => {
    expect(expiry('2026-11-19T12:00:00Z', now)).toMatchObject({
      text: 'In 3 days · 19 Nov',
      tone: 'warn',
    });
    expect(expiry('2026-11-23T11:59:00Z', now)).toMatchObject({
      text: 'In 6 days · 23 Nov',
      tone: 'warn',
    });
  });

  it('shows a date a week or more away as plain table text', () => {
    expect(expiry('2026-11-23T12:00:00Z', now)).toEqual({
      text: '2026-11-23 12:00',
      tone: null,
      title: null,
    });
  });
});

describe('pillDate', () => {
  it('adds the year only outside the current one', () => {
    const now = new Date('2026-12-31T23:00:00Z');
    expect(pillDate('2026-01-02T00:00:00Z', now)).toBe('2 Jan');
    expect(pillDate('2027-01-02T00:00:00Z', now)).toBe('2 Jan 2027');
  });
});
