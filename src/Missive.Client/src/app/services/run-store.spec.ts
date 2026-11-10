import { TestBed } from '@angular/core/testing';
import { Mock } from 'vitest';
import { ERROR_CODES, RunResultDto } from '../models/messages';
import { Bridge, BridgeError } from './bridge';
import { RunStore, isRunPlanDirty } from './run-store';
import { WorkspaceStore } from './workspace-store';

const result = (patch: Partial<RunResultDto> = {}): RunResultDto =>
  ({ runId: 'r1', state: 'completed', ...patch }) as RunResultDto;

describe('RunStore', () => {
  let send: Mock;
  let handlers: Record<string, (payload: any) => unknown>;
  let store: RunStore;
  let workspace: WorkspaceStore;

  beforeEach(async () => {
    handlers = {
      'runPlan.list': () => [{ path: 'a.run.yaml', name: 'A', error: null }],
      'runPlan.open': (p) => ({ path: p.path, yaml: 'name: A\n', error: null }),
      'runPlan.save': (p) => ({ path: p.path, yaml: p.yaml, error: null }),
      'run.start': () => ({ runId: 'r1' }),
      'run.progress': () => result(),
      'run.list': () => [],
      'runPlan.delete': () => null,
    };
    send = vi.fn(async (type: string, payload: unknown) => {
      const handler = handlers[type];
      if (!handler) throw new Error(`Unexpected message ${type}`);
      return handler(payload);
    });
    TestBed.configureTestingModule({
      providers: [{ provide: Bridge, useValue: { isDesktop: true, send } }],
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
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    await store.open('a.run.yaml');
    store.edit(key(), 'name: B\n');

    store.close(key());
    expect(store.drafts()).toHaveLength(1);

    confirm.mockReturnValue(true);
    store.close(key());
    expect(store.drafts()).toEqual([]);
    expect(workspace.tabs()).toEqual([]);
    confirm.mockRestore();
  });

  it('keeps a running plan open and its history shut', async () => {
    await store.open('a.run.yaml');
    store.drafts.update((drafts) =>
      drafts.map((d) => ({ ...d, run: result({ state: 'running' }) })),
    );

    store.close(key());
    await store.show(key(), 'old-run');

    expect(store.drafts()).toHaveLength(1);
    expect(store.draft()?.run?.runId).toBe('r1');
    expect(store.draft()?.failure).toContain('Stop the running plan');
    expect(send).not.toHaveBeenCalledWith('run.progress', { runId: 'old-run' });
  });

  it('deletes a plan after confirming and closes its tab', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    await store.open('a.run.yaml');
    handlers['runPlan.list'] = () => [];

    await store.delete('a.run.yaml');

    expect(confirm).toHaveBeenCalled();
    expect(send).toHaveBeenCalledWith('runPlan.delete', { path: 'a.run.yaml' });
    expect(store.drafts()).toEqual([]);
    expect(workspace.tabs()).toEqual([]);
    expect(store.plans()).toEqual([]);
    confirm.mockRestore();
  });

  it('keeps the plan when the deletion is not confirmed', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);

    await store.delete('a.run.yaml');

    expect(send).not.toHaveBeenCalledWith('runPlan.delete', expect.anything());
    confirm.mockRestore();
  });

  it('does not delete the open plan while it is running', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    await store.open('a.run.yaml');
    store.drafts.update((drafts) =>
      drafts.map((d) => ({ ...d, run: result({ state: 'running' }) })),
    );

    await store.delete('a.run.yaml');

    expect(confirm).not.toHaveBeenCalled();
    expect(send).not.toHaveBeenCalledWith('runPlan.delete', expect.anything());
    expect(store.draft()?.failure).toContain('Stop the running plan');
    confirm.mockRestore();
  });
});
