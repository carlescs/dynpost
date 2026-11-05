import { Component, computed, input } from '@angular/core';
import { textChange } from './request-diff-model';

/**
 * Shows a value that may have changed: as-is when it didn't, otherwise the old value struck
 * through and the new one, with the part that differs highlighted.
 */
@Component({
  selector: 'app-text-change',
  host: { class: 'min-w-0' },
  template: `
    @let c = change();
    @if (before() === after()) {
      <span class="break-all whitespace-pre-wrap">{{ after() || placeholder() }}</span>
    } @else {
      <span class="flex min-w-0 gap-1" [class.flex-col]="stacked()" [class.flex-wrap]="!stacked()">
        @if (before()) {
          <del
            class="rounded-sm bg-rose-50 px-0.5 break-all whitespace-pre-wrap text-rose-800 decoration-rose-400"
            [attr.aria-label]="'Before: ' + before()"
            >{{ c.prefix }}<span class="bg-rose-200">{{ c.removed }}</span
            >{{ c.suffix }}</del
          >
        }
        @if (after()) {
          <ins
            class="rounded-sm bg-emerald-50 px-0.5 break-all whitespace-pre-wrap text-emerald-800 no-underline"
            [attr.aria-label]="'After: ' + after()"
            >{{ c.prefix }}<span class="bg-emerald-200">{{ c.added }}</span
            >{{ c.suffix }}</ins
          >
        } @else {
          <span class="text-xs text-slate-400 italic">{{ placeholder() || 'removed' }}</span>
        }
      </span>
    }
  `,
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
