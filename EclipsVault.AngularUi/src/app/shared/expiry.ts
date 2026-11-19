import { utcTimestamp } from './relative-time';

const hour = 3_600_000;
const day = 24 * hour;
const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** How an expiry reads: a tinted pill when it is close, plain text when it is not. */
export interface Expiry {
  text: string;
  /** `null` is plain text: no pill. */
  tone: 'danger' | 'warn' | null;
  /** The exact UTC time, for a tooltip on a pill. */
  title: string | null;
}

/** A date inside a pill, in UTC: "13 Nov", with the year only when it is not the current one. */
export function pillDate(timestampUtc: string, now: Date): string {
  const date = new Date(timestampUtc);
  const year = date.getUTCFullYear() === now.getUTCFullYear() ? '' : ` ${date.getUTCFullYear()}`;
  return `${date.getUTCDate()} ${months[date.getUTCMonth()]}${year}`;
}

/** Red under 3 days (or already past), yellow within the week, later dates as plain table text. */
export function expiry(timestampUtc: string | null, now: Date): Expiry {
  if (timestampUtc === null) return { text: 'No expiry', tone: null, title: null };
  const left = new Date(timestampUtc).getTime() - now.getTime();
  const date = pillDate(timestampUtc, now);
  const title = utcTimestamp(timestampUtc);
  if (left <= 0) return { text: `Expired · ${date}`, tone: 'danger', title };
  if (left < day) {
    const hours = Math.ceil(left / hour);
    return {
      text: `In ${hours} ${hours === 1 ? 'hour' : 'hours'} · ${date}`,
      tone: 'danger',
      title,
    };
  }
  if (left < 7 * day) {
    const days = Math.floor(left / day);
    const text = `In ${days} ${days === 1 ? 'day' : 'days'} · ${date}`;
    return { text, tone: left < 3 * day ? 'danger' : 'warn', title };
  }
  // A date in a plain table cell keeps the batch's `yyyy-MM-dd HH:mm` UTC form (owner, 16 Nov).
  return { text: title.slice(0, 16), tone: null, title: null };
}
