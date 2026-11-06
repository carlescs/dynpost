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
  templateUrl: './commit-view.html',
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
