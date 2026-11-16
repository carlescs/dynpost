import { ComparedMetric, DeltaVerdict, MetricDeltaDto } from '../../../models/messages';
import { formatMs } from './run-format';

/** The name shown for each compared metric. */
export const METRIC_LABELS: Record<ComparedMetric, string> = {
  samples: 'Requests',
  errorPercent: 'Error rate',
  average: 'Average',
  median: 'Median',
  p90: 'p90',
  p95: 'p95',
  p99: 'p99',
  max: 'Max',
  throughput: 'Throughput',
};

/** The text color of a delta by its verdict. */
export const VERDICT_COLORS: Record<DeltaVerdict, string> = {
  better: 'text-emerald-700',
  worse: 'text-rose-700',
  same: 'text-slate-500',
};

/** A value of a compared metric in its unit: `1,200`, `2.5%`, `85 ms`, `40.2/s`. */
export function formatMetric(metric: ComparedMetric, value: number): string {
  switch (metric) {
    case 'samples':
      return Math.round(value).toLocaleString('en-US');
    case 'errorPercent':
      return `${trim(value, 2)}%`;
    case 'throughput':
      return `${trim(value, 1)}/s`;
    default:
      return formatMs(value);
  }
}

/**
 * How a metric changed: in percentage points for the error rate (`+1.5 pts`), relative to the
 * baseline otherwise (`-12%`); `new` when the baseline was 0, `±0` when nothing changed.
 */
export function formatChange(delta: MetricDeltaDto): string {
  if (delta.change === 0) return '±0';
  if (delta.metric === 'errorPercent')
    return `${sign(delta.change)}${trim(Math.abs(delta.change), 2)} pts`;
  if (delta.changePercent === null) return 'new';
  const percent = Math.abs(delta.changePercent);
  return `${sign(delta.changePercent)}${trim(percent, percent < 10 ? 1 : 0)}%`;
}

function sign(value: number): string {
  return value < 0 ? '−' : '+';
}

function trim(value: number, digits: number): string {
  return String(Number(value.toFixed(digits)));
}
