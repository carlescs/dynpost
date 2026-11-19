import { Component, computed, input } from '@angular/core';
import { PlanForm } from '../../models/run-plan-form';
import { planChanges } from '../../models/plan-diff-model';
import { RowDiffTable } from '../row-diff-table/row-diff-table';
import { TextChange } from '../text-change/text-change';

/**
 * Two run plans laid out like the plan form, read-only, with what differs marked: the name and,
 * section by section, the requests, environments, load, data, bodies, assertions and thresholds.
 */
@Component({
  selector: 'app-plan-diff',
  imports: [RowDiffTable, TextChange],
  host: { class: 'flex min-h-0 flex-1 flex-col overflow-y-auto' },
  templateUrl: './plan-diff.html',
})
export class PlanDiff {
  readonly before = input.required<PlanForm>();
  readonly after = input.required<PlanForm>();

  private readonly sections = computed(() => planChanges(this.before(), this.after()));
  /** The sections either plan sets. */
  protected readonly shown = computed(() => this.sections().filter((s) => s.rows.length));
  /** The sections neither plan sets, named in one line. */
  protected readonly unset = computed(() =>
    this.sections()
      .filter((s) => !s.rows.length)
      .map((s) => s.title)
      .join(', '),
  );
  protected readonly changeCount = computed(
    () =>
      this.sections().reduce((sum, s) => sum + s.changes, 0) +
      (this.before().name === this.after().name ? 0 : 1),
  );
}
