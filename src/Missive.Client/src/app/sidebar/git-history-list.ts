import { Component, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faCircleExclamation, faRotate, faSpinner } from '@fortawesome/free-solid-svg-icons';
import { relativeTime } from '../git/git-format';
import { GitHistoryStore } from '../git/git-history-store';

/** The commits that changed the workspace, newest first; choosing one opens it for review. */
@Component({
  selector: 'app-git-history',
  imports: [FaIconComponent],
  host: { class: 'flex flex-col' },
  template: `
    @if (history.error(); as error) {
      <div
        class="mx-3 mt-2 flex items-start gap-2 rounded border border-rose-200 bg-rose-50 px-2 py-1.5 text-xs text-rose-800"
        role="alert"
      >
        <fa-icon class="mt-0.5" [icon]="faCircleExclamation" />
        <p class="min-w-0 flex-1 break-words whitespace-pre-line">{{ error.message }}</p>
        <button
          type="button"
          class="shrink-0 text-rose-500 hover:text-rose-800"
          title="Retry"
          aria-label="Retry"
          (click)="history.load()"
        >
          <fa-icon [icon]="faRotate" />
        </button>
      </div>
    }

    @if (history.commits(); as commits) {
      <ul class="min-h-0 flex-1 overflow-y-auto py-1 text-sm" aria-label="Commits">
        @for (commit of commits; track commit.id) {
          <li>
            <button
              type="button"
              class="block w-full px-3 py-1 text-left hover:bg-slate-200"
              [class.bg-sky-100]="isSelected(commit.id)"
              [attr.aria-current]="isSelected(commit.id) ? 'true' : null"
              [title]="commit.subject + '\\n' + commit.authorName + ' · ' + commit.id.slice(0, 7)"
              (click)="history.open(commit.id)"
            >
              <span class="block truncate">{{ commit.subject }}</span>
              <span class="flex gap-2 text-xs text-slate-500">
                <span class="min-w-0 truncate">{{ commit.authorName }}</span>
                <span class="shrink-0">{{ relativeTime(commit.date) }}</span>
                <span class="ml-auto shrink-0 font-mono text-slate-400">{{
                  commit.id.slice(0, 7)
                }}</span>
              </span>
            </button>
          </li>
        } @empty {
          <li class="px-3 py-1 text-slate-500">No commits yet.</li>
        }
        @if (history.hasMore()) {
          <li class="px-3 py-1">
            <button
              type="button"
              class="flex w-full items-center justify-center gap-2 rounded border border-slate-300 bg-white px-3 py-1 text-sm enabled:hover:bg-slate-100 disabled:opacity-50"
              [disabled]="history.loading()"
              (click)="history.loadMore()"
            >
              @if (history.loading()) {
                <fa-icon [icon]="faSpinner" animation="spin" />
              }
              Load more
            </button>
          </li>
        }
      </ul>
    } @else if (history.loading()) {
      <p class="px-3 py-2 text-sm text-slate-500">
        <fa-icon [icon]="faSpinner" animation="spin" /> Reading history…
      </p>
    }
  `,
})
export class GitHistoryList {
  protected readonly history = inject(GitHistoryStore);
  protected readonly relativeTime = relativeTime;
  protected readonly faCircleExclamation = faCircleExclamation;
  protected readonly faRotate = faRotate;
  protected readonly faSpinner = faSpinner;

  protected isSelected(id: string): boolean {
    return (this.history.commit()?.commit.id ?? this.history.opening()) === id;
  }
}
