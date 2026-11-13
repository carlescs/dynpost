import { TestBed } from '@angular/core/testing';
import { BridgeEvents } from '../models/messages';
import { Bridge } from './bridge';
import { EnvironmentStore } from './environment-store';
import { GitStore } from './git-store';
import { WorkspaceStore } from './workspace-store';
import { WorkspaceSync } from './workspace-sync';

describe('WorkspaceSync', () => {
  function setup(open = true) {
    let emit: (payload: BridgeEvents['workspace.changed']) => void = () => undefined;
    const stop = vi.fn();
    const on = vi.fn((_: string, listener: typeof emit) => {
      emit = listener;
      return stop;
    });
    const workspace = {
      workspace: () => (open ? { root: '/work/api' } : null),
      refresh: vi.fn().mockResolvedValue(undefined),
      reloadFromDisk: vi.fn().mockResolvedValue(undefined),
    };
    const environments = { refresh: vi.fn().mockResolvedValue(undefined) };
    const git = { available: true, refresh: vi.fn().mockResolvedValue(undefined) };
    TestBed.configureTestingModule({
      providers: [
        { provide: Bridge, useValue: { isDesktop: true, on } },
        { provide: WorkspaceStore, useValue: workspace },
        { provide: EnvironmentStore, useValue: environments },
        { provide: GitStore, useValue: git },
      ],
    });
    TestBed.inject(WorkspaceSync);
    // The listener's work only awaits resolved mocks: it is done by the next macrotask.
    const changed = async (paths: string[]) => {
      emit({ paths });
      await new Promise((resolve) => setTimeout(resolve));
    };
    return { on, stop, workspace, environments, git, changed };
  }

  it('re-reads the tree, the changed files, the environments and the git status', async () => {
    const { on, workspace, environments, git, changed } = setup();
    expect(on).toHaveBeenCalledWith('workspace.changed', expect.any(Function));

    await changed(['requests/get-user.yaml']);

    expect(workspace.refresh).toHaveBeenCalled();
    expect(workspace.reloadFromDisk).toHaveBeenCalledWith(['requests/get-user.yaml']);
    expect(environments.refresh).toHaveBeenCalledWith(['requests/get-user.yaml']);
    expect(git.refresh).toHaveBeenCalled();
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

  it('stops listening when the app is destroyed', () => {
    const { stop } = setup();

    TestBed.resetTestingModule();

    expect(stop).toHaveBeenCalled();
  });
});
