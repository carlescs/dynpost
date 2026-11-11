import { TestBed } from '@angular/core/testing';
import { Mock } from 'vitest';
import { ERROR_CODES, RunResultDto, RunSummaryDto } from '../models/messages';
import { Bridge, BridgeError } from './bridge';
import { RunStore, isRunPlanDirty } from './run-store';
import { WorkspaceStore } from './workspace-store';
import { Dialogs } from './dialogs';

const result = (patch: Partial<RunResultDto> = {}): RunResultDto =>
  ({ runId: 'r1', state: 'completed', ...patch }) as RunResultDto;

describe('RunStore', () => {
  let dialogs: { confirm: Mock };
  beforeEach(() => (dialogs = { confirm: vi.fn().mockResolvedValue(true) }));
  let send: Mock;
  let handlers: Record<string, (payload: any) => unknown>;
  let store: RunStore;
  let workspace: WorkspaceStore;

  beforeEach(async () => {
    handlers = {
      'runPlan.list': () => [{ path: 'a.run.yaml', name: 'A', error: null }],
      'runFolder.list': () => [],
      'workspace.tree': () => ({ name: 'api', root: '/r', nodes: [] }),
      'runPlan.open': (p) => ({ path: p.path, yaml: 'name: A\n', error: null }),
      'runPlan.save': (p) => ({ path: p.path, yaml: p.yaml, error: null }),
      'run.start': () => ({ runId: 'r1' }),
      'run.progress': () => result(),
      'run.list': () => [],
      'runPlan.delete': () => null,
      'run.delete': () => null,
      'run.clear': () => null,
    };
    send = vi.fn(async (type: string, payload: unknown) => {
      const handler = handlers[type];
      if (!handler) throw new Error(`Unexpected message ${type}`);
      return handler(payload);
    });
    TestBed.configureTestingModule({
      providers: [
        { provide: Bridge, useValue: { isDesktop: true, send } },
        { provide: Dialogs, useValue: dialogs },
      ],
    });
    store = TestBed.inject(RunStore);
    workspace = TestBed.inject(WorkspaceStore);
    workspace.workspace.set({ name: 'api', root: '/r' } as never);
    TestBed.tick();
    await vi.waitFor(() => expect(store.plans().length).toBe(1));
  });

  const key = () => store.draft()!.key;
  const dirty = () => isRunPlanDirty(store.draft()!);

  it('opens a plan in a tab and tracks unsaved edits', async () => {
    await store.open('a.run.yaml');
    expect(workspace.tabs()).toEqual([key()]);
    expect(workspace.activeTab()).toBe(key());
    expect(dirty()).toBe(false);
    store.edit(key(), 'name: B\n');
    expect(dirty()).toBe(true);
  });

  it('opens several plans in their own tabs', async () => {
    await store.open('a.run.yaml');
    const first = key();
    await store.open('b.run.yaml');

    expect(store.drafts().map((d) => d.path)).toEqual(['a.run.yaml', 'b.run.yaml']);
    expect(workspace.tabs()).toHaveLength(2);
    expect(store.draft()?.path).toBe('b.run.yaml');

    workspace.activateTab(first);
    expect(store.draft()?.path).toBe('a.run.yaml');
  });

  it('shows the open tab when opening the same plan again', async () => {
    await store.open('a.run.yaml');
    const first = key();
    store.edit(first, 'name: Unsaved\n');
    await store.open('b.run.yaml');

    await store.open('a.run.yaml');

    expect(workspace.activeTab()).toBe(first);
    expect(store.draft()?.yaml).toBe('name: Unsaved\n');
    expect(send.mock.calls.filter(([type]) => type === 'runPlan.open')).toHaveLength(2);
  });

  it('is not the active editor while a request tab is active', async () => {
    await store.open('a.run.yaml');
    workspace.openTab('request');

    expect(store.draft()).toBeNull();
  });

  it('saves pending edits before starting and shows the result', async () => {
    await store.open('a.run.yaml');
    store.edit(key(), 'name: B\n');
    await store.start(key());
    expect(send).toHaveBeenCalledWith('runPlan.save', { path: 'a.run.yaml', yaml: 'name: B\n' });
    expect(dirty()).toBe(false);
    expect(store.draft()?.run?.state).toBe('completed');
    expect(store.draft()?.starting).toBe(false);
  });

  it('keeps each plan’s run in its own tab', async () => {
    await store.open('a.run.yaml');
    const first = key();
    await store.start(first);
    await store.open('b.run.yaml');

    expect(store.draft()?.run).toBeNull();
    expect(store.drafts().find((d) => d.key === first)?.run?.runId).toBe('r1');
  });

  it('asks before a very large run', async () => {
    handlers['run.start'] = () => {
      throw new BridgeError(ERROR_CODES.confirmationRequired, 'Really?');
    };
    await store.open('a.run.yaml');

    await store.start(key());

    expect(store.draft()?.confirmation).toBe('Really?');
  });

  it('keeps normalized YAML returned by save as the current draft', async () => {
    handlers['runPlan.save'] = (p) => ({
      path: p.path,
      yaml: p.yaml.endsWith('\n') ? p.yaml : `${p.yaml}\n`,
      error: null,
    });
    await store.open('a.run.yaml');
    store.edit(key(), 'name: B');

    await store.save(key());

    expect(store.draft()?.yaml).toBe('name: B\n');
    expect(dirty()).toBe(false);
  });

  it('follows the file when saving renames it', async () => {
    handlers['runPlan.save'] = (p) => ({ path: 'renamed.run.yaml', yaml: p.yaml, error: null });
    await store.open('a.run.yaml');
    const tab = key();
    store.edit(tab, 'name: Renamed\n');

    await store.save(tab);

    expect(store.draft()?.path).toBe('renamed.run.yaml');
    expect(workspace.activeTab()).toBe(tab);
    expect(dirty()).toBe(false);
  });

  it('preserves edits made while a save is in flight', async () => {
    let finishSave!: (value: { path: string; yaml: string; error: null }) => void;
    handlers['runPlan.save'] = () =>
      new Promise((resolve) => {
        finishSave = resolve;
      });
    await store.open('a.run.yaml');
    store.edit(key(), 'name: B\n');

    const saving = store.save(key());
    store.edit(key(), 'name: C\n');
    finishSave({ path: 'a.run.yaml', yaml: 'name: B\n', error: null });
    await saving;

    expect(store.draft()?.yaml).toBe('name: C\n');
    expect(dirty()).toBe(true);
  });

  it('does not reset the drafts when the workspace object refreshes at the same root', async () => {
    await store.open('a.run.yaml');
    store.edit(key(), 'name: Unsaved\n');

    workspace.workspace.set({ name: 'refreshed', root: '/r' } as never);
    TestBed.tick();

    expect(store.draft()?.yaml).toBe('name: Unsaved\n');
  });

  it('closes the tabs when another folder is opened', async () => {
    await store.open('a.run.yaml');

    workspace.workspace.set({ name: 'other', root: '/o' } as never);
    TestBed.tick();

    expect(store.drafts()).toEqual([]);
    expect(workspace.tabs()).toEqual([]);
  });

  it('closes a tab, asking first when it has unsaved changes', async () => {
    const confirm = (dialogs.confirm = vi.fn().mockResolvedValue(false));
    await store.open('a.run.yaml');
    store.edit(key(), 'name: B\n');

    await store.close(key());
    expect(store.drafts()).toHaveLength(1);

    confirm.mockResolvedValue(true);
    await store.close(key());
    expect(store.drafts()).toEqual([]);
    expect(workspace.tabs()).toEqual([]);
  });

  it('keeps a running plan open and its history shut', async () => {
    await store.open('a.run.yaml');
    store.drafts.update((drafts) =>
      drafts.map((d) => ({ ...d, run: result({ state: 'running' }) })),
    );

    await store.close(key());
    await store.show(key(), 'old-run');

    expect(store.drafts()).toHaveLength(1);
    expect(store.draft()?.run?.runId).toBe('r1');
    expect(store.draft()?.failure).toContain('Stop the running plan');
    expect(send).not.toHaveBeenCalledWith('run.progress', { runId: 'old-run' });
  });

  it('deletes a plan after confirming and closes its tab', async () => {
    const confirm = (dialogs.confirm = vi.fn().mockResolvedValue(true));
    await store.open('a.run.yaml');
    handlers['runPlan.list'] = () => [];

    await store.delete('a.run.yaml');

    expect(confirm).toHaveBeenCalled();
    expect(send).toHaveBeenCalledWith('runPlan.delete', { path: 'a.run.yaml' });
    expect(store.drafts()).toEqual([]);
    expect(workspace.tabs()).toEqual([]);
    expect(store.plans()).toEqual([]);
  });

  it('keeps the plan when the deletion is not confirmed', async () => {
    const confirm = (dialogs.confirm = vi.fn().mockResolvedValue(false));

    await store.delete('a.run.yaml');

    expect(send).not.toHaveBeenCalledWith('runPlan.delete', expect.anything());
  });

  it('does not delete the open plan while it is running', async () => {
    const confirm = (dialogs.confirm = vi.fn().mockResolvedValue(true));
    await store.open('a.run.yaml');
    store.drafts.update((drafts) =>
      drafts.map((d) => ({ ...d, run: result({ state: 'running' }) })),
    );

    await store.delete('a.run.yaml');

    expect(confirm).not.toHaveBeenCalled();
    expect(send).not.toHaveBeenCalledWith('runPlan.delete', expect.anything());
    expect(store.draft()?.failure).toContain('Stop the running plan');
  });

  it('deletes an earlier run after confirming and empties the dashboard that showed it', async () => {
    await store.open('a.run.yaml');
    const summary = { runId: 'r1', startedAt: '2026-01-02T03:04:05Z' } as RunSummaryDto;
    store.drafts.update((drafts) =>
      drafts.map((d) => ({ ...d, run: result(), history: [summary] })),
    );

    await store.deleteRun(key(), 'r1');

    expect(dialogs.confirm).toHaveBeenCalled();
    expect(send).toHaveBeenCalledWith('run.delete', { runId: 'r1' });
    expect(send).toHaveBeenLastCalledWith('run.list', { path: 'a.run.yaml' });
    expect(store.draft()?.run).toBeNull();
    expect(store.draft()?.history).toEqual([]);
  });

  it('keeps the run when its deletion is not confirmed', async () => {
    dialogs.confirm = vi.fn().mockResolvedValue(false);
    await store.open('a.run.yaml');

    await store.deleteRun(key(), 'r1');

    expect(send).not.toHaveBeenCalledWith('run.delete', expect.anything());
  });

  it('clears the history of a plan after confirming', async () => {
    await store.open('a.run.yaml');
    store.drafts.update((drafts) => drafts.map((d) => ({ ...d, run: result() })));

    await store.clearHistory(key());

    expect(send).toHaveBeenCalledWith('run.clear', { path: 'a.run.yaml' });
    expect(store.draft()?.run).toBeNull();
  });

  it('does not delete runs while the plan is running', async () => {
    await store.open('a.run.yaml');
    store.drafts.update((drafts) =>
      drafts.map((d) => ({ ...d, run: result({ state: 'running' }) })),
    );

    await store.deleteRun(key(), 'r1');
    await store.clearHistory(key());

    expect(dialogs.confirm).not.toHaveBeenCalled();
    expect(send).not.toHaveBeenCalledWith('run.delete', expect.anything());
    expect(send).not.toHaveBeenCalledWith('run.clear', expect.anything());
    expect(store.draft()?.failure).toContain('Stop the running plan');
  });

  describe('folders', () => {
    const running = () =>
      store.drafts.update((drafts) =>
        drafts.map((d) => ({ ...d, run: result({ state: 'running' }) })),
      );

    it('loads the folders with the plans', async () => {
      handlers['runFolder.list'] = () => ['perf'];

      await store.refresh();

      expect(store.folders()).toEqual(['perf']);
    });

    it('creates a plan in a folder', async () => {
      handlers['runPlan.list'] = () => [{ path: 'perf/plan-1.run.yaml', name: 'P', error: null }];
      await store.refresh();

      await store.create('perf');

      expect(send).toHaveBeenCalledWith('runPlan.save', {
        path: 'perf/plan-2.run.yaml',
        yaml: expect.stringContaining('name: Plan 2'),
      });
    });

    it('creates a folder and reloads the plans and the explorer', async () => {
      handlers['folder.create'] = (p) => ({ path: `${p.parent}/${p.name}` });
      send.mockClear();

      expect(await store.createFolder('perf', 'nightly')).toBe(true);

      expect(send).toHaveBeenCalledWith('folder.create', { parent: 'perf', name: 'nightly' });
      expect(send).toHaveBeenCalledWith('runFolder.list');
      expect(send).toHaveBeenCalledWith('workspace.tree');
    });

    it('moves open plans along with a renamed or moved folder', async () => {
      await store.open('perf/a.run.yaml');
      handlers['runFolder.rename'] = () => ({ path: 'load' });
      handlers['runFolder.move'] = () => ({ path: 'archive/load' });

      await store.renameFolder('perf', 'load');
      expect(store.draft()?.path).toBe('load/a.run.yaml');

      await store.moveFolder('load', 'archive');
      expect(store.draft()?.path).toBe('archive/load/a.run.yaml');
    });

    it('moves a plan into a folder and its tab follows', async () => {
      await store.open('a.run.yaml');
      handlers['runPlan.move'] = () => ({ path: 'perf/a.run.yaml' });

      expect(await store.movePlan('a.run.yaml', 'perf')).toBe(true);

      expect(send).toHaveBeenCalledWith('runPlan.move', { path: 'a.run.yaml', folder: 'perf' });
      expect(store.draft()?.path).toBe('perf/a.run.yaml');
    });

    it('shows why a folder action was refused', async () => {
      handlers['runFolder.duplicate'] = () => {
        throw new BridgeError(ERROR_CODES.invalidRequest, "'shop' also holds requests");
      };

      expect(await store.duplicateFolder('shop')).toBe(false);

      expect(workspace.error()).toContain('also holds requests');
    });

    it('deletes a folder after confirming and closes the tabs of its plans', async () => {
      handlers['runPlan.list'] = () => [{ path: 'perf/a.run.yaml', name: 'A', error: null }];
      await store.refresh();
      await store.open('perf/a.run.yaml');
      handlers['runFolder.delete'] = () => null;

      expect(await store.deleteFolder('perf')).toBe(true);

      expect(dialogs.confirm).toHaveBeenCalledWith(
        expect.stringContaining('the 1 run plan(s) in it'),
        expect.anything(),
      );
      expect(send).toHaveBeenCalledWith('runFolder.delete', { path: 'perf' });
      expect(store.drafts()).toEqual([]);
    });

    it('keeps a folder whose deletion is not confirmed', async () => {
      dialogs.confirm.mockResolvedValue(false);

      expect(await store.deleteFolder('perf')).toBe(false);

      expect(send).not.toHaveBeenCalledWith('runFolder.delete', expect.anything());
    });

    it('leaves a folder with a running plan alone', async () => {
      await store.open('perf/a.run.yaml');
      running();

      expect(await store.renameFolder('perf', 'load')).toBe(false);
      expect(await store.moveFolder('perf', 'archive')).toBe(false);
      expect(await store.deleteFolder('perf')).toBe(false);
      expect(await store.movePlan('perf/a.run.yaml', '')).toBe(false);

      expect(dialogs.confirm).not.toHaveBeenCalled();
      expect(store.draft()?.failure).toContain('Stop the running plan');
      expect(send).not.toHaveBeenCalledWith('runFolder.rename', expect.anything());
      expect(send).not.toHaveBeenCalledWith('runPlan.move', expect.anything());
    });
  });
});
