import { DestroyRef, Injectable, inject } from '@angular/core';
import { BridgeEvents } from '../models/messages';
import { touchesArea } from '../models/disk-changes';
import { PLANS_FOLDER, REQUESTS_FOLDER } from '../models/workspace';
import { Bridge } from './bridge';
import { EnvironmentStore } from './environment-store';
import { GitStore } from './git-store';
import { RunStore } from './run-store';
import { WorkspaceStore } from './workspace-store';

/**
 * Keeps the UI in step with the open workspace on disk, whoever changes it. The host watches the
 * workspace and reports files that changed (edited in another program, changed by git in a
 * terminal, or saved by the app itself), changes an AI assistant made through the MCP server, and
 * changes to the git state. This re-reads what changed: the tree, the open requests, environments,
 * folder settings and run plans (reloading those without unsaved edits, and asking about those
 * with), the plans and their runs, and the git status; it also follows the load runs an assistant
 * starts and opens the files it asks to show. The app shell injects it to start it.
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
      this.bridge.on('workspace.filesChanged', ({ paths }) => void this.reload(paths ?? undefined)),
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

  /**
   * Re-reads what the UI shows of the files at `paths` (or inside folders at `paths`; anything
   * when missing), which the host saw change on disk. The git status is re-read on its own event.
   */
  private async reload(paths?: string[]): Promise<void> {
    if (!this.workspace.workspace()) return;
    // A file's local-only mark lives in .gitignore.
    if (touchesArea(paths, REQUESTS_FOLDER, GITIGNORE)) {
      await this.workspace.refresh();
      await this.workspace.reloadFromDisk(paths);
    }
    // The selected environment is kept in the workspace file.
    if (touchesArea(paths, ENVIRONMENTS_FOLDER, WORKSPACE_FILE, GITIGNORE)) {
      await this.environments.refresh(paths);
    }
    if (touchesArea(paths, PLANS_FOLDER, GITIGNORE)) {
      await this.runs.refresh();
      await this.runs.reloadFromDisk(paths);
    }
  }
}

const GITIGNORE = '.gitignore';
const WORKSPACE_FILE = 'dynpost.yaml';
const ENVIRONMENTS_FOLDER = 'environments';
