import { Component, computed, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faCircleInfo,
  faCodeCompare,
  faFileCode,
  faFileLines,
  faSpinner,
  faXmark,
} from '@fortawesome/free-solid-svg-icons';
import { GitFileDiff, GitFileStatus } from '../../../../models/messages';
import { CodeDiffEditor } from '../../../../components/code-diff-editor/code-diff-editor';
import { fromRequestDto } from '../../../../models/request-mapping';
import { languageForPath } from '../../models/git-format';
import { GitStore } from '../../../../services/git-store';
import { RequestDiff } from '../request-diff/request-diff';

type DiffMode = 'request' | 'text';

const STATUS_LABELS: Record<GitFileStatus, string> = {
  modified: 'Modified',
  added: 'Added',
  untracked: 'New file',
  renamed: 'Renamed',
  deleted: 'Deleted',
  conflicted: 'Conflicted',
};

/**
 * What changed in a file since the last commit. Request files can be compared as requests, laid
 * out like the request editor, or as text; other files as text.
 */
@Component({
  selector: 'app-diff-view',
  imports: [FaIconComponent, CodeDiffEditor, RequestDiff],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    @if (git.diff(); as diff) {
      <header class="flex shrink-0 items-center gap-3 border-b border-slate-200 px-4 py-2">
        <fa-icon class="text-slate-500" [icon]="faCodeCompare" />
        <div class="min-w-0 flex-1">
          <h2 class="truncate font-semibold" [title]="diff.path">
            {{ diff.path }}
            <span class="ml-1 text-xs font-normal text-slate-500">{{
              statusLabels[diff.status]
            }}</span>
            @if (git.diffLoading() === diff.path) {
              <fa-icon class="ml-1 text-xs text-slate-400" [icon]="faSpinner" animation="spin" />
            }
          </h2>
          <p class="truncate text-xs text-slate-500">
            @if (diff.originalPath) {
              Renamed from {{ diff.originalPath }}.
            }
            Changes since the last commit.
          </p>
        </div>

        @if (diff.isRequest && !diff.binary) {
          <div
            class="flex shrink-0 overflow-hidden rounded border border-slate-300 text-sm"
            role="radiogroup"
            aria-label="Show the changes as"
          >
            <button
              type="button"
              role="radio"
              class="flex items-center gap-1.5 px-2.5 py-1 disabled:cursor-not-allowed disabled:opacity-50"
              [class]="
                effectiveMode() === 'request' ? 'bg-sky-600 text-white' : 'hover:bg-slate-100'
              "
              [attr.aria-checked]="effectiveMode() === 'request'"
              [disabled]="!canShowRequest()"
              [title]="canShowRequest() ? 'Compare as a request' : (diff.requestError ?? '')"
              (click)="mode.set('request')"
            >
              <fa-icon [icon]="faFileCode" /> Request
            </button>
            <button
              type="button"
              role="radio"
              class="flex items-center gap-1.5 border-l border-slate-300 px-2.5 py-1"
              [class]="effectiveMode() === 'text' ? 'bg-sky-600 text-white' : 'hover:bg-slate-100'"
              [attr.aria-checked]="effectiveMode() === 'text'"
              title="Compare the file's text"
              (click)="mode.set('text')"
            >
              <fa-icon [icon]="faFileLines" /> Text
            </button>
          </div>
        }

        @if (!diff.binary && (diff.original !== null || diff.modified !== null)) {
          <label class="flex shrink-0 items-center gap-1.5 text-sm text-slate-600">
            <input
              type="checkbox"
              class="accent-sky-600"
              [checked]="sideBySide()"
              (change)="sideBySide.set($any($event.target).checked)"
            />
            Side by side
          </label>
        }

        <button
          type="button"
          class="flex size-7 shrink-0 items-center justify-center rounded text-slate-500 hover:bg-slate-100 hover:text-slate-800"
          title="Close"
          aria-label="Close"
          (click)="git.closeDiff()"
        >
          <fa-icon [icon]="faXmark" />
        </button>
      </header>

      @if (diff.isRequest && diff.requestError) {
        <p
          class="mx-4 mt-3 flex items-start gap-2 rounded border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800"
          role="status"
        >
          <fa-icon class="mt-0.5" [icon]="faCircleInfo" />
          <span class="min-w-0 flex-1 break-words">
            Showing the text: a version of this file can't be read as a request.
            {{ diff.requestError }}
          </span>
        </p>
      }

      @if (diff.binary) {
        <p class="p-4 text-sm text-slate-500">
          This is a binary file; its differences are not shown.
        </p>
      } @else if (effectiveMode() === 'request') {
        <app-request-diff
          [before]="requests().before"
          [after]="requests().after"
          [sideBySide]="sideBySide()"
        />
      } @else {
        <app-code-diff-editor
          class="m-4 min-h-0 flex-1 overflow-hidden rounded border border-slate-300"
          [ariaLabel]="'Changes in ' + diff.path"
          [original]="diff.original ?? ''"
          [modified]="diff.modified ?? ''"
          [language]="language()"
          [sideBySide]="sideBySide()"
          [hideUnchanged]="true"
        />
      }
    }
  `,
})
export class DiffView {
  protected readonly git = inject(GitStore);
  protected readonly statusLabels = STATUS_LABELS;
  protected readonly faCodeCompare = faCodeCompare;
  protected readonly faCircleInfo = faCircleInfo;
  protected readonly faFileCode = faFileCode;
  protected readonly faFileLines = faFileLines;
  protected readonly faSpinner = faSpinner;
  protected readonly faXmark = faXmark;

  /** The mode the user picked; kept while moving between files. */
  protected readonly mode = signal<DiffMode>('request');
  protected readonly sideBySide = signal(true);

  /** Whether every existing side of the request file could be parsed. */
  protected readonly canShowRequest = computed(() => {
    const diff = this.git.diff();
    return !!diff && diff.isRequest && !diff.binary && !diff.requestError && hasRequest(diff);
  });

  protected readonly effectiveMode = computed<DiffMode>(() =>
    this.canShowRequest() ? this.mode() : 'text',
  );

  protected readonly requests = computed(() => {
    const diff = this.git.diff();
    return {
      before: diff?.originalRequest ? fromRequestDto(diff.originalRequest) : null,
      after: diff?.modifiedRequest ? fromRequestDto(diff.modifiedRequest) : null,
    };
  });

  protected readonly language = computed(() => languageForPath(this.git.diff()?.path ?? ''));
}

function hasRequest(diff: GitFileDiff): boolean {
  return !!(diff.originalRequest || diff.modifiedRequest);
}
