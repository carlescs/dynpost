import { TestBed } from '@angular/core/testing';
import { DateFormat } from '../models/messages';
import { Bridge } from './bridge';
import { DateFormatStore } from './date-format-store';

const NAMES = Array.from({ length: 12 }, (_, i) => `M${i + 1}`);
const DAYS = Array.from({ length: 7 }, (_, i) => `D${i}`);

const FORMAT: DateFormat = {
  culture: 'test',
  shortDatePattern: 'yyyy-MM-dd',
  shortTimePattern: 'HH:mm',
  longTimePattern: 'HH:mm:ss',
  dateSeparator: '/',
  timeSeparator: ':',
  amDesignator: 'AM',
  pmDesignator: 'PM',
  monthNames: NAMES,
  abbreviatedMonthNames: NAMES,
  monthGenitiveNames: NAMES,
  abbreviatedMonthGenitiveNames: NAMES,
  dayNames: DAYS,
  abbreviatedDayNames: DAYS,
};

describe('DateFormatStore', () => {
  let send: ReturnType<typeof vi.fn>;
  let store: DateFormatStore;

  beforeEach(() => {
    send = vi.fn().mockResolvedValue(FORMAT);
    TestBed.configureTestingModule({ providers: [{ provide: Bridge, useValue: { send } }] });
    store = TestBed.inject(DateFormatStore);
  });

  it('formats dates with the system patterns once they are loaded', async () => {
    await store.load();
    const date = new Date(2026, 10, 12, 9, 5, 7);

    expect(send).toHaveBeenCalledWith('locale.dateFormat');
    expect(store.dateTime(date)).toBe('2026-11-12 09:05:07');
    expect(store.dateTimeShort(date.toISOString())).toBe('2026-11-12 09:05');
    expect(store.date(date.getTime())).toBe('2026-11-12');
  });

  it('falls back to the webview locale until the patterns are loaded', () => {
    const date = new Date(2026, 10, 12, 9, 5, 7);

    expect(store.dateTime(date)).toBe(date.toLocaleString());
    expect(store.date(date)).toBe(date.toLocaleDateString());
  });

  it('keeps the fallback when the patterns cannot be read', async () => {
    send.mockRejectedValue(new Error('no host'));
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);

    await store.load();

    expect(store.format()).toBeNull();
  });

  it('shows nothing for a missing date and an unparsable one as it is', async () => {
    await store.load();

    expect(store.dateTime(null)).toBe('');
    expect(store.dateTime(undefined)).toBe('');
    expect(store.dateTime('not a date')).toBe('not a date');
  });
});
