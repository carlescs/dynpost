import { Injectable, inject, signal } from '@angular/core';
import { formatDatePattern } from '../models/date-format';
import { DateFormat } from '../models/messages';
import { Bridge } from './bridge';

type DateInput = string | number | Date | null | undefined;

/**
 * Formats dates the way the operating system does, with the patterns its regional settings use
 * (read by the host, so customized formats are followed too). Until they are loaded, or if they
 * can't be, dates fall back to the webview's locale.
 */
@Injectable({ providedIn: 'root' })
export class DateFormatStore {
  private readonly bridge = inject(Bridge);

  readonly format = signal<DateFormat | null>(null);

  async load(): Promise<void> {
    try {
      this.format.set(await this.bridge.send('locale.dateFormat'));
    } catch (error) {
      console.warn('Could not read the date format', error);
    }
  }

  /** A date and time with seconds, such as `12/11/2026 14:05:09`. */
  dateTime(value: DateInput): string {
    return this.apply(
      value,
      (format) => `${format.shortDatePattern} ${format.longTimePattern}`,
      (date) => date.toLocaleString(),
    );
  }

  /** A date and time without seconds, such as `12/11/2026 14:05`. */
  dateTimeShort(value: DateInput): string {
    return this.apply(
      value,
      (format) => `${format.shortDatePattern} ${format.shortTimePattern}`,
      (date) => date.toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' }),
    );
  }

  /** A date alone, such as `12/11/2026`. */
  date(value: DateInput): string {
    return this.apply(
      value,
      (format) => format.shortDatePattern,
      (date) => date.toLocaleDateString(),
    );
  }

  /**
   * Formats `value` (an ISO 8601 string, a timestamp or a date): empty when there is none, and
   * an unparsable string as it is.
   */
  private apply(
    value: DateInput,
    pattern: (format: DateFormat) => string,
    fallback: (date: Date) => string,
  ): string {
    if (value === null || value === undefined || value === '') return '';
    const date = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(date.getTime())) return typeof value === 'string' ? value : '';
    const format = this.format();
    return format ? formatDatePattern(date, pattern(format), format) : fallback(date);
  }
}
