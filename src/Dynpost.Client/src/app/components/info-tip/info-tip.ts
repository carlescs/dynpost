import { Component, input } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faCircleInfo } from '@fortawesome/free-solid-svg-icons';

/**
 * A small info icon that explains the setting next to it in a tooltip. It can be focused, and
 * screen readers read the explanation; clicking it inside a `<label>` doesn't change the field.
 */
@Component({
  selector: 'app-info-tip',
  imports: [FaIconComponent],
  templateUrl: './info-tip.html',
  host: {
    class:
      'inline-flex shrink-0 cursor-help items-center text-xs font-normal text-slate-400 normal-case hover:text-slate-600 focus:text-sky-600 focus:outline-none',
    tabindex: '0',
    role: 'img',
    '[attr.title]': 'text()',
    '[attr.aria-label]': 'text()',
    '(click)': '$event.preventDefault(); $event.stopPropagation()',
  },
})
export class InfoTip {
  /** The explanation shown in the tooltip. */
  readonly text = input.required<string>();
  protected readonly faCircleInfo = faCircleInfo;
}
