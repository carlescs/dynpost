import { DateFormat } from './messages';

/** The pieces of a date the patterns refer to, in local time. */
const PATTERN_LETTERS = 'dfFghHKmMstyz';

/** Pads a number with leading zeros to `width` digits. */
function pad(value: number, width: number): string {
  return String(value).padStart(width, '0');
}

/** The UTC offset of `date` as `+hh:mm`, or with `zz`/`z` only the (padded) hours. */
function offset(date: Date, count: number): string {
  const minutes = -date.getTimezoneOffset();
  const sign = minutes < 0 ? '-' : '+';
  const hours = Math.trunc(Math.abs(minutes) / 60);
  if (count === 1) return `${sign}${hours}`;
  if (count === 2) return `${sign}${pad(hours, 2)}`;
  return `${sign}${pad(hours, 2)}:${pad(Math.abs(minutes) % 60, 2)}`;
}

/** Whether a pattern has a day number (`d` or `dd`), which makes month names genitive. */
function hasDayNumber(pattern: string): boolean {
  return /(^|[^d])dd?([^d]|$)/.test(pattern.replace(/'[^']*'|"[^"]*"|\\./g, ''));
}

/**
 * Formats `date` (local time) with a .NET custom date and time format pattern, the kind the
 * operating system's regional settings use (`dd/MM/yyyy`, `h:mm:ss tt`, `d. MMMM yyyy`), with the
 * names and separators in `format`.
 */
export function formatDatePattern(date: Date, pattern: string, format: DateFormat): string {
  const genitive = hasDayNumber(pattern);
  let result = '';
  let index = 0;
  while (index < pattern.length) {
    const char = pattern[index];
    if (char === "'" || char === '"') {
      const end = pattern.indexOf(char, index + 1);
      const stop = end < 0 ? pattern.length : end;
      result += pattern.slice(index + 1, stop);
      index = stop + 1;
      continue;
    }
    if (char === '\\') {
      result += pattern[index + 1] ?? '';
      index += 2;
      continue;
    }
    if (char === '%') {
      index++;
      continue;
    }
    if (char === '/') {
      result += format.dateSeparator;
      index++;
      continue;
    }
    if (char === ':') {
      result += format.timeSeparator;
      index++;
      continue;
    }
    if (!PATTERN_LETTERS.includes(char)) {
      result += char;
      index++;
      continue;
    }
    let count = 1;
    while (pattern[index + count] === char) count++;
    index += count;
    result += field(date, char, count, format, genitive);
  }
  return result;
}

function field(
  date: Date,
  letter: string,
  count: number,
  format: DateFormat,
  genitive: boolean,
): string {
  const hours = date.getHours();
  switch (letter) {
    case 'd':
      if (count <= 2) return pad(date.getDate(), count);
      return (count === 3 ? format.abbreviatedDayNames : format.dayNames)[date.getDay()];
    case 'M': {
      const month = date.getMonth();
      if (count <= 2) return pad(month + 1, count);
      if (count === 3) {
        return (genitive ? format.abbreviatedMonthGenitiveNames : format.abbreviatedMonthNames)[
          month
        ];
      }
      return (genitive ? format.monthGenitiveNames : format.monthNames)[month];
    }
    case 'y': {
      const year = date.getFullYear();
      if (count === 1) return String(year % 100);
      if (count === 2) return pad(year % 100, 2);
      return pad(year, count);
    }
    case 'h':
      return pad(hours % 12 || 12, Math.min(count, 2));
    case 'H':
      return pad(hours, Math.min(count, 2));
    case 'm':
      return pad(date.getMinutes(), Math.min(count, 2));
    case 's':
      return pad(date.getSeconds(), Math.min(count, 2));
    case 't': {
      const designator = hours < 12 ? format.amDesignator : format.pmDesignator;
      return count === 1 ? designator.slice(0, 1) : designator;
    }
    case 'f':
    case 'F': {
      const digits = pad(date.getMilliseconds(), 3).padEnd(count, '0').slice(0, count);
      return letter === 'F' ? digits.replace(/0+$/, '') : digits;
    }
    case 'z':
      return offset(date, count);
    case 'K':
      return offset(date, 3);
    // The era (`g`) is left out: the Gregorian calendar's is never shown in regional formats.
    default:
      return '';
  }
}
