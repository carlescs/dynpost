import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { Bridge, BridgeError } from '../bridge/bridge';
import {
  BridgeErrorShape,
  GitCommitDetails,
  GitCommitInfo,
  GitFileRevision,
} from '../bridge/messages';
import { WorkspaceStore } from '../workspace/workspace-store';
import { GitStore } from './git-store';

/** How many commits each page of the history loads. */
export const HISTORY_PAGE_SIZE = 50;

/**
 * The commit history of the open workspace, and the commit being reviewed in the main area. Reading
 * history never changes the repository, so it runs alongside the operations in {@link GitStore}.
 */
@Injectable({ providedIn: 'root' })
export class GitHistoryStore {
  private readonly bridge = inject(Bridge);
  private readonly workspace = inject(WorkspaceStore);
  private readonly git = inject(GitStore);

  /** The commits loaded so far, newest first; `null` until the history is first loaded. */
  readonly commits = signal<GitCommitInfo[] | null>(null);
  /** Whether older commits than those loaded exist. */
  readonly hasMore = signal(false);
  readonly loading = signal(false);
  /** Why the history could not be read. */
  readonly error = signal<BridgeErrorShape | null>(null);

  /** The commit being reviewed in the main area, if any. */
  readonly commit = signal<GitCommitDetails | null>(null);
  /** The id of the commit being opened, while it loads. */
  readonly opening = signal<string | null>(null);
  /** The file of {@link commit} whose changes are shown. */
  readonly file = signal<GitFileRevision | null>(null);
  /** The path of the file being opened, while it loads. */
  readonly openingFile = signal<string | null>(null);
  /** Why the commit or one of its files could not be read. */
  readonly reviewError = signal<BridgeErrorShape | null>(null);

  /** Whether the commit review takes the main area. */
  readonly reviewing = computed(() => this.commit() !== null || this.opening() !== null);

  private readonly root = computed(() => this.workspace.workspace()?.root ?? null);
  /** Increments to discard answers to requests made before a reset or a newer request. */
  private historyRequest = 0;
  private reviewRequest = 0;

  constructor() {
    // A new workspace starts with no history loaded and nothing under review.
    effect(() => {
      this.root();
      untracked(() => {
        this.historyRequest++;
        this.commits.set(null);
        this.hasMore.set(false);
        this.error.set(null);
        this.close();
      });
    });

    // Commits, pulls and outside changes show up in the status: reload the history once it is shown.
    effect(() => {
      this.git.status();
      untracked(() => {
        if (this.commits() !== null && !this.loading()) void this.load();
      });
    });

    // Opening or selecting a request or environment tab (even the one shown) shows it instead.
    effect(() => {
      this.workspace.shown();
      untracked(() => this.close());
    });

    // The review and a working tree diff share the main area: opening a diff closes the review.
    effect(() => {
      if (this.git.diff()) untracked(() => this.close());
    });
  }

  /** (Re)loads the history from the newest commit, keeping as many commits as were loaded. */
  async load(): Promise<void> {
    const count = Math.max(HISTORY_PAGE_SIZE, this.commits()?.length ?? 0);
    await this.fetch(0, count, (page) => page);
  }

  /** Loads the next page of older commits. */
  async loadMore(): Promise<void> {
    const loaded = this.commits() ?? [];
    if (this.loading() || !this.hasMore()) return;
    await this.fetch(loaded.length, HISTORY_PAGE_SIZE, (page) => [...loaded, ...page]);
  }

  /** Shows a commit's message and changed files in the main area, with the first file opened. */
  async open(commitId: string): Promise<void> {
    if (!this.root()) return;
    const request = ++this.reviewRequest;
    this.opening.set(commitId);
    this.reviewError.set(null);
    this.git.closeConflict();
    this.git.closeDiff();
    try {
      const details = await this.bridge.send('git.show', { commitId });
      if (request !== this.reviewRequest) return;
      this.commit.set(details);
      this.file.set(null);
      this.opening.set(null);
      if (details.files.length) await this.openFile(details.files[0].path);
    } catch (error) {
      if (request !== this.reviewRequest) return;
      this.opening.set(null);
      this.reviewError.set(toErrorShape(error));
    }
  }

  /** Shows what the reviewed commit changed in one of its files. */
  async openFile(path: string): Promise<void> {
    const commit = this.commit();
    if (!commit) return;
    const request = ++this.reviewRequest;
    this.openingFile.set(path);
    this.reviewError.set(null);
    try {
      const file = await this.bridge.send('git.commitFile', { commitId: commit.commit.id, path });
      if (request !== this.reviewRequest) return;
      this.file.set(file);
    } catch (error) {
      if (request !== this.reviewRequest) return;
      this.reviewError.set(toErrorShape(error));
    } finally {
      if (request === this.reviewRequest) this.openingFile.set(null);
    }
  }

  /** Closes the commit review. */
  close(): void {
    this.reviewRequest++;
    this.commit.set(null);
    this.file.set(null);
    this.opening.set(null);
    this.openingFile.set(null);
    this.reviewError.set(null);
  }

  private async fetch(
    skip: number,
    limit: number,
    merge: (page: GitCommitInfo[]) => GitCommitInfo[],
  ): Promise<void> {
    if (!this.root() || !this.git.available) return;
    const request = ++this.historyRequest;
    this.loading.set(true);
    this.error.set(null);
    try {
      const page = await this.bridge.send('git.log', { skip, limit });
      if (request !== this.historyRequest) return;
      this.commits.set(merge(page.commits));
      this.hasMore.set(page.hasMore);
    } catch (error) {
      if (request !== this.historyRequest) return;
      this.error.set(toErrorShape(error));
    } finally {
      if (request === this.historyRequest) this.loading.set(false);
    }
  }
}

function toErrorShape(error: unknown): BridgeErrorShape {
  return error instanceof BridgeError
    ? { code: error.code, message: error.message }
    : { code: 'internal_error', message: String(error) };
}
