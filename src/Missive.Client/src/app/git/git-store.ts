import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { Bridge, BridgeError } from '../bridge/bridge';
import {
  BridgeErrorShape,
  ConflictResolution,
  GitConflict,
  GitStatus,
  PullOutcome,
} from '../bridge/messages';
import { WorkspaceStore } from '../workspace/workspace-store';

export type GitOperation =
  'refresh' | 'commit' | 'pull' | 'push' | 'conflict' | 'resolve' | 'abort';

export interface GitNotice {
  kind: 'info' | 'warning';
  text: string;
}

/**
 * Git state of the open workspace. All git work happens in the .NET host; this store calls it
 * through the bridge and keeps the latest status, the running operation and its outcome.
 */
@Injectable({ providedIn: 'root' })
export class GitStore {
  private readonly bridge = inject(Bridge);
  private readonly workspace = inject(WorkspaceStore);

  /** Whether git is reachable: the browser dev server has no git backend. */
  readonly available = this.bridge.isDesktop;
  /** The latest status, or `null` before it is loaded or when it could not be read. */
  readonly status = signal<GitStatus | null>(null);
  /** The operation in progress; only one runs at a time. */
  readonly busy = signal<GitOperation | null>(null);
  /** The error of the last operation, until the next one starts or it is dismissed. */
  readonly error = signal<BridgeErrorShape | null>(null);
  /** The outcome of the last commit, pull or push. */
  readonly notice = signal<GitNotice | null>(null);
  /** The conflicted file being compared in the main area, if any. */
  readonly conflict = signal<GitConflict | null>(null);

  readonly changeCount = computed(() => this.status()?.changes.length ?? 0);
  readonly conflicts = computed(
    () => this.status()?.changes.filter((c) => c.status === 'conflicted') ?? [],
  );

  private readonly root = computed(() => this.workspace.workspace()?.root ?? null);

  constructor() {
    if (this.available) {
      // Pick up commits and edits made in other tools when the user comes back to the window.
      window.addEventListener('focus', () => {
        if (this.root()) void this.refresh();
      });
    }

    effect(() => {
      const root = this.root();
      untracked(() => {
        this.status.set(null);
        this.error.set(null);
        this.notice.set(null);
        this.conflict.set(null);
        if (root && this.available) void this.refresh();
      });
    });
  }

  async refresh(): Promise<void> {
    await this.run(
      'refresh',
      () => this.bridge.send('git.status'),
      (status) => this.status.set(status),
    );
  }

  /** Commits `paths`, or every change when `paths` is empty. Resolves to whether it succeeded. */
  commit(message: string, paths: string[] = []): Promise<boolean> {
    return this.run(
      'commit',
      () => this.bridge.send('git.commit', { message, ...(paths.length ? { paths } : {}) }),
      (r) => {
        const completedMerge = this.status()?.merging === true;
        this.status.set(r.status);
        this.notice.set({
          kind: 'info',
          text: `${completedMerge ? 'Merge completed' : 'Committed'} (${r.commitId.slice(0, 7)}).`,
        });
      },
    );
  }

  async pull(): Promise<void> {
    await this.run(
      'pull',
      () => this.bridge.send('git.pull'),
      (r) => {
        this.status.set(r.status);
        this.notice.set(PULL_NOTICES[r.outcome]);
        // A pull can add, change or remove request files.
        void this.workspace.refresh();
        void this.workspace.reloadFromDisk();
      },
    );
  }

  async push(): Promise<void> {
    await this.run(
      'push',
      () => this.bridge.send('git.push'),
      (status) => {
        this.status.set(status);
        this.notice.set({ kind: 'info', text: `Pushed to ${status.upstream ?? 'the remote'}.` });
      },
    );
  }

  /** Shows both sides of a conflicted file. */
  async openConflict(path: string): Promise<void> {
    await this.run(
      'conflict',
      () => this.bridge.send('git.conflict', { path }),
      (conflict) => this.conflict.set(conflict),
    );
  }

  closeConflict(): void {
    this.conflict.set(null);
  }

  /** Resolves one conflicted file the way the user chose. */
  async resolve(path: string, resolution: ConflictResolution): Promise<void> {
    await this.run(
      'resolve',
      () => this.bridge.send('git.resolve', { path, resolution }),
      (status) => {
        this.status.set(status);
        if (this.conflict()?.path === path) this.conflict.set(null);
        const remaining = status.changes.filter((c) => c.status === 'conflicted').length;
        this.notice.set({
          kind: 'info',
          text: remaining
            ? `Resolved ${path}. ${remaining} conflicted file(s) left.`
            : 'All conflicts are resolved. Commit to complete the merge.',
        });
        void this.workspace.refresh();
        void this.workspace.reloadFromDisk([path]);
      },
    );
  }

  /** Abandons the merge and restores the branch to how it was before the pull. */
  async abortMerge(): Promise<void> {
    await this.run(
      'abort',
      () => this.bridge.send('git.abortMerge'),
      (status) => {
        this.status.set(status);
        this.conflict.set(null);
        this.notice.set({ kind: 'info', text: 'The merge was aborted.' });
        void this.workspace.refresh();
        void this.workspace.reloadFromDisk();
      },
    );
  }

  dismissError(): void {
    this.error.set(null);
  }

  private async run<T>(
    operation: GitOperation,
    call: () => Promise<T>,
    onSuccess: (result: T) => void,
  ): Promise<boolean> {
    const root = this.root();
    if (!root || this.busy()) return false;

    this.busy.set(operation);
    this.error.set(null);
    if (operation !== 'refresh') this.notice.set(null);

    try {
      const result = await call();
      // Ignore the answer if the user switched workspaces in the meantime.
      if (this.root() !== root) return false;
      onSuccess(result);
      return true;
    } catch (error) {
      if (this.root() !== root) return false;
      this.error.set(
        error instanceof BridgeError
          ? { code: error.code, message: error.message }
          : { code: 'internal_error', message: String(error) },
      );
      if (operation === 'refresh') this.status.set(null);
      return false;
    } finally {
      this.busy.set(null);
    }
  }
}

const PULL_NOTICES: Record<PullOutcome, GitNotice> = {
  upToDate: { kind: 'info', text: 'Already up to date.' },
  updated: { kind: 'info', text: 'Pulled the latest changes.' },
  conflicted: {
    kind: 'warning',
    text: 'The pull stopped on merge conflicts. Open each conflicted file below to resolve it.',
  },
};
