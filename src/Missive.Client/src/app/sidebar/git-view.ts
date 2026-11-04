import { Component, computed, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faArrowDown, faArrowUp, faCheck, faCodeBranch } from '@fortawesome/free-solid-svg-icons';
import { GitFileStatus } from '../workspace/models';
import { WorkspaceStore } from '../workspace/workspace-store';

const STATUS_BADGES: Record<GitFileStatus, { letter: string; color: string }> = {
  modified: { letter: 'M', color: 'text-amber-600' },
  added: { letter: 'A', color: 'text-emerald-600' },
  untracked: { letter: 'U', color: 'text-emerald-600' },
  deleted: { letter: 'D', color: 'text-rose-600' },
  conflicted: { letter: '!', color: 'text-rose-700' },
};

@Component({
  selector: 'app-git-view',
  imports: [FaIconComponent],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <header class="flex h-9 shrink-0 items-center px-3">
      <h2 class="text-xs font-semibold tracking-wide text-slate-500 uppercase">Source control</h2>
    </header>

    <div class="flex items-center gap-2 px-3 pb-2 text-sm text-slate-700">
      <fa-icon class="text-slate-500" [icon]="faCodeBranch" />
      <span class="truncate font-medium">{{ git().branch }}</span>
      <span class="ml-auto flex shrink-0 gap-2 text-xs text-slate-500">
        <span title="Commits to pull"><fa-icon [icon]="faArrowDown" /> {{ git().behind }}</span>
        <span title="Commits to push"><fa-icon [icon]="faArrowUp" /> {{ git().ahead }}</span>
      </span>
    </div>

    <div class="space-y-2 px-3 pb-3">
      <textarea
        class="block w-full resize-none rounded border border-slate-300 bg-white px-2 py-1 text-sm focus:border-sky-500 focus:outline-none"
        rows="3"
        placeholder="Commit message"
        aria-label="Commit message"
        [value]="message()"
        (input)="message.set($any($event.target).value)"
      ></textarea>
      <button
        type="button"
        class="flex w-full items-center justify-center gap-2 rounded bg-sky-600 px-3 py-1 text-sm text-white hover:bg-sky-700 disabled:cursor-not-allowed disabled:opacity-50"
        [disabled]="!canCommit()"
      >
        <fa-icon [icon]="faCheck" /> Commit
      </button>
      <div class="flex gap-2">
        <button
          type="button"
          class="flex flex-1 items-center justify-center gap-2 rounded border border-slate-300 bg-white px-3 py-1 text-sm hover:bg-slate-100"
        >
          <fa-icon [icon]="faArrowDown" /> Pull
        </button>
        <button
          type="button"
          class="flex flex-1 items-center justify-center gap-2 rounded border border-slate-300 bg-white px-3 py-1 text-sm hover:bg-slate-100"
        >
          <fa-icon [icon]="faArrowUp" /> Push
        </button>
      </div>
    </div>

    <h3 class="px-3 pb-1 text-xs font-semibold tracking-wide text-slate-500 uppercase">
      Changes ({{ git().changes.length }})
    </h3>
    <ul class="min-h-0 flex-1 overflow-y-auto pb-2 text-sm">
      @for (change of git().changes; track change.path) {
        <li class="flex items-center gap-2 px-3 py-0.5 hover:bg-slate-200" [title]="change.path">
          <span class="truncate">{{ fileName(change.path) }}</span>
          <span class="truncate text-xs text-slate-400">{{ folder(change.path) }}</span>
          <span
            class="ml-auto font-mono text-xs font-bold"
            [class]="badges[change.status].color"
            [title]="change.status"
            >{{ badges[change.status].letter }}</span
          >
        </li>
      } @empty {
        <li class="px-3 py-1 text-slate-500">No changes.</li>
      }
    </ul>
  `,
})
export class GitView {
  private readonly store = inject(WorkspaceStore);
  protected readonly git = this.store.git;
  protected readonly badges = STATUS_BADGES;
  protected readonly message = signal('');
  protected readonly canCommit = computed(
    () => this.message().trim().length > 0 && this.git().changes.length > 0,
  );

  protected readonly faCodeBranch = faCodeBranch;
  protected readonly faArrowDown = faArrowDown;
  protected readonly faArrowUp = faArrowUp;
  protected readonly faCheck = faCheck;

  protected fileName(path: string): string {
    return path.slice(path.lastIndexOf('/') + 1);
  }

  protected folder(path: string): string {
    const index = path.lastIndexOf('/');
    return index < 0 ? '' : path.slice(0, index);
  }
}
