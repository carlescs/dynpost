import { TestBed } from '@angular/core/testing';
import { BridgeEvents } from '../models/messages';
import { Bridge } from './bridge';
import { EnvironmentStore } from './environment-store';
import { GitStore } from './git-store';
import { RunStore } from './run-store';
import { WorkspaceStore } from './workspace-store';
import { WorkspaceSync } from './workspace-sync';

describe('WorkspaceSync', () => {
  function setup(open = true) {
    const listeners = new Map<string, (payload: unknown) => void>();
    const emit = <K extends keyof BridgeEvents>(type: K, payload: BridgeEvents[K]) =>
      listeners.get(type)?.(payload);
    const stop = vi.fn();
    const on = vi.fn((type: string, listener: (payload: unknown) => void) => {
      listeners.set(type, listener);
      return stop;
    });
    const workspace = {
      workspace: () => (open ? { root: '/work/api' } : null),
      refresh: vi.fn().mockResolvedValue(undefined),
      reloadFromDisk: vi.fn().mockResolvedValue(undefined),
      openRequest: vi.fn().mockResolvedValue(undefined),
    };
    const environments = {
      refresh: vi.fn().mockResolvedValue(undefined),
      edit: vi.fn().mockResolvedValue(undefined),
    };
    const git = { available: true, refreshInBackground: vi.fn().mockResolvedValue(undefined) };
    const runs = {
      refresh: vi.fn().mockResolvedValue(undefined),
      reloadFromDisk: vi.fn().mockResolvedValue(undefined),
      followStarted: vi.fn().mockResolvedValue(undefined),
      open: vi.fn().mockResolvedValue(undefined),
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: Bridge, useValue: { isDesktop: true, on } },
        { provide: WorkspaceStore, useValue: workspace },
        { provide: EnvironmentStore, useValue: environments },
        { provide: GitStore, useValue: git },
        { provide: RunStore, useValue: runs },
      ],
    });
    TestBed.inject(WorkspaceSync);
    // The listener's work only awaits resolved mocks: it is done by the next macrotask.
    const changed = async (paths: string[]) => {
      emit('workspace.changed', { paths });
      await new Promise((resolve) => setTimeout(resolve));
    };
    const filesChanged = async (paths: string[] | null) => {
      emit('workspace.filesChanged', { paths });
      await new Promise((resolve) => setTimeout(resolve));
    };
    return { on, stop, emit, workspace, environments, git, runs, changed, filesChanged };
  }

  it('re-reads the tree, the changed files, the environments, the run plans and the git status', async () => {
    const { on, workspace, environments, git, runs, changed } = setup();
    expect(on).toHaveBeenCalledWith('workspace.changed', expect.any(Function));

    await changed(['requests/get-user.yaml']);

    expect(workspace.refresh).toHaveBeenCalled();
    expect(workspace.reloadFromDisk).toHaveBeenCalledWith(['requests/get-user.yaml']);
    expect(environments.refresh).toHaveBeenCalledWith(['requests/get-user.yaml']);
    expect(runs.reloadFromDisk).toHaveBeenCalledWith(['requests/get-user.yaml']);
    expect(git.refreshInBackground).toHaveBeenCalled();
  });

  it('leaves open files alone when only other state changed', async () => {
    const { workspace, environments, changed } = setup();

    await changed([]);

    expect(workspace.reloadFromDisk).not.toHaveBeenCalled();
    expect(environments.refresh).toHaveBeenCalledWith([]);
  });

  it('does nothing without an open workspace', async () => {
    const { workspace, changed } = setup(false);

    await changed(['requests/a.yaml']);

    expect(workspace.refresh).not.toHaveBeenCalled();
  });

  it('follows load runs started outside the UI', () => {
    const { emit, runs } = setup();

    emit('run.started', { path: 'plans/smoke.run.yaml', runId: 'r1' });

    expect(runs.followStarted).toHaveBeenCalledWith('plans/smoke.run.yaml', 'r1');
  });

  it('ignores load runs started without an open workspace', () => {
    const { emit, runs } = setup(false);

    emit('run.started', { path: 'plans/smoke.run.yaml', runId: 'r1' });

    expect(runs.followStarted).not.toHaveBeenCalled();
  });

  it('opens the requests, run plans and environments it is asked to show', () => {
    const { emit, workspace, runs, environments } = setup();

    emit('item.open', { path: 'requests/a.yaml', kind: 'request', id: 'requests/a.yaml' });
    emit('item.open', { path: 'plans/p.run.yaml', kind: 'runPlan', id: 'plans/p.run.yaml' });
    emit('item.open', { path: 'environments/dev.yaml', kind: 'environment', id: 'dev.yaml' });

    expect(workspace.openRequest).toHaveBeenCalledWith(
      expect.objectContaining({ path: 'requests/a.yaml', kind: 'request' }),
    );
    expect(runs.open).toHaveBeenCalledWith('plans/p.run.yaml');
    expect(environments.edit).toHaveBeenCalledWith('dev.yaml');
  });

  it('opens nothing without an open workspace', () => {
    const { emit, workspace } = setup(false);

    emit('item.open', { path: 'requests/a.yaml', kind: 'request', id: 'requests/a.yaml' });

    expect(workspace.openRequest).not.toHaveBeenCalled();
  });

  it('re-reads the git status when the host sees the workspace change', () => {
    const { git, emit } = setup();

    emit('git.changed', {});

    expect(git.refreshInBackground).toHaveBeenCalled();
  });

  it('ignores git changes without an open workspace', () => {
    const { git, emit } = setup(false);

    emit('git.changed', {});

    expect(git.refreshInBackground).not.toHaveBeenCalled();
  });

  it('stops listening when the app is destroyed', () => {
    const { stop } = setup();

    TestBed.resetTestingModule();

    expect(stop).toHaveBeenCalledTimes(5);
  });

  describe('files changed on disk', () => {
    it('re-reads the tree and the open requests when requests changed', async () => {
      const { workspace, environments, runs, git, filesChanged } = setup();

      await filesChanged(['requests/users', 'requests/people']);

      expect(workspace.refresh).toHaveBeenCalled();
      expect(workspace.reloadFromDisk).toHaveBeenCalledWith(['requests/users', 'requests/people']);
      expect(environments.refresh).not.toHaveBeenCalled();
      expect(runs.reloadFromDisk).not.toHaveBeenCalled();
      // The git status follows its own event.
      expect(git.refreshInBackground).not.toHaveBeenCalled();
    });

    it('re-reads the environments when they or the workspace file changed', async () => {
      const { workspace, environments, filesChanged } = setup();

      await filesChanged(['dynpost.yaml']);

      expect(environments.refresh).toHaveBeenCalledWith(['dynpost.yaml']);
      expect(workspace.refresh).not.toHaveBeenCalled();
    });

    it('re-reads the plans and the open ones when plans changed', async () => {
      const { runs, workspace, filesChanged } = setup();

      await filesChanged(['plans/smoke.run.yaml']);

      expect(runs.refresh).toHaveBeenCalled();
      expect(runs.reloadFromDisk).toHaveBeenCalledWith(['plans/smoke.run.yaml']);
      expect(workspace.refresh).not.toHaveBeenCalled();
    });

    it('re-reads everything when changes were lost', async () => {
      const { workspace, environments, runs, filesChanged } = setup();

      await filesChanged(null);

      expect(workspace.reloadFromDisk).toHaveBeenCalledWith(undefined);
      expect(environments.refresh).toHaveBeenCalledWith(undefined);
      expect(runs.reloadFromDisk).toHaveBeenCalledWith(undefined);
    });

    it('does nothing without an open workspace', async () => {
      const { workspace, filesChanged } = setup(false);

      await filesChanged(['requests/a.yaml']);

      expect(workspace.refresh).not.toHaveBeenCalled();
    });
  });
});
