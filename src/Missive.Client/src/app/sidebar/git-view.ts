import { Component, computed, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faArrowDown,
  faArrowUp,
  faCheck,
  faCircleExclamation,
  faCodeBranch,
  faFolderOpen,
  faRotate,
  faSpinner,
  faTriangleExclamation,
  faXmark,
} from '@fortawesome/free-solid-svg-icons';
import { GitChange, GitFileStatus } from '../bridge/messages';
import { GitOperation, GitStore } from '../git/git-store';
import { WorkspaceStore } from '../workspace/workspace-store';

const STATUS_BADGES: Record<GitFileStatus, { letter: string; color: string }> = {
  modified: { letter: 'M', color: 'text-amber-600' },
  added: { letter: 'A', color: 'text-emerald-600' },
  untracked: { letter: 'U', color: 'text-emerald-600' },
  renamed: { letter: 'R', color: 'text-sky-600' },
  deleted: { letter: 'D', color: 'text-rose-600' },
  conflicted: { letter: '!', color: 'text-rose-700' },
};

@Component({
  selector: 'app-git-view',
  imports: [FaIconComponent],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <header class="flex h-9 shrink-0 items-center justify-between px-3">
      <h2 class="text-xs font-semibold tracking-wide text-slate-500 uppercase">Source control</h2>
      @if (workspace.workspacePath()) {
        <button
          type="button"
          class="flex size-6 items-center justify-center rounded text-slate-500 enabled:hover:bg-slate-200 enabled:hover:text-slate-800 disabled:opacity-50"
          title="Refresh"
          aria-label="Refresh"
          [disabled]="git.busy() !== null"
          (click)="git.refresh()"
        >
          <fa-icon [icon]="faRotate" [animation]="git.busy() === 'refresh' ? 'spin' : undefined" />
        </button>
      }
    </header>

    @if (!git.available) {
      <p class="px-3 text-sm text-slate-500">
        Source control is available in the Missive desktop app.
      </p>
    } @else if (workspace.restoring()) {
      <p class="px-3 text-sm text-slate-500">
        <fa-icon [icon]="faSpinner" animation="spin" /> Opening the last workspace…
      </p>
    } @else if (!workspace.workspacePath()) {
      <div class="space-y-2 px-3 text-sm text-slate-500">
        <p>Open a workspace folder to see its changes and sync it with git.</p>
        <button
          type="button"
          class="flex items-center justify-center gap-2 rounded px-3 py-1 text-sm disabled:cursor-not-allowed disabled:opacity-50 w-full bg-sky-600 text-white enabled:hover:bg-sky-700"
          (click)="workspace.openWorkspace()"
        >
          <fa-icon [icon]="faFolderOpen" /> Open folder
        </button>
        @if (workspace.openError(); as error) {
          <p class="text-rose-700" role="alert">{{ error.message }}</p>
        }
      </div>
    } @else {
      @if (git.error(); as error) {
        <div
          class="mx-3 mb-2 flex items-start gap-2 rounded border border-rose-200 bg-rose-50 px-2 py-1.5 text-xs text-rose-800"
          role="alert"
        >
          <fa-icon class="mt-0.5" [icon]="faCircleExclamation" />
          <p class="min-w-0 flex-1 break-words whitespace-pre-line">{{ error.message }}</p>
          <button
            type="button"
            class="shrink-0 text-rose-500 hover:text-rose-800"
            title="Dismiss"
            aria-label="Dismiss"
            (click)="git.dismissError()"
          >
            <fa-icon [icon]="faXmark" />
          </button>
        </div>
      }

      @if (git.status(); as status) {
        <div class="flex items-center gap-2 px-3 pb-2 text-sm text-slate-700">
          <fa-icon class="text-slate-500" [icon]="faCodeBranch" />
          <span class="truncate font-medium" [title]="status.upstream ?? 'No upstream branch'">{{
            status.branch ?? 'Detached HEAD'
          }}</span>
          @if (status.upstream) {
            <span class="ml-auto flex shrink-0 gap-2 text-xs text-slate-500">
              <span title="Commits to pull"
                ><fa-icon [icon]="faArrowDown" /> {{ status.behind }}</span
              >
              <span title="Commits to push"><fa-icon [icon]="faArrowUp" /> {{ status.ahead }}</span>
            </span>
          } @else {
            <span class="ml-auto shrink-0 text-xs text-slate-400">Not published</span>
          }
        </div>

        @if (status.merging) {
          <div
            class="mx-3 mb-2 flex items-start gap-2 rounded border border-amber-200 bg-amber-50 px-2 py-1.5 text-xs text-amber-800"
          >
            <fa-icon class="mt-0.5" [icon]="faTriangleExclamation" />
            <p>
              @if (git.conflicts().length) {
                Merge in progress. Resolve the conflicted files and mark them as resolved (git add),
                then commit.
              } @else {
                Merge in progress. Commit to complete it.
              }
            </p>
          </div>
        }

        <div class="space-y-2 px-3 pb-3">
          <textarea
            class="block w-full resize-none rounded border border-slate-300 bg-white px-2 py-1 text-sm focus:border-sky-500 focus:outline-none"
            rows="3"
            placeholder="Commit message (Ctrl+Enter to commit)"
            aria-label="Commit message"
            [value]="message()"
            (input)="message.set($any($event.target).value)"
            (keydown.control.enter)="commit()"
            (keydown.meta.enter)="commit()"
          ></textarea>
          <button
            type="button"
            class="flex items-center justify-center gap-2 rounded px-3 py-1 text-sm disabled:cursor-not-allowed disabled:opacity-50 w-full bg-sky-600 text-white enabled:hover:bg-sky-700"
            [disabled]="!canCommit()"
            (click)="commit()"
          >
            <fa-icon [icon]="busyIcon('commit', faCheck)" [animation]="spin('commit')" />
            {{ selected().size ? 'Commit selected (' + selected().size + ')' : 'Commit all' }}
          </button>
          <div class="flex gap-2">
            <button
              type="button"
              class="flex items-center justify-center gap-2 rounded px-3 py-1 text-sm disabled:cursor-not-allowed disabled:opacity-50 flex-1 border border-slate-300 bg-white enabled:hover:bg-slate-100"
              [disabled]="git.busy() !== null || !status.upstream"
              [title]="
                status.upstream ? 'Pull from ' + status.upstream : 'The branch has no upstream yet'
              "
              (click)="git.pull()"
            >
              <fa-icon [icon]="busyIcon('pull', faArrowDown)" [animation]="spin('pull')" /> Pull
            </button>
            <button
              type="button"
              class="flex items-center justify-center gap-2 rounded px-3 py-1 text-sm disabled:cursor-not-allowed disabled:opacity-50 flex-1 border border-slate-300 bg-white enabled:hover:bg-slate-100"
              [disabled]="git.busy() !== null || status.branch === undefined"
              [title]="status.upstream ? 'Push to ' + status.upstream : 'Publish the branch'"
              (click)="git.push()"
            >
              <fa-icon [icon]="busyIcon('push', faArrowUp)" [animation]="spin('push')" />
              {{ status.upstream ? 'Push' : 'Publish' }}
            </button>
          </div>
          @if (git.notice(); as notice) {
            <p
              class="text-xs"
              [class]="notice.kind === 'warning' ? 'text-amber-700' : 'text-slate-500'"
              role="status"
            >
              {{ notice.text }}
            </p>
          }
        </div>

        <h3 class="px-3 pb-1 text-xs font-semibold tracking-wide text-slate-500 uppercase">
          Changes ({{ status.changes.length }})
        </h3>
        <ul class="min-h-0 flex-1 overflow-y-auto pb-2 text-sm">
          @for (change of status.changes; track change.path) {
            <li
              class="flex items-center gap-2 px-3 py-0.5 hover:bg-slate-200"
              [title]="describe(change)"
            >
              <input
                type="checkbox"
                class="shrink-0 accent-sky-600"
                [attr.aria-label]="'Select ' + change.path"
                [checked]="selected().has(change.path)"
                [disabled]="status.merging || change.status === 'conflicted'"
                (change)="toggle(change.path)"
              />
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
      } @else if (git.busy() === 'refresh') {
        <p class="px-3 text-sm text-slate-500">
          <fa-icon [icon]="faSpinner" animation="spin" /> Reading git status…
        </p>
      } @else if (git.error()) {
        <div class="px-3">
          <button
            type="button"
            class="flex items-center justify-center gap-2 rounded px-3 py-1 text-sm disabled:cursor-not-allowed disabled:opacity-50 flex-1 border border-slate-300 bg-white enabled:hover:bg-slate-100 w-full"
            (click)="git.refresh()"
          >
            <fa-icon [icon]="faRotate" /> Retry
          </button>
        </div>
      }
    }
  `,
})
export class GitView {
  protected readonly git = inject(GitStore);
  protected readonly workspace = inject(WorkspaceStore);
  protected readonly badges = STATUS_BADGES;
  protected readonly message = signal('');
  /** Paths picked for a partial commit; empty commits everything. */
  protected readonly selected = signal(new Set<string>());

  /** Selected paths that still have changes. */
  private readonly selectedPaths = computed(() => {
    const changed = new Set(this.git.status()?.changes.map((c) => c.path));
    return [...this.selected()].filter((p) => changed.has(p));
  });

  protected readonly canCommit = computed(
    () =>
      this.git.busy() === null &&
      this.message().trim().length > 0 &&
      this.git.changeCount() > 0 &&
      this.git.conflicts().length === 0,
  );

  protected readonly faCodeBranch = faCodeBranch;
  protected readonly faArrowDown = faArrowDown;
  protected readonly faArrowUp = faArrowUp;
  protected readonly faCheck = faCheck;
  protected readonly faRotate = faRotate;
  protected readonly faSpinner = faSpinner;
  protected readonly faFolderOpen = faFolderOpen;
  protected readonly faXmark = faXmark;
  protected readonly faCircleExclamation = faCircleExclamation;
  protected readonly faTriangleExclamation = faTriangleExclamation;

  protected async commit(): Promise<void> {
    if (!this.canCommit()) return;
    if (await this.git.commit(this.message(), this.selectedPaths())) {
      this.message.set('');
      this.selected.set(new Set());
    }
  }

  protected toggle(path: string): void {
    this.selected.update((set) => {
      const next = new Set(set);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  }

  protected busyIcon(operation: GitOperation, icon: typeof faCheck): typeof faCheck {
    return this.git.busy() === operation ? faSpinner : icon;
  }

  protected spin(operation: GitOperation): 'spin' | undefined {
    return this.git.busy() === operation ? 'spin' : undefined;
  }

  protected describe(change: GitChange): string {
    return change.originalPath ? `${change.originalPath} → ${change.path}` : change.path;
  }

  protected fileName(path: string): string {
    return path.slice(path.lastIndexOf('/') + 1);
  }

  protected folder(path: string): string {
    const index = path.lastIndexOf('/');
    return index < 0 ? '' : path.slice(0, index);
  }
}
