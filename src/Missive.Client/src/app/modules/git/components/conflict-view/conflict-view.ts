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
 * file as they edited it. Missive never picks a side itself.
 */
@Component({
  selector: 'app-conflict-view',
  imports: [FaIconComponent],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    @if (git.conflict(); as conflict) {
      <header class="flex shrink-0 items-start gap-3 border-b border-slate-200 px-4 py-3">
        <fa-icon class="mt-1 text-amber-600" [icon]="faCodeMerge" />
        <div class="min-w-0 flex-1">
          <h2 class="truncate font-semibold">Merge conflict in {{ conflict.path }}</h2>
          <p class="text-sm text-slate-500">
            Both branches changed this file. Choose the version to keep, or edit the file yourself
            and mark it as resolved.
          </p>
        </div>
        <button
          type="button"
          class="flex size-7 shrink-0 items-center justify-center rounded text-slate-500 hover:bg-slate-100 hover:text-slate-800"
          title="Close"
          aria-label="Close"
          (click)="git.closeConflict()"
        >
          <fa-icon [icon]="faXmark" />
        </button>
      </header>

      @if (git.error(); as error) {
        <div
          class="mx-4 mt-3 flex items-start gap-2 rounded border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-800"
          role="alert"
        >
          <fa-icon class="mt-0.5" [icon]="faTriangleExclamation" />
          <p class="min-w-0 flex-1 break-words">{{ error.message }}</p>
        </div>
      }

      <div class="grid min-h-0 flex-1 grid-cols-2 gap-3 overflow-hidden p-4">
        @for (side of sides; track side.resolution) {
          <section class="flex min-h-0 flex-col rounded border border-slate-200">
            <div
              class="flex shrink-0 items-center justify-between gap-2 border-b border-slate-200 bg-slate-50 px-3 py-2"
            >
              <div class="min-w-0">
                <h3 class="text-sm font-semibold">{{ side.title }}</h3>
                <p class="truncate text-xs text-slate-500">{{ side.subtitle }}</p>
              </div>
              <button
                type="button"
                class="flex shrink-0 items-center gap-1.5 rounded bg-sky-600 px-2.5 py-1 text-sm text-white enabled:hover:bg-sky-700 disabled:cursor-not-allowed disabled:opacity-50"
                [disabled]="git.busy() !== null"
                (click)="resolve(conflict.path, side.resolution)"
              >
                <fa-icon
                  [icon]="busy(side.resolution) ? faSpinner : faCheck"
                  [animation]="busy(side.resolution) ? 'spin' : undefined"
                />
                {{ conflict[side.resolution] === null ? 'Keep it deleted' : 'Keep this version' }}
              </button>
            </div>
            @if (conflict[side.resolution]; as text) {
              <pre class="min-h-0 flex-1 overflow-auto p-3 font-mono text-xs leading-5">{{
                text
              }}</pre>
            } @else {
              <p class="p-3 text-sm text-slate-500 italic">{{ side.deleted }}</p>
            }
          </section>
        }
      </div>

      <footer class="shrink-0 space-y-2 border-t border-slate-200 px-4 py-3 text-sm">
        <details>
          <summary class="cursor-pointer text-slate-600">
            The file on disk
            @if (conflict.hasConflictMarkers) {
              <span class="text-amber-700">(still has conflict markers)</span>
            }
          </summary>
          @if (conflict.working !== null) {
            <pre
              class="mt-2 max-h-48 overflow-auto rounded border border-slate-200 bg-slate-50 p-3 font-mono text-xs leading-5"
              >{{ conflict.working }}</pre>
          } @else {
            <p class="mt-2 text-slate-500 italic">The file was deleted.</p>
          }
        </details>
        <div class="flex items-center gap-3">
          <button
            type="button"
            class="flex items-center gap-1.5 rounded border border-slate-300 bg-white px-2.5 py-1 enabled:hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-50"
            [disabled]="git.busy() !== null"
            (click)="resolve(conflict.path, 'edited')"
          >
            <fa-icon
              [icon]="busy('edited') ? faSpinner : faCheck"
              [animation]="busy('edited') ? 'spin' : undefined"
            />
            Mark as resolved
          </button>
          <span class="text-slate-500">
            After editing the file in your own editor, combining both sides and removing the
            <code>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</code> / <code>=======</code> /
            <code>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</code> markers.
          </span>
        </div>
      </footer>
    }
  `,
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
