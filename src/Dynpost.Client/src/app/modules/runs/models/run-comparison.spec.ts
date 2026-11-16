import { MetricDeltaDto } from '../../../models/messages';
import { formatChange, formatMetric } from './run-comparison';

const delta = (overrides: Partial<MetricDeltaDto>): MetricDeltaDto => ({
  metric: 'p95',
  baseline: 100,
  current: 100,
  change: 0,
  changePercent: 0,
  verdict: 'same',
  ...overrides,
});

describe('formatMetric', () => {
  it('shows each metric in its unit', () => {
    expect(formatMetric('samples', 12000)).toBe('12,000');
    expect(formatMetric('errorPercent', 2.456)).toBe('2.46%');
    expect(formatMetric('throughput', 40.25)).toBe('40.3/s');
    expect(formatMetric('p95', 85.4)).toBe('85 ms');
  });
});

describe('formatChange', () => {
  it('shows relative changes', () => {
    expect(formatChange(delta({ change: -25, changePercent: -25 }))).toBe('−25%');
    expect(formatChange(delta({ change: 4, changePercent: 4.04 }))).toBe('+4%');
    expect(formatChange(delta({ change: 4.5, changePercent: 4.5 }))).toBe('+4.5%');
  });

  it('shows the error rate in percentage points', () => {
    expect(formatChange(delta({ metric: 'errorPercent', change: 1.5, changePercent: null }))).toBe(
      '+1.5 pts',
    );
  });

  it('marks values that were 0 and no changes', () => {
    expect(formatChange(delta({ change: 5, changePercent: null }))).toBe('new');
    expect(formatChange(delta({}))).toBe('±0');
  });
});
