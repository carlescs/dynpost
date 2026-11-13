import { DecimalPipe } from '@angular/common';
import { Component, computed, inject, input, output, signal } from '@angular/core';
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
import { DateFormatStore } from '../../../../services/date-format-store';

/**
 * The earlier runs of a plan, newest first; their p95 bars share one scale so runs compare. Each
 * run can be deleted, several can be checked (shift-click checks a range) and deleted together, or
 * the whole history cleared.
 */
@Component({
  selector: 'app-run-history',
  imports: [DecimalPipe, FaIconComponent],
  host: { class: 'block' },
  templateUrl: './run-history.html',
})
export class RunHistory {
  protected readonly dates = inject(DateFormatStore);
  readonly runs = input.required<RunSummaryDto[]>();
  /** The run shown in the dashboard. */
  readonly selected = input<string | null>(null);
  readonly select = output<string>();
  /** Asks to delete one or more runs, by id. */
  readonly remove = output<string[]>();
  /** Asks to delete every run of the plan. */
  readonly clear = output<void>();

  protected readonly faTrash = faTrash;

  protected readonly formatMs = formatMs;
  protected readonly formatElapsed = formatElapsed;

  /** The checked runs; ids of runs no longer listed are ignored (see `chosen`). */
  private readonly checked = signal<ReadonlySet<string>>(new Set());
  /** The run last checked or unchecked, where a shift-click range starts. */
  private anchor: string | null = null;

  /** The checked runs that are still listed, in list order. */
  protected readonly chosen = computed(() => {
    const checked = this.checked();
    return this.runs()
      .map((run) => run.runId)
      .filter((id) => checked.has(id));
  });
  protected readonly allChosen = computed(
    () => this.runs().length > 0 && this.chosen().length === this.runs().length,
  );

  protected readonly rows = computed(() => {
    const runs = this.runs();
    const checked = this.checked();
    const scale = Math.max(1, ...runs.map((run) => run.p95));
    return runs.map((run) => ({
      run,
      checked: checked.has(run.runId),
      bar: (run.p95 / scale) * 100,
      ...this.verdict(run),
    }));
  });

  /** Checks or unchecks a run; with shift, every run between it and the last one toggled too. */
  protected toggle(runId: string, event: MouseEvent): void {
    const ids = this.runs().map((run) => run.runId);
    const on = !this.checked().has(runId);
    const from = event.shiftKey && this.anchor ? ids.indexOf(this.anchor) : -1;
    const to = ids.indexOf(runId);
    const range = from >= 0 ? ids.slice(Math.min(from, to), Math.max(from, to) + 1) : [runId];
    this.checked.update((checked) => {
      const next = new Set(checked);
      for (const id of range) {
        if (on) next.add(id);
        else next.delete(id);
      }
      return next;
    });
    this.anchor = runId;
  }

  /** Checks every run, or none when they all are. */
  protected toggleAll(): void {
    this.checked.set(new Set(this.allChosen() ? [] : this.runs().map((run) => run.runId)));
    this.anchor = null;
  }

  protected deselect(): void {
    this.checked.set(new Set());
    this.anchor = null;
  }

  protected removeChosen(): void {
    const chosen = this.chosen();
    if (chosen.length) this.remove.emit(chosen);
  }

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
