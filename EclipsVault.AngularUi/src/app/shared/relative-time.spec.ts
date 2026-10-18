import { relativeTime, utcTimestamp } from './relative-time';

describe('relative time', () => {
  const now = new Date('2026-10-18T12:00:00.000Z');
  const before = (milliseconds: number) => new Date(now.getTime() - milliseconds).toISOString();
  const second = 1_000;
  const minute = 60 * second;
  const hour = 60 * minute;
  const day = 24 * hour;

  it.each([
    [0, 'just now'],
    [59_999, 'just now'],
    [minute, '1m ago'],
    [hour - 1, '59m ago'],
    [hour, '1h ago'],
    [day - 1, '23h ago'],
    [day, '1d ago'],
    [7 * day - 1, '6d ago'],
    [7 * day, '2026-10-11'],
    [400 * day, '2025-09-13'],
  ])('reads %i ms ago as %s', (ago, expected) => {
    expect(relativeTime(before(ago), now)).toBe(expected);
  });

  it('reads a timestamp ahead of the clock as just now', () => {
    expect(relativeTime(before(-2 * day), now)).toBe('just now');
  });

  it('dates an old entry by its UTC day, not the local one', () => {
    expect(relativeTime('2026-10-01T23:30:00Z', now)).toBe('2026-10-01');
    expect(relativeTime('2026-10-02T00:30:00Z', now)).toBe('2026-10-02');
  });

  it('gives the exact UTC time for the tooltip', () => {
    expect(utcTimestamp('2026-10-18T09:05:07.891Z')).toBe('2026-10-18 09:05:07 UTC');
    expect(utcTimestamp('2026-01-02T00:00:00Z')).toBe('2026-01-02 00:00:00 UTC');
  });
});
