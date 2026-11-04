import { Component, ElementRef, afterNextRender, input, output, viewChild } from '@angular/core';

/**
 * Inline text box for naming a tree entry. Enter commits, Escape cancels, and leaving the box
 * commits a changed name (or cancels an unchanged or empty one).
 */
@Component({
  selector: 'app-name-input',
  host: { class: 'flex min-w-0 flex-1' },
  template: `
    <input
      #box
      class="min-w-0 flex-1 rounded-sm border border-sky-500 bg-white px-1 text-sm focus:outline-none disabled:opacity-60"
      spellcheck="false"
      [attr.aria-label]="label()"
      [value]="initial()"
      [disabled]="busy()"
      (keydown.enter)="$event.preventDefault(); submit(box.value, false)"
      (keydown.escape)="$event.preventDefault(); cancelled.emit()"
      (blur)="submit(box.value, true)"
      (click)="$event.stopPropagation()"
    />
  `,
})
export class NameInput {
  readonly initial = input('');
  readonly label = input.required<string>();
  readonly busy = input(false);
  /** The entered name; `viaBlur` is true when the user left the box rather than pressing Enter. */
  readonly committed = output<{ name: string; viaBlur: boolean }>();
  readonly cancelled = output();

  private readonly box = viewChild.required<ElementRef<HTMLInputElement>>('box');

  constructor() {
    afterNextRender(() => {
      const box = this.box().nativeElement;
      box.focus();
      box.select();
    });
  }

  protected submit(value: string, viaBlur: boolean): void {
    if (this.busy()) return;
    const name = value.trim();
    if (!name || (viaBlur && name === this.initial())) {
      this.cancelled.emit();
    } else {
      this.committed.emit({ name, viaBlur });
    }
  }
}
