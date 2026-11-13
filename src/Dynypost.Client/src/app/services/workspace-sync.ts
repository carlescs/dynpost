import { DestroyRef, Injectable, inject } from '@angular/core';
import { BridgeEvents } from '../models/messages';
import { Bridge } from './bridge';
import { EnvironmentStore } from './environment-store';
import { GitStore } from './git-store';
import { RunStore } from './run-store';
import { WorkspaceStore } from './workspace-store';

/**
 * Shows changes made to the open workspace outside the UI — by an AI assistant through the MCP
 * server — as the host reports them: re-reads the tree, the open requests and environments that
 * changed (keeping unsaved edits), run plans and their runs, and the git status; and follows the
 * load runs it starts, and opens the files it asks to show. It also re-reads the git status
 * whenever the host sees the workspace's files or git state change, whoever changed them. The app
 * shell injects it to start it.
 */
@Injectable({ providedIn: 'root' })
export class WorkspaceSync {
  private readonly bridge = inject(Bridge);
  private readonly workspace = inject(WorkspaceStore);
  private readonly environments = inject(EnvironmentStore);
  private readonly git = inject(GitStore);
  private readonly runs = inject(RunStore);

  constructor() {
    const stops = [
      this.bridge.on('workspace.changed', ({ paths }) => void this.apply(paths)),
      this.bridge.on('run.started', ({ path, runId }) => {
        if (this.workspace.workspace()) void this.runs.followStarted(path, runId);
      }),
      this.bridge.on('item.open', (item) => void this.open(item)),
      // The host watches the workspace and its repository, whoever changes them.
      this.bridge.on('git.changed', () => {
        if (this.workspace.workspace() && this.git.available) void this.git.refreshInBackground();
      }),
    ];
    inject(DestroyRef).onDestroy(() => stops.forEach((stop) => stop()));
  }

  private async open({ path, kind, id }: BridgeEvents['item.open']): Promise<void> {
    if (!this.workspace.workspace()) return;
    switch (kind) {
      case 'request':
        await this.workspace.openRequest({ path, name: path, kind: 'request' });
        break;
      case 'runPlan':
        await this.runs.open(path);
        break;
      case 'environment':
        await this.environments.edit(id);
        break;
    }
  }

  private async apply(paths: string[]): Promise<void> {
    if (!this.workspace.workspace()) return;
    await this.workspace.refresh();
    if (paths.length) await this.workspace.reloadFromDisk(paths);
    // The selected environment, its variables or captured values may have changed too.
    await this.environments.refresh(paths);
    await this.runs.reloadFromDisk(paths);
    if (this.git.available) void this.git.refreshInBackground();
  }
}
