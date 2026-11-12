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
});
