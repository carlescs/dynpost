import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faBan,
  faChevronRight,
  faCircleCheck,
  faCircleXmark,
  faSpinner,
  faTriangleExclamation,
} from '@fortawesome/free-solid-svg-icons';
import { IconDefinition } from '@fortawesome/fontawesome-svg-core';
import { RunResultDto, RunSampleDto, RunState, RunStatsDto } from '../../../../models/messages';
import { HttpMethod, METHOD_COLORS } from '../../../../models/workspace';
import {
  RUN_STATE_COLORS,
  RUN_STATE_LABELS,
  formatBytes,
  formatElapsed,
  formatMs,
  statusFill,
} from '../../models/run-format';
import { RunChart } from '../run-chart/run-chart';

const STATE_ICONS: Record<RunState, IconDefinition> = {
  running: faSpinner,
  completed: faCircleCheck,
  cancelled: faBan,
  failed: faCircleXmark,
};

const THRESHOLD_NAMES: Record<string, string> = {
  average: 'Average',
  errorRate: 'Error rate',
  throughput: 'Throughput',
};

/** The dashboard of a run, live or finished: headline numbers, charts and the samples kept. */
@Component({
  selector: 'app-run-results',
  imports: [DatePipe, DecimalPipe, FaIconComponent, RunChart],
  host: { class: 'flex flex-col gap-4' },
  templateUrl: './run-results.html',
})
export class RunResults {
  readonly run = input.required<RunResultDto>();

  protected readonly stateLabels = RUN_STATE_LABELS;
  protected readonly stateColors = RUN_STATE_COLORS;
  protected readonly stateIcons = STATE_ICONS;
  protected readonly faCircleCheck = faCircleCheck;
  protected readonly faCircleXmark = faCircleXmark;
  protected readonly faTriangleExclamation = faTriangleExclamation;
  protected readonly faChevronRight = faChevronRight;
  protected readonly formatMs = formatMs;
  protected readonly formatElapsed = formatElapsed;
  protected readonly formatBytes = formatBytes;
  protected readonly statusFill = statusFill;

  protected readonly heading = 'text-xs font-semibold tracking-wide text-slate-500 uppercase';
  protected readonly card = 'rounded-lg border border-slate-200 bg-white';

  /** The headline numbers, each with a smaller related one below. */
  protected readonly tiles = computed(() => {
    const total = this.run().total;
    return [
      {
        label: 'Requests',
        value: total.samples.toLocaleString('en-US'),
        detail: `${formatBytes(total.bytesReceived)} received`,
      },
      {
        label: 'Throughput',
        value: total.throughput.toFixed(1),
        unit: 'req/s',
        detail: `${formatBytes(total.bytesSent)} sent`,
      },
      {
        label: 'Errors',
        value: `${Number(total.errorPercent.toFixed(1))}%`,
        detail: `${total.errors.toLocaleString('en-US')} failed`,
        alert: total.errors > 0,
      },
      {
        label: 'Average',
        value: formatMs(total.average),
        detail: `median ${formatMs(total.median)}`,
      },
      { label: 'p95', value: formatMs(total.p95), detail: `p90 ${formatMs(total.p90)}` },
      { label: 'p99', value: formatMs(total.p99), detail: `max ${formatMs(total.max)}` },
    ];
  });

  /**
   * Each request's latency spread on one shared scale, in percent: the whole range (min to max)
   * and, inside it, where most requests fall (median to p95).
   */
  protected readonly requests = computed(() => {
    const requests = this.run().requests;
    const scale = Math.max(1, ...requests.map((r) => r.stats.max));
    const percent = (value: number) => (value / scale) * 100;
    return requests.map(({ request, stats }) => ({
      request,
      stats,
      range: { left: percent(stats.min), width: percent(stats.max - stats.min) },
      typical: { left: percent(stats.median), width: percent(stats.p95 - stats.median) },
      average: percent(stats.average),
    }));
  });

  protected readonly statusCodes = computed(() => this.shares(this.run().total.statusCodes));
  protected readonly errorReasons = computed(() =>
    this.shares(this.run().total.errorReasons).sort((a, b) => b.count - a.count),
  );

  protected thresholdName(name: string): string {
    return THRESHOLD_NAMES[name] ?? name;
  }

  protected methodColor(method: string): string {
    return METHOD_COLORS[method as HttpMethod] ?? 'text-slate-500';
  }

  protected dataRow(sample: RunSampleDto): [string, string][] {
    return Object.entries(sample.dataRow ?? {});
  }

  protected spreadTitle(stats: RunStatsDto): string {
    return (
      `min ${formatMs(stats.min)} · median ${formatMs(stats.median)} · p90 ${formatMs(stats.p90)}` +
      ` · p95 ${formatMs(stats.p95)} · p99 ${formatMs(stats.p99)} · max ${formatMs(stats.max)}`
    );
  }

  private shares(
    counts: Record<string, number>,
  ): { key: string; count: number; percent: number }[] {
    const entries = Object.entries(counts).filter(([, count]) => count > 0);
    const total = entries.reduce((sum, [, count]) => sum + count, 0);
    return entries.map(([key, count]) => ({ key, count, percent: (count / total) * 100 }));
  }
}
