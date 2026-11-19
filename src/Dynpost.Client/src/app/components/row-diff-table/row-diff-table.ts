import { Component, input } from '@angular/core';
import { RowChange } from '../../models/request-diff-model';
import { TextChange } from '../text-change/text-change';

const ROW_STYLES: Record<RowChange['kind'], { marker: string; row: string; label: string }> = {
  same: { marker: '', row: '', label: 'Unchanged' },
  added: { marker: '+', row: 'bg-emerald-50 text-emerald-900', label: 'Added' },
  removed: { marker: '−', row: 'bg-rose-50 text-rose-900 line-through', label: 'Removed' },
  changed: { marker: '~', row: 'bg-amber-50', label: 'Changed' },
};

/**
 * A table of compared rows (params, headers, settings, ...): added, removed and changed rows are
 * marked, and a changed row shows its old and new key and value.
 */
@Component({
  selector: 'app-row-diff-table',
  imports: [TextChange],
  templateUrl: './row-diff-table.html',
})
export class RowDiffTable {
  readonly rows = input.required<RowChange[]>();
  /** The heading of the key column. */
  readonly label = input.required<string>();
  /** The heading of the value column. */
  readonly valueLabel = input('Value');
  /** Whether the rows can be turned off, so their enabled state is shown. */
  readonly toggles = input(true);

  protected rowStyle(change: RowChange): (typeof ROW_STYLES)[RowChange['kind']] {
    return ROW_STYLES[change.kind];
  }
}
