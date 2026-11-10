import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faBan,
  faCircleCheck,
  faCircleXmark,
  faSpinner,
  faTrash,
} from '@fortawesome/free-solid-svg-icons';
import { RunSummaryDto } from '../../../../models/messages';
import { RUN_STATE_LABELS, formatElapsed, formatMs } from '../../models/run-format';

/**
 * The earlier runs of a plan, newest first; their p95 bars share one scale so runs compare. Each
 * run can be deleted, or the whole history cleared.
 */
@Component({
  selector: 'app-run-history',
  imports: [DatePipe, DecimalPipe, FaIconComponent],
  host: { class: 'block' },
  templateUrl: './run-history.html',
})
export class RunHistory {
  readonly runs = input.required<RunSummaryDto[]>();
  /** The run shown in the dashboard. */
  readonly selected = input<string | null>(null);
  readonly select = output<string>();
  /** Asks to delete a run, by id. */
  readonly remove = output<string>();
  /** Asks to delete every run of the plan. */
  readonly clear = output<void>();

  protected readonly faTrash = faTrash;

  protected readonly formatMs = formatMs;
  protected readonly formatElapsed = formatElapsed;

  protected readonly rows = computed(() => {
    const runs = this.runs();
    const scale = Math.max(1, ...runs.map((run) => run.p95));
    return runs.map((run) => ({ run, bar: (run.p95 / scale) * 100, ...this.verdict(run) }));
  });

  /** A run is judged by its thresholds when it has a result, by how it ended otherwise. */
  private verdict(run: RunSummaryDto) {
    if (run.passed === true)
      return { icon: faCircleCheck, color: 'text-emerald-700', label: 'Thresholds passed' };
    if (run.passed === false)
      return { icon: faCircleXmark, color: 'text-rose-700', label: 'Thresholds failed' };
    const label = RUN_STATE_LABELS[run.state];
    switch (run.state) {
      case 'running':
        return { icon: faSpinner, color: 'text-sky-700', label };
      case 'cancelled':
        return { icon: faBan, color: 'text-amber-700', label };
      case 'failed':
        return { icon: faCircleXmark, color: 'text-rose-700', label };
      default:
        return { icon: faCircleCheck, color: 'text-slate-500', label };
    }
  }
}
