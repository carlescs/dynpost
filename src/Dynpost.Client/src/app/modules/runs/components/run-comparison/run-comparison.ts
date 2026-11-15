import { Component, computed, inject, input, output } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faArrowRight, faCodeCompare, faXmark } from '@fortawesome/free-solid-svg-icons';
import {
  ComparedMetric,
  ComparisonRowDto,
  MetricDeltaDto,
  RunComparisonDto,
} from '../../../../models/messages';
import { DateFormatStore } from '../../../../services/date-format-store';
import {
  METRIC_LABELS,
  VERDICT_COLORS,
  formatChange,
  formatMetric,
} from '../../models/run-comparison';

/** The metrics of the per-request and per-environment tables, which are narrower than the total. */
const ROW_METRICS: ComparedMetric[] = ['errorPercent', 'median', 'p95', 'p99', 'throughput'];

/**
 * Two runs side by side: every metric of the whole run with its baseline and compared value, and
 * the main ones per request and per environment. Better changes are green, worse ones red; changes
 * within noise stay grey.
 */
@Component({
  selector: 'app-run-comparison',
  imports: [FaIconComponent],
  host: { class: 'flex flex-col gap-4' },
  templateUrl: './run-comparison.html',
})
export class RunComparison {
  protected readonly dates = inject(DateFormatStore);
  readonly comparison = input.required<RunComparisonDto>();
  /** Asks to close the comparison and show the dashboard again. */
  readonly close = output<void>();

  protected readonly faArrowRight = faArrowRight;
  protected readonly faCodeCompare = faCodeCompare;
  protected readonly faXmark = faXmark;
  protected readonly labels = METRIC_LABELS;
  protected readonly colors = VERDICT_COLORS;
  protected readonly rowMetrics = ROW_METRICS;
  protected readonly formatMetric = formatMetric;
  protected readonly formatChange = formatChange;

  protected readonly card = 'rounded-lg border border-slate-200 bg-white';
  protected readonly heading = 'text-xs font-semibold tracking-wide text-slate-500 uppercase';

  /** How many of the total's metrics got better and worse. */
  protected readonly tally = computed(() => {
    const metrics = this.comparison().total.metrics;
    return {
      better: metrics.filter((m) => m.verdict === 'better').length,
      worse: metrics.filter((m) => m.verdict === 'worse').length,
    };
  });

  protected readonly sections = computed(() => {
    const comparison = this.comparison();
    return [
      { id: 'request', title: 'Per request', rows: comparison.requests },
      { id: 'environment', title: 'Per environment', rows: comparison.environments },
    ].filter((section) => section.rows.length > 0);
  });

  /** The delta of `metric` in a row, or `undefined` when the row isn't in both runs. */
  protected deltaOf(row: ComparisonRowDto, metric: ComparedMetric): MetricDeltaDto | undefined {
    return row.metrics.find((m) => m.metric === metric);
  }

  /** Why a row has no deltas: it is in only one of the runs. */
  protected missingLabel(row: ComparisonRowDto): string {
    return row.inCurrent ? 'Not in the baseline run' : 'Only in the baseline run';
  }

  protected deltaTitle(delta: MetricDeltaDto): string {
    const label = this.labels[delta.metric];
    return `${label}: ${formatMetric(delta.metric, delta.baseline)} → ${formatMetric(delta.metric, delta.current)}`;
  }
}
