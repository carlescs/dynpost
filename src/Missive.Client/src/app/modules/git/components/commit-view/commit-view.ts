import { Component, computed, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faCircleInfo,
  faCodeCommit,
  faFileCode,
  faFileLines,
  faSpinner,
  faTriangleExclamation,
  faXmark,
} from '@fortawesome/free-solid-svg-icons';
import { GitChange } from '../../../../models/messages';
import { CodeDiffEditor } from '../../../../components/code-diff-editor/code-diff-editor';
import { fromRequestDto } from '../../../../models/request-mapping';
import { GitHistoryStore } from '../../../../services/git-history-store';
import { STATUS_BADGES, fileName, folderOf, languageForPath } from '../../models/git-format';
import { RequestDiff } from '../request-diff/request-diff';

type DiffMode = 'request' | 'text';

/**
 * Reviews a commit: its message and author, the workspace files it changed, and what changed in
 * each file compared with the commit before it. Request files can be compared as requests, laid
 * out like the request editor, or as text; other files as text. Read-only.
 */
@Component({
  selector: 'app-commit-view',
  imports: [FaIconComponent, CodeDiffEditor, RequestDiff],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <header class="flex shrink-0 items-start gap-3 border-b border-slate-200 px-4 py-3">
      <fa-icon class="mt-1 text-slate-500" [icon]="faCodeCommit" />
      @if (history.commit(); as details) {
        <div class="min-w-0 flex-1">
          <h2 class="font-semibold break-words">{{ details.commit.subject }}</h2>
          <p class="text-sm text-slate-500">
            <span [title]="details.commit.authorEmail">{{ details.commit.authorName }}</span>
            committed
            <time [attr.datetime]="details.commit.date" [title]="details.commit.date">{{
              formatDate(details.commit.date)
            }}</time>
            ·
            <span class="font-mono" [title]="details.commit.id">{{
              details.commit.id.slice(0, 7)
            }}</span>
            @if (details.commit.parents.length > 1) {
              · merge, compared with
              <span class="font-mono">{{ details.commit.parents[0].slice(0, 7) }}</span>
            }
          </p>
          @if (details.body) {
            <pre
              class="mt-2 max-h-32 overflow-auto font-sans text-sm whitespace-pre-wrap text-slate-700"
              >{{ details.body }}</pre>
          }
        </div>
      } @else {
        <p class="min-w-0 flex-1 text-sm text-slate-500">
          <fa-icon [icon]="faSpinner" animation="spin" /> Reading commit…
        </p>
      }
      <button
        type="button"
        class="flex size-7 shrink-0 items-center justify-center rounded text-slate-500 hover:bg-slate-100 hover:text-slate-800"
        title="Close"
        aria-label="Close"
        (click)="history.close()"
      >
        <fa-icon [icon]="faXmark" />
      </button>
    </header>

    @if (history.reviewError(); as error) {
      <div
        class="mx-4 mt-3 flex items-start gap-2 rounded border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-800"
        role="alert"
      >
        <fa-icon class="mt-0.5" [icon]="faTriangleExclamation" />
        <p class="min-w-0 flex-1 break-words whitespace-pre-line">{{ error.message }}</p>
      </div>
    }

    @if (history.commit(); as details) {
      <div class="flex min-h-0 flex-1">
        <nav
          class="flex w-64 shrink-0 flex-col border-r border-slate-200"
          aria-label="Changed files"
        >
          <h3 class="px-3 pt-2 pb-1 text-xs font-semibold tracking-wide text-slate-500 uppercase">
            Files ({{ details.files.length }})
          </h3>
          <ul class="min-h-0 flex-1 overflow-y-auto pb-2 text-sm">
            @for (change of details.files; track change.path) {
              <li>
                <button
                  type="button"
                  class="flex w-full items-center gap-2 px-3 py-0.5 text-left hover:bg-slate-100"
                  [class.bg-sky-100]="selectedPath() === change.path"
                  [attr.aria-current]="selectedPath() === change.path ? 'true' : null"
                  [title]="describe(change)"
                  (click)="history.openFile(change.path)"
                >
                  <span class="truncate">{{ fileName(change.path) }}</span>
                  <span class="truncate text-xs text-slate-400">{{ folderOf(change.path) }}</span>
                  <span
                    class="ml-auto font-mono text-xs font-bold"
                    [class]="badges[change.status].color"
                    [title]="change.status"
                    >{{ badges[change.status].letter }}</span
                  >
                </button>
              </li>
            } @empty {
              <li class="px-3 py-1 text-slate-500">No files in this workspace changed.</li>
            }
          </ul>
        </nav>

        <section class="flex min-w-0 flex-1 flex-col" aria-label="File changes">
          @if (history.file(); as file) {
            <div
              class="flex shrink-0 items-center gap-3 border-b border-slate-200 bg-slate-50 px-3 py-1.5 text-sm"
            >
              <span class="min-w-0 flex-1 truncate" [title]="describe(file.change)">
                {{ describe(file.change) }}
              </span>
              @if (history.openingFile()) {
                <fa-icon class="text-slate-400" [icon]="faSpinner" animation="spin" />
              }
              @if (file.isRequest && !file.binary) {
                <div
                  class="flex shrink-0 overflow-hidden rounded border border-slate-300 text-sm"
                  role="group"
                  aria-label="Show the changes as"
                >
                  <button
                    type="button"
                    class="flex items-center gap-1.5 px-2.5 py-1 disabled:cursor-not-allowed disabled:opacity-50"
                    [class]="
                      effectiveMode() === 'request' ? 'bg-sky-600 text-white' : 'hover:bg-slate-100'
                    "
                    [attr.aria-pressed]="effectiveMode() === 'request'"
                    [disabled]="!canShowRequest()"
                    [title]="canShowRequest() ? 'Compare as a request' : (file.requestError ?? '')"
                    (click)="mode.set('request')"
                  >
                    <fa-icon [icon]="faFileCode" /> Request
                  </button>
                  <button
                    type="button"
                    class="flex items-center gap-1.5 border-l border-slate-300 px-2.5 py-1"
                    [class]="
                      effectiveMode() === 'text' ? 'bg-sky-600 text-white' : 'hover:bg-slate-100'
                    "
                    [attr.aria-pressed]="effectiveMode() === 'text'"
                    title="Compare the file's text"
                    (click)="mode.set('text')"
                  >
                    <fa-icon [icon]="faFileLines" /> Text
                  </button>
                </div>
              }
              <label class="flex shrink-0 items-center gap-1.5 text-xs text-slate-600">
                <input
                  type="checkbox"
                  class="accent-sky-600"
                  [checked]="sideBySide()"
                  (change)="sideBySide.set(!sideBySide())"
                />
                Side by side
              </label>
            </div>
            @if (file.isRequest && file.requestError && !file.binary) {
              <p
                class="mx-4 mt-3 flex items-start gap-2 rounded border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800"
                role="status"
              >
                <fa-icon class="mt-0.5" [icon]="faCircleInfo" />
                <span class="min-w-0 flex-1 break-words">
                  Showing the text: a version of this file can't be read as a request.
                  {{ file.requestError }}
                </span>
              </p>
            }
            @if (file.binary) {
              <p class="p-4 text-sm text-slate-500 italic">
                This is a binary file; it isn't shown.
              </p>
            } @else if (effectiveMode() === 'request') {
              <app-request-diff
                class="min-h-0 flex-1 overflow-auto"
                [before]="requests().before"
                [after]="requests().after"
                [sideBySide]="sideBySide()"
              />
            } @else {
              <app-code-diff-editor
                class="m-4 min-h-0 flex-1 overflow-hidden rounded border border-slate-300"
                [original]="file.before ?? ''"
                [modified]="file.after ?? ''"
                [language]="language(file.change.path)"
                [sideBySide]="sideBySide()"
                [ariaLabel]="'Changes to ' + file.change.path"
                [hideUnchanged]="true"
              />
            }
          } @else if (history.openingFile()) {
            <p class="p-4 text-sm text-slate-500">
              <fa-icon [icon]="faSpinner" animation="spin" /> Reading file…
            </p>
          } @else if (details.files.length) {
            <p class="p-4 text-sm text-slate-500">Choose a file to see its changes.</p>
          }
        </section>
      </div>
    }
  `,
})
export class CommitView {
  protected readonly history = inject(GitHistoryStore);
  protected readonly badges = STATUS_BADGES;
  protected readonly sideBySide = signal(true);
  protected readonly fileName = fileName;
  protected readonly folderOf = folderOf;

  protected readonly faCodeCommit = faCodeCommit;
  protected readonly faCircleInfo = faCircleInfo;
  protected readonly faFileCode = faFileCode;
  protected readonly faFileLines = faFileLines;
  protected readonly faSpinner = faSpinner;
  protected readonly faTriangleExclamation = faTriangleExclamation;
  protected readonly faXmark = faXmark;

  /** The mode the user picked; kept while moving between files. */
  protected readonly mode = signal<DiffMode>('request');

  /** The file shown or being opened. */
  protected readonly selectedPath = computed(
    () => this.history.openingFile() ?? this.history.file()?.change.path ?? null,
  );

  /** Whether every existing side of the request file could be parsed. */
  protected readonly canShowRequest = computed(() => {
    const file = this.history.file();
    return (
      !!file &&
      file.isRequest &&
      !file.binary &&
      !file.requestError &&
      !!(file.originalRequest || file.modifiedRequest)
    );
  });

  protected readonly effectiveMode = computed<DiffMode>(() =>
    this.canShowRequest() ? this.mode() : 'text',
  );

  protected readonly requests = computed(() => {
    const file = this.history.file();
    return {
      before: file?.originalRequest ? fromRequestDto(file.originalRequest) : null,
      after: file?.modifiedRequest ? fromRequestDto(file.modifiedRequest) : null,
    };
  });

  protected language(path: string): string {
    return languageForPath(path);
  }

  protected describe(change: GitChange): string {
    return change.originalPath ? `${change.originalPath} → ${change.path}` : change.path;
  }

  protected formatDate(date: string): string {
    return new Date(date).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
  }
}
