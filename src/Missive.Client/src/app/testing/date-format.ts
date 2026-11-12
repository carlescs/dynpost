import { TestBed } from '@angular/core/testing';
import { DateFormat } from '../models/messages';
import { DateFormatStore } from '../services/date-format-store';

const MONTHS = Array.from({ length: 12 }, (_, i) => `Month${i + 1}`);
const DAYS = Array.from({ length: 7 }, (_, i) => `Day${i}`);

/**
 * A regional format no browser locale produces (`dd.MM.yyyy HH:mm:ss`), so a test can tell a
 * date formatted with the system's patterns from one formatted with the webview's locale.
 */
export const TEST_DATE_FORMAT: DateFormat = {
  culture: 'test',
  shortDatePattern: 'dd.MM.yyyy',
  shortTimePattern: 'HH:mm',
  longTimePattern: 'HH:mm:ss',
  dateSeparator: '/',
  timeSeparator: ':',
  amDesignator: 'AM',
  pmDesignator: 'PM',
  monthNames: MONTHS,
  abbreviatedMonthNames: MONTHS,
  monthGenitiveNames: MONTHS,
  abbreviatedMonthGenitiveNames: MONTHS,
  dayNames: DAYS,
  abbreviatedDayNames: DAYS,
};

/** Makes the root `DateFormatStore` use `TEST_DATE_FORMAT`, as if the host had sent it. */
export function useTestDateFormat(): void {
  TestBed.inject(DateFormatStore).format.set(TEST_DATE_FORMAT);
}

const pad = (value: number) => String(value).padStart(2, '0');

/** `value` in local time as `TEST_DATE_FORMAT` writes it, worked out without the formatter. */
export function testDate(value: string): string {
  const date = new Date(value);
  return `${pad(date.getDate())}.${pad(date.getMonth() + 1)}.${date.getFullYear()}`;
}

/** `value` in local time as `TEST_DATE_FORMAT` writes a date and time with seconds. */
export function testDateTime(value: string): string {
  const date = new Date(value);
  const time = `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
  return `${testDate(value)} ${time}`;
}

/** `value` in local time as `TEST_DATE_FORMAT` writes a date and time without seconds. */
export function testDateTimeShort(value: string): string {
  const date = new Date(value);
  return `${testDate(value)} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
