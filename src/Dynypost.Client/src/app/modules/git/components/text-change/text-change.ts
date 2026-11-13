import { Component, computed, input } from '@angular/core';
import { textChange } from '../../models/request-diff-model';

/**
 * Shows a value that may have changed: as-is when it didn't, otherwise the old value struck
 * through and the new one, with the part that differs highlighted.
 */
@Component({
  selector: 'app-text-change',
  host: { class: 'min-w-0' },
  templateUrl: './text-change.html',
})
export class TextChange {
  readonly before = input.required<string>();
  readonly after = input.required<string>();
  /** Shows the old and new values on separate lines. */
  readonly stacked = input(false);
  /** Shown for an empty value. */
  readonly placeholder = input('');

  protected readonly change = computed(() => textChange(this.before(), this.after()));
}
