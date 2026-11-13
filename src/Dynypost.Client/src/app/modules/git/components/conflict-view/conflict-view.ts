import { Component, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faCheck,
  faCodeMerge,
  faSpinner,
  faTriangleExclamation,
  faXmark,
} from '@fortawesome/free-solid-svg-icons';
import { ConflictResolution } from '../../../../models/messages';
import { GitStore } from '../../../../services/git-store';

/**
 * Shows both sides of a conflicted file and lets the user decide: keep one side, or keep the
 * file as they edited it. Dynypost never picks a side itself.
 */
@Component({
  selector: 'app-conflict-view',
  imports: [FaIconComponent],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  templateUrl: './conflict-view.html',
})
export class ConflictView {
  protected readonly git = inject(GitStore);
  protected readonly faCheck = faCheck;
  protected readonly faCodeMerge = faCodeMerge;
  protected readonly faSpinner = faSpinner;
  protected readonly faTriangleExclamation = faTriangleExclamation;
  protected readonly faXmark = faXmark;

  protected readonly sides: {
    resolution: 'ours' | 'theirs';
    title: string;
    subtitle: string;
    deleted: string;
  }[] = [
    {
      resolution: 'ours',
      title: 'Your version',
      subtitle: 'The current branch',
      deleted: 'Your branch deleted this file.',
    },
    {
      resolution: 'theirs',
      title: 'Incoming version',
      subtitle: 'The branch being merged in',
      deleted: 'The incoming branch deleted this file.',
    },
  ];

  /** Which resolution is being applied, to show its spinner. */
  private pending: ConflictResolution | null = null;

  protected busy(resolution: ConflictResolution): boolean {
    return this.git.busy() === 'resolve' && this.pending === resolution;
  }

  protected async resolve(path: string, resolution: ConflictResolution): Promise<void> {
    this.pending = resolution;
    try {
      await this.git.resolve(path, resolution);
    } finally {
      this.pending = null;
    }
  }
}
