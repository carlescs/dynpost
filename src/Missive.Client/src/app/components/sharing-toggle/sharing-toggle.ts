import { Component, computed, input, output } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faLaptop } from '@fortawesome/free-solid-svg-icons';

/**
 * A row button that shows whether a file (an environment, a run plan) is kept on this computer
 * only, and switches it between local-only and shared through git. While the file is shared, the
 * button only appears when its row (a `group`) is hovered or focused.
 */
@Component({
  selector: 'app-sharing-toggle',
  imports: [FaIconComponent],
  host: { class: 'contents' },
  templateUrl: './sharing-toggle.html',
})
export class SharingToggle {
  /** Whether the file is local-only. */
  readonly localOnly = input.required<boolean>();
  /** The file's name, for the button's labels. */
  readonly name = input.required<string>();
  readonly toggled = output<void>();

  protected readonly faLaptop = faLaptop;
  protected readonly stateClass = computed(() =>
    this.localOnly()
      ? 'text-amber-600'
      : 'text-slate-400 opacity-0 group-focus-within:opacity-100 group-hover:opacity-100',
  );
  protected readonly title = computed(() =>
    this.localOnly()
      ? `${this.name()} is kept on this computer only (git-ignored). Click to share it through git.`
      : `Keep ${this.name()} on this computer only, out of git`,
  );
}
