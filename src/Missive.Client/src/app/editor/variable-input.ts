import { Component, ElementRef, computed, input, output, viewChild } from '@angular/core';
import { VariableLookup, describeVariable, splitVariables, variableClass } from './variables';

/**
 * A single-line text input that highlights `{{variable}}` references. The input's own text is
 * transparent; a layer behind it draws the same text with the references highlighted, and
 * follows the input's horizontal scroll.
 */
@Component({
  selector: 'app-variable-input',
  host: { class: 'relative block min-w-0' },
  template: `
    <div
      #backdrop
      class="pointer-events-none absolute inset-0 overflow-hidden whitespace-pre text-slate-800"
      [class]="fieldClass()"
      aria-hidden="true"
    >
      @for (segment of segments(); track $index) {
        @if (segment.variable) {
          <span [class]="classOf(segment.variable)" [attr.data-variable]="segment.variable">{{
            segment.text
          }}</span>
        } @else {
          <span>{{ segment.text }}</span>
        }
      }
    </div>
    <input
      #field
      class="relative w-full bg-transparent text-transparent caret-slate-800 placeholder:text-slate-400 focus:outline-none"
      [class]="fieldClass()"
      [placeholder]="placeholder()"
      [attr.aria-label]="ariaLabel()"
      [title]="hover()"
      spellcheck="false"
      [value]="value()"
      (input)="changed($event)"
      (scroll)="syncScroll()"
      (keyup)="syncScroll()"
      (click)="syncScroll()"
      (select)="syncScroll()"
    />
  `,
})
export class VariableInput {
  readonly value = input.required<string>();
  readonly placeholder = input('');
  readonly ariaLabel = input<string | null>(null);
  /** Padding and font classes, applied to both the input and the highlight layer. */
  readonly fieldClass = input('px-3 py-1.5 font-mono text-sm');
  /** The defined variables; references to others are marked. `null` marks none. */
  readonly variables = input<VariableLookup | null>(null);

  readonly valueChange = output<string>();

  protected readonly segments = computed(() => splitVariables(this.value()));
  /** Describes the references, since the highlight layer can't show hovers of its own. */
  protected readonly hover = computed(() => {
    const names = [...new Set(this.segments().flatMap((s) => (s.variable ? [s.variable] : [])))];
    return names.map((name) => describeVariable(name, this.variables())).join('\n');
  });
  private readonly backdrop = viewChild.required<ElementRef<HTMLElement>>('backdrop');
  private readonly field = viewChild.required<ElementRef<HTMLInputElement>>('field');

  protected classOf(name: string): string {
    return variableClass(name, this.variables());
  }

  protected changed(event: Event): void {
    this.valueChange.emit((event.target as HTMLInputElement).value);
    this.syncScroll();
  }

  protected syncScroll(): void {
    // After the browser has moved the caret (and scrolled the input) for this event.
    requestAnimationFrame(() => {
      this.backdrop().nativeElement.scrollLeft = this.field().nativeElement.scrollLeft;
    });
  }
}
