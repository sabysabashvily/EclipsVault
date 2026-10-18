// The Legacy UI's "time ago" (AuditDisplay.Ago), measured against the browser's clock instead of
// the server's. A timestamp ahead of the clock reads "just now", as Legacy's negative span does.

const minute = 60_000;
const hour = 60 * minute;
const day = 24 * hour;

/** "just now", "Nm ago", "Nh ago", "Nd ago" under 7 days, then the UTC date as `yyyy-MM-dd`. */
export function relativeTime(timestampUtc: string, now: Date): string {
  const timestamp = new Date(timestampUtc);
  const span = now.getTime() - timestamp.getTime();
  if (span < minute) return 'just now';
  if (span < hour) return `${Math.trunc(span / minute)}m ago`;
  if (span < day) return `${Math.trunc(span / hour)}h ago`;
  if (span < 7 * day) return `${Math.trunc(span / day)}d ago`;
  return timestamp.toISOString().slice(0, 10);
}

/** The exact time for a tooltip: `yyyy-MM-dd HH:mm:ss UTC`. */
export function utcTimestamp(timestampUtc: string): string {
  return `${new Date(timestampUtc).toISOString().slice(0, 19).replace('T', ' ')} UTC`;
}
