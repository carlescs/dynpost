import { Component, input, output } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faTrash } from '@fortawesome/free-solid-svg-icons';
import { KeyValue } from '../workspace/models';

/** Editable key/value list (query params, headers). Always shows one empty row for new entries. */
@Component({
  selector: 'app-key-value-table',
  imports: [FaIconComponent],
  template: `
    <table class="w-full table-fixed border-collapse text-sm">
      <thead>
        <tr class="text-left text-xs text-slate-500">
          <th class="w-8"></th>
          <th class="px-2 py-1 font-medium">{{ keyLabel() }}</th>
          <th class="px-2 py-1 font-medium">Value</th>
          <th class="w-8"></th>
        </tr>
      </thead>
      <tbody>
        @for (row of rowsWithBlank(); track $index; let i = $index; let last = $last) {
          <tr class="border-t border-slate-200">
            <td class="text-center">
              @if (!last) {
                <input
                  type="checkbox"
                  [checked]="row.enabled"
                  [attr.aria-label]="'Enable ' + row.key"
                  (change)="patch(i, { enabled: $any($event.target).checked })"
                />
              }
            </td>
            <td>
              <input
                class="w-full px-2 py-1 font-mono focus:bg-sky-50 focus:outline-none"
                [placeholder]="keyLabel()"
                [value]="row.key"
                (input)="patch(i, { key: $any($event.target).value })"
              />
            </td>
            <td>
              <input
                class="w-full px-2 py-1 font-mono focus:bg-sky-50 focus:outline-none"
                placeholder="Value"
                [value]="row.value"
                (input)="patch(i, { value: $any($event.target).value })"
              />
            </td>
            <td class="text-center">
              @if (!last) {
                <button
                  type="button"
                  class="text-slate-400 hover:text-rose-600"
                  aria-label="Remove row"
                  (click)="remove(i)"
                >
                  <fa-icon [icon]="faTrash" />
                </button>
              }
            </td>
          </tr>
        }
      </tbody>
    </table>
  `,
})
export class KeyValueTable {
  readonly rows = input.required<KeyValue[]>();
  readonly keyLabel = input('Key');
  readonly rowsChange = output<KeyValue[]>();

  protected readonly faTrash = faTrash;

  protected rowsWithBlank(): KeyValue[] {
    return [...this.rows(), { key: '', value: '', enabled: true }];
  }

  protected patch(index: number, patch: Partial<KeyValue>): void {
    const rows = [...this.rows()];
    const current = rows[index] ?? { key: '', value: '', enabled: true };
    rows[index] = { ...current, ...patch };
    this.rowsChange.emit(rows);
  }

  protected remove(index: number): void {
    this.rowsChange.emit(this.rows().filter((_, i) => i !== index));
  }
}
