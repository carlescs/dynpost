import { GitFileStatus } from '../bridge/messages';

/** The letter and color that mark how a file changed. */
export const STATUS_BADGES: Record<GitFileStatus, { letter: string; color: string }> = {
  modified: { letter: 'M', color: 'text-amber-600' },
  added: { letter: 'A', color: 'text-emerald-600' },
  untracked: { letter: 'U', color: 'text-emerald-600' },
  renamed: { letter: 'R', color: 'text-sky-600' },
  deleted: { letter: 'D', color: 'text-rose-600' },
  conflicted: { letter: '!', color: 'text-rose-700' },
};

export function fileName(path: string): string {
  return path.slice(path.lastIndexOf('/') + 1);
}

export function folderOf(path: string): string {
  const index = path.lastIndexOf('/');
  return index < 0 ? '' : path.slice(0, index);
}

const RELATIVE_UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 3600],
  ['month', 30 * 24 * 3600],
  ['week', 7 * 24 * 3600],
  ['day', 24 * 3600],
  ['hour', 3600],
  ['minute', 60],
];

/** A short description of how long ago `date` (ISO 8601) was, such as "3 days ago". */
export function relativeTime(date: string, now = Date.now()): string {
  const seconds = Math.round((new Date(date).getTime() - now) / 1000);
  const format = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
  for (const [unit, size] of RELATIVE_UNITS) {
    if (Math.abs(seconds) >= size) return format.format(Math.trunc(seconds / size), unit);
  }
  return format.format(0, 'minute');
}
