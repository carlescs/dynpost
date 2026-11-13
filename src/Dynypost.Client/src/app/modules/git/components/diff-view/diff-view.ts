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
  templateUrl: './diff-view.html',
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
