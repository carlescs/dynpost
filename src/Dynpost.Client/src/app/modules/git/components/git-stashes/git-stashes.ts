import { Component, computed, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faBoxArchive,
  faBoxOpen,
  faChevronDown,
  faChevronRight,
  faCircleExclamation,
  faFileImport,
  faPlus,
  faSpinner,
  faTrash,
  faXmark,
} from '@fortawesome/free-solid-svg-icons';
import { GitStash } from '../../../../models/messages';
import { GitStore } from '../../../../services/git-store';
import { Dialogs } from '../../../../services/dialogs';
import { DateFormatStore } from '../../../../services/date-format-store';

/**
 * The repository's stash, in a collapsible section of the Source control view: set the
 * workspace's uncommitted changes aside, bring an entry back (apply keeps it, pop drops it), or
 * drop one.
 */
@Component({
  selector: 'app-git-stashes',
  imports: [FaIconComponent],
  host: { class: 'block shrink-0 border-t border-slate-200' },
  templateUrl: './git-stashes.html',
})
export class GitStashes {
  protected readonly dates = inject(DateFormatStore);
  protected readonly git = inject(GitStore);
  private readonly dialogs = inject(Dialogs);
  protected readonly expanded = signal(false);
  /** The new stash's message while the form is open; `null` when it is closed. */
  protected readonly form = signal<string | null>(null);
  /** The entry an operation is running for, to show its spinner. */
  protected readonly pending = signal<string | null>(null);

  /** Why the changes can't be stashed right now, or `null` when they can. */
  protected readonly stashBlocker = computed(() => {
    const status = this.git.status();
    if (!status) return 'The git status is not loaded yet';
    if (status.unborn) return 'Commit first: there is no commit to go back to yet';
    if (status.merging) return 'Complete or abort the merge in progress first';
    if (this.git.conflicts().length) return 'Resolve the conflicted files first';
    if (!status.changes.length) return 'There are no changes to stash';
    return null;
  });

  /** Why no entry can be applied right now, or `null` when it can. */
  protected readonly applyBlocker = computed(() => {
    const status = this.git.status();
    if (!status) return 'The git status is not loaded yet';
    if (status.merging) return 'Complete or abort the merge in progress first';
    if (this.git.conflicts().length) return 'Resolve the conflicted files first';
    if (status.changes.length) return 'Commit, stash or discard your changes first';
    return null;
  });

  protected readonly faBoxArchive = faBoxArchive;
  protected readonly faBoxOpen = faBoxOpen;
  protected readonly faChevronDown = faChevronDown;
  protected readonly faChevronRight = faChevronRight;
  protected readonly faCircleExclamation = faCircleExclamation;
  protected readonly faFileImport = faFileImport;
  protected readonly faPlus = faPlus;
  protected readonly faSpinner = faSpinner;
  protected readonly faTrash = faTrash;
  protected readonly faXmark = faXmark;

  protected toggle(): void {
    this.expanded.update((open) => !open);
    if (this.expanded()) void this.git.loadStashes();
  }

  protected startStash(): void {
    if (!this.expanded()) this.toggle();
    this.form.set('');
  }

  protected cancel(): void {
    this.form.set(null);
    if (this.git.failedOperation() === 'stash') this.git.dismissError();
  }

  protected async stash(): Promise<void> {
    const message = this.form();
    if (message === null) return;
    if (await this.git.stash(message)) this.form.set(null);
  }

  protected applyTitle(stash: GitStash, pop: boolean): string {
    return (
      this.applyBlocker() ??
      (pop
        ? `Pop: bring back "${stash.message}" and drop it`
        : `Apply: bring back "${stash.message}" and keep it`)
    );
  }

  protected async apply(stash: GitStash, pop: boolean): Promise<void> {
    await this.track(stash.id, () => this.git.applyStash(stash, pop));
  }

  protected async drop(stash: GitStash): Promise<void> {
    const ok = await this.dialogs.confirm(
      `Drop the stash "${stash.message}"? Its changes are lost.`,
      { title: 'Drop stash?', confirmLabel: 'Drop', danger: true },
    );
    if (!ok) return;
    await this.track(stash.id, () => this.git.dropStash(stash));
  }

  private async track(id: string, action: () => Promise<boolean>): Promise<void> {
    this.pending.set(id);
    try {
      await action();
    } finally {
      this.pending.set(null);
    }
  }
}
