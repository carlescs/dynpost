import { formatDatePattern } from './date-format';
import { DateFormat } from './messages';

const MONTHS = ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio', 'agosto'];
const ALL_MONTHS = [...MONTHS, 'septiembre', 'octubre', 'noviembre', 'diciembre'];

const FORMAT: DateFormat = {
  culture: 'test',
  shortDatePattern: 'dd/MM/yyyy',
  shortTimePattern: 'H:mm',
  longTimePattern: 'H:mm:ss',
  dateSeparator: '-',
  timeSeparator: '.',
  amDesignator: 'a. m.',
  pmDesignator: 'p. m.',
  monthNames: ALL_MONTHS,
  abbreviatedMonthNames: ALL_MONTHS.map((name) => name.slice(0, 3)),
  monthGenitiveNames: ALL_MONTHS.map((name) => `de ${name}`),
  abbreviatedMonthGenitiveNames: ALL_MONTHS.map((name) => `de ${name.slice(0, 3)}`),
  dayNames: ['domingo', 'lunes', 'martes', 'miércoles', 'jueves', 'viernes', 'sábado'],
  abbreviatedDayNames: ['dom', 'lun', 'mar', 'mié', 'jue', 'vie', 'sáb'],
};

// Thursday, March 5, 2026, 14:07:09.045 local time.
const DATE = new Date(2026, 2, 5, 14, 7, 9, 45);

describe('formatDatePattern', () => {
  it('formats numbers with the separators of the format', () => {
    expect(formatDatePattern(DATE, 'dd/MM/yyyy HH:mm:ss', FORMAT)).toBe('05-03-2026 14.07.09');
    expect(formatDatePattern(DATE, 'd/M/yy H:m:s', FORMAT)).toBe('5-3-26 14.7.9');
  });

  it('uses 12-hour clocks and designators', () => {
    expect(formatDatePattern(DATE, 'h:mm tt', FORMAT)).toBe('2.07 p. m.');
    expect(formatDatePattern(new Date(2026, 0, 1, 0, 5), 'hh:mm t', FORMAT)).toBe('12.05 a');
  });

  it('writes day and month names', () => {
    expect(formatDatePattern(DATE, 'dddd, MMMM yyyy', FORMAT)).toBe('jueves, marzo 2026');
    expect(formatDatePattern(DATE, 'ddd MMM', FORMAT)).toBe('jue mar');
  });

  it('uses genitive month names next to a day number', () => {
    expect(formatDatePattern(DATE, 'd MMMM yyyy', FORMAT)).toBe('5 de marzo 2026');
    expect(formatDatePattern(DATE, 'dd MMM', FORMAT)).toBe('05 de mar');
  });

  it('keeps quoted and escaped text as it is', () => {
    expect(formatDatePattern(DATE, "d 'de' MMMM", FORMAT)).toBe('5 de de marzo');
    expect(formatDatePattern(DATE, 'yyyy"/"MM\\/dd', FORMAT)).toBe('2026/03/05');
    expect(formatDatePattern(DATE, "'dddd' yyyy", FORMAT)).toBe('dddd 2026');
  });

  it('writes fractions of a second', () => {
    expect(formatDatePattern(DATE, 'ss.fff', FORMAT)).toBe('09.045');
    expect(formatDatePattern(DATE, 'ss.ff', FORMAT)).toBe('09.04');
    expect(formatDatePattern(new Date(2026, 0, 1, 0, 0, 1, 500), 'ss.FFF', FORMAT)).toBe('01.5');
  });

  it('reads a lone letter after % as a pattern', () => {
    expect(formatDatePattern(DATE, '%d', FORMAT)).toBe('5');
  });

  it('writes years with as many digits as the pattern asks for', () => {
    const early = new Date(2009, 0, 1);
    expect(formatDatePattern(early, 'y', FORMAT)).toBe('9');
    expect(formatDatePattern(early, 'yy', FORMAT)).toBe('09');
    expect(formatDatePattern(early, 'yyy', FORMAT)).toBe('2009');
    expect(formatDatePattern(early, 'yyyyy', FORMAT)).toBe('02009');
  });

  it('writes hours, minutes and seconds with at most two digits', () => {
    expect(formatDatePattern(DATE, 'HHH:mmm:sss', FORMAT)).toBe('14.07.09');
    expect(formatDatePattern(new Date(2026, 0, 1, 12, 0), 'h tt', FORMAT)).toBe('12 p. m.');
    expect(formatDatePattern(new Date(2026, 0, 1, 9, 0), 'h tt', FORMAT)).toBe('9 a. m.');
  });

  it('does not use genitive month names with a day name alone', () => {
    expect(formatDatePattern(DATE, 'dddd MMMM', FORMAT)).toBe('jueves marzo');
    expect(formatDatePattern(DATE, "MMMM 'd'", FORMAT)).toBe('marzo d');
  });

  it('writes the UTC offset', () => {
    const minutes = -DATE.getTimezoneOffset();
    const sign = minutes < 0 ? '-' : '+';
    const hours = Math.trunc(Math.abs(minutes) / 60);
    const full = `${sign}${String(hours).padStart(2, '0')}:${String(Math.abs(minutes) % 60).padStart(2, '0')}`;

    expect(formatDatePattern(DATE, 'z', FORMAT)).toBe(`${sign}${hours}`);
    expect(formatDatePattern(DATE, 'zz', FORMAT)).toBe(`${sign}${String(hours).padStart(2, '0')}`);
    expect(formatDatePattern(DATE, 'zzz', FORMAT)).toBe(full);
    expect(formatDatePattern(DATE, 'K', FORMAT)).toBe(full);
  });

  it('leaves out the era and keeps other characters', () => {
    expect(formatDatePattern(DATE, 'yyyy g', FORMAT)).toBe('2026 ');
    expect(formatDatePattern(DATE, 'yyyy年M月d日', FORMAT)).toBe('2026年3月5日');
    expect(formatDatePattern(DATE, 'd-M (yyyy)', FORMAT)).toBe('5-3 (2026)');
  });

  it('copies the rest of the pattern after an unclosed quote', () => {
    expect(formatDatePattern(DATE, "d 'MMMM", FORMAT)).toBe('5 MMMM');
  });

  it('formats an empty pattern as nothing', () => {
    expect(formatDatePattern(DATE, '', FORMAT)).toBe('');
  });
});
