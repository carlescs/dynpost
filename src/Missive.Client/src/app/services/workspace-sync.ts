import { DestroyRef, Injectable, inject } from '@angular/core';
import { Bridge } from './bridge';
import { EnvironmentStore } from './environment-store';
import { GitStore } from './git-store';
import { WorkspaceStore } from './workspace-store';

/**
 * Shows changes made to the open workspace outside the UI — by an AI assistant through the MCP
 * server — as the host reports them: re-reads the tree, the open requests and environments that
 * changed (keeping unsaved edits), and the git status. The app shell injects it to start it.
 */
@Injectable({ providedIn: 'root' })
export class WorkspaceSync {
  private readonly bridge = inject(Bridge);
  private readonly workspace = inject(WorkspaceStore);
  private readonly environments = inject(EnvironmentStore);
  private readonly git = inject(GitStore);

  constructor() {
    const stop = this.bridge.on('workspace.changed', ({ paths }) => void this.apply(paths));
    inject(DestroyRef).onDestroy(stop);
  }

  private async apply(paths: string[]): Promise<void> {
    if (!this.workspace.workspace()) return;
    await this.workspace.refresh();
    if (paths.length) await this.workspace.reloadFromDisk(paths);
    // The selected environment, its variables or captured values may have changed too.
    await this.environments.refresh(paths);
    if (this.git.available) void this.git.refresh();
  }
}
