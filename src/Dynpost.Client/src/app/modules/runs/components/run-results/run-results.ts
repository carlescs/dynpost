import { DecimalPipe } from '@angular/common';
import { Component, computed, inject, input, output, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faBan,
  faChevronRight,
  faCircleCheck,
  faCircleXmark,
  faSpinner,
  faTableList,
  faTriangleExclamation,
} from '@fortawesome/free-solid-svg-icons';
import { IconDefinition } from '@fortawesome/fontawesome-svg-core';
import { RunResultDto, RunSampleDto, RunState, RunStatsDto } from '../../../../models/messages';
import { HttpMethod, METHOD_COLORS } from '../../../../models/workspace';
import { httpStatusTooltip } from '../../../../models/http-status';
import {
  RUN_STATE_COLORS,
  RUN_STATE_LABELS,
  formatBytes,
  formatElapsed,
  formatMs,
  statusFill,
} from '../../models/run-format';
import { RunChart } from '../run-chart/run-chart';
import { DateFormatStore } from '../../../../services/date-format-store';

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

/** What the statistics below the headline numbers are broken down by. */
export type StatsBreakdown = 'overall' | 'environment' | 'request';

/** Rows of statistics, each with its latency spread on one shared scale, in percent. */
function spreads(rows: { key: string; label: string; stats: RunStatsDto }[]) {
  const scale = Math.max(1, ...rows.map((r) => r.stats.max));
  const percent = (value: number) => (value / scale) * 100;
  return rows.map((row) => ({
    ...row,
    range: { left: percent(row.stats.min), width: percent(row.stats.max - row.stats.min) },
    typical: { left: percent(row.stats.median), width: percent(row.stats.p95 - row.stats.median) },
    average: percent(row.stats.average),
  }));
}

/** The dashboard of a run, live or finished: headline numbers, charts and the samples kept. */
@Component({
  selector: 'app-run-results',
  imports: [DecimalPipe, FaIconComponent, RunChart],
  host: { class: 'flex flex-col gap-4' },
  templateUrl: './run-results.html',
})
export class RunResults {
  protected readonly dates = inject(DateFormatStore);
  readonly run = input.required<RunResultDto>();
  /** Asks to show the table of every request sent with its response. */
  readonly showRequests = output<void>();

  protected readonly stateLabels = RUN_STATE_LABELS;
  protected readonly stateColors = RUN_STATE_COLORS;
  protected readonly stateIcons = STATE_ICONS;
  protected readonly faCircleCheck = faCircleCheck;
  protected readonly faCircleXmark = faCircleXmark;
  protected readonly faTriangleExclamation = faTriangleExclamation;
  protected readonly faChevronRight = faChevronRight;
  protected readonly faTableList = faTableList;
  protected readonly formatMs = formatMs;
  protected readonly formatElapsed = formatElapsed;
  protected readonly formatBytes = formatBytes;
  protected readonly statusFill = statusFill;
  protected readonly statusTooltip = httpStatusTooltip;

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
  protected readonly requests = computed(() =>
    spreads(
      this.run().requests.map(({ request, stats }) => ({ key: request, label: request, stats })),
    ),
  );

  /** Each environment's latency spread, like {@link requests}; empty when the plan lists none. */
  protected readonly environments = computed(() =>
    spreads(
      (this.run().environments ?? []).map(({ environment, name, stats }) => ({
        key: environment,
        label: name,
        stats,
      })),
    ),
  );

  /** The breakdowns this run offers: by environment only when the plan lists environments. */
  protected readonly breakdowns = computed(() => {
    const options: { id: StatsBreakdown; label: string }[] = [{ id: 'overall', label: 'Overall' }];
    if (this.environments().length) options.push({ id: 'environment', label: 'Per environment' });
    options.push({ id: 'request', label: 'Per request' });
    return options;
  });

  /** The breakdown picked with the switch; it stays when another run is shown. */
  protected readonly selectedBreakdown = signal<StatsBreakdown>('request');

  /** The breakdown shown: per request when the one picked is not available for this run. */
  protected readonly breakdown = computed(() => {
    const picked = this.selectedBreakdown();
    return this.breakdowns().some((option) => option.id === picked) ? picked : 'request';
  });

  /** The rows of the breakdown shown: one for the whole run when only the overall is shown. */
  protected readonly breakdownRows = computed(() => {
    switch (this.breakdown()) {
      case 'environment':
        return this.environments();
      case 'request':
        return this.requests();
      case 'overall':
        return spreads([{ key: 'All requests', label: 'All requests', stats: this.run().total }]);
    }
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
