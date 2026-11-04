import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { Bridge } from '../bridge/bridge';
import {
  BridgeError,
  BridgeResult,
  GitStatus,
  MessageType,
  RequestOf,
  ResponseOf,
} from '../bridge/messages';
import { WorkspaceStore } from '../workspace/workspace-store';

export type GitOperation = 'refresh' | 'commit' | 'pull' | 'push';

export interface GitNotice {
  kind: 'info' | 'warning';
  text: string;
}

/**
 * Git state of the open workspace. All git work happens in the .NET backend; this store calls
 * it through the bridge and keeps the latest status, the running operation and its outcome.
 */
@Injectable({ providedIn: 'root' })
export class GitStore {
  private readonly bridge = inject(Bridge);
  private readonly workspace = inject(WorkspaceStore);

  /** Whether git is reachable (the app runs in the desktop host). */
  readonly available = this.bridge.available;
  /** The latest status, or `null` before it is loaded or when it could not be read. */
  readonly status = signal<GitStatus | null>(null);
  /** The operation in progress; only one runs at a time. */
  readonly busy = signal<GitOperation | null>(null);
  /** The error of the last operation, until the next one starts or it is dismissed. */
  readonly error = signal<BridgeError | null>(null);
  /** The outcome of the last commit, pull or push. */
  readonly notice = signal<GitNotice | null>(null);

  readonly changeCount = computed(() => this.status()?.changes.length ?? 0);
  readonly conflicts = computed(
    () => this.status()?.changes.filter((c) => c.status === 'conflicted') ?? [],
  );

  constructor() {
    effect(() => {
      const path = this.workspace.workspacePath();
      untracked(() => {
        this.status.set(null);
        this.error.set(null);
        this.notice.set(null);
        if (path) void this.refresh();
      });
    });
  }

  async refresh(): Promise<void> {
    await this.run('refresh', 'git.status', {}, (status) => this.status.set(status));
  }

  /** Commits `paths`, or every change when `paths` is empty. Resolves to whether it succeeded. */
  commit(message: string, paths: string[] = []): Promise<boolean> {
    return this.run(
      'commit',
      'git.commit',
      { message, ...(paths.length ? { paths } : {}) },
      (r) => {
        this.status.set(r.status);
        this.notice.set({ kind: 'info', text: `Committed ${r.commitId.slice(0, 7)}.` });
      },
    );
  }

  async pull(): Promise<void> {
    await this.run('pull', 'git.pull', {}, (r) => {
      this.status.set(r.status);
      this.notice.set(PULL_NOTICES[r.outcome]);
    });
  }

  async push(): Promise<void> {
    await this.run('push', 'git.push', {}, (status) => {
      this.status.set(status);
      this.notice.set({ kind: 'info', text: `Pushed to ${status.upstream ?? 'the remote'}.` });
    });
  }

  dismissError(): void {
    this.error.set(null);
  }

  private async run<T extends MessageType & `git.${string}`>(
    operation: GitOperation,
    type: T,
    payload: Omit<RequestOf<T>, 'workspacePath'>,
    onSuccess: (response: ResponseOf<T>) => void,
  ): Promise<boolean> {
    const workspacePath = this.workspace.workspacePath();
    if (!workspacePath || this.busy()) return false;

    this.busy.set(operation);
    this.error.set(null);
    if (operation !== 'refresh') this.notice.set(null);

    let result: BridgeResult<ResponseOf<T>>;
    try {
      result = await this.bridge.request(type, { ...payload, workspacePath } as RequestOf<T>);
    } finally {
      this.busy.set(null);
    }

    // Ignore the answer if the user switched workspaces in the meantime.
    if (this.workspace.workspacePath() !== workspacePath) return false;

    if (!result.ok) {
      this.error.set(result.error);
      if (operation === 'refresh') this.status.set(null);
      return false;
    }
    onSuccess(result.value);
    return true;
  }
}

const PULL_NOTICES: Record<ResponseOf<'git.pull'>['outcome'], GitNotice> = {
  upToDate: { kind: 'info', text: 'Already up to date.' },
  updated: { kind: 'info', text: 'Pulled the latest changes.' },
  conflicted: { kind: 'warning', text: 'The pull stopped on merge conflicts.' },
};
