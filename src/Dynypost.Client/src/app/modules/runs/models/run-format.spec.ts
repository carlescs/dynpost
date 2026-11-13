import {
  formatBytes,
  formatElapsed,
  formatMs,
  niceMax,
  parseDuration,
  statusFill,
} from './run-format';

describe('run-format', () => {
  it('parses plan durations', () => {
    expect(parseDuration('500ms')).toBe(500);
    expect(parseDuration('10s')).toBe(10_000);
    expect(parseDuration(' 2M ')).toBe(120_000);
    expect(parseDuration('1h')).toBe(3_600_000);
    expect(parseDuration('3')).toBe(3000);
    expect(parseDuration('soon')).toBeNull();
    expect(parseDuration('')).toBeNull();
  });

  it('formats latencies and elapsed times', () => {
    expect(formatMs(84.6)).toBe('85 ms');
    expect(formatMs(1250)).toBe('1.25 s');
    expect(formatElapsed(6000)).toBe('6.0 s');
    expect(formatElapsed(125_000)).toBe('2m 05s');
    expect(formatElapsed(3_780_000)).toBe('1h 03m');
  });

  it('formats sizes', () => {
    expect(formatBytes(512)).toBe('512 B');
    expect(formatBytes(1536)).toBe('1.5 KB');
  });

  it('rounds chart maximums up to a round number', () => {
    expect(niceMax(0)).toBe(1);
    expect(niceMax(40)).toBe(50);
    expect(niceMax(110)).toBe(200);
    expect(niceMax(230)).toBe(250);
    expect(niceMax(500)).toBe(500);
  });

  it('colors status codes by class', () => {
    expect(statusFill(201)).toBe('bg-emerald-500');
    expect(statusFill('503')).toBe('bg-rose-500');
    expect(statusFill(null)).toBe('bg-slate-400');
  });
});
