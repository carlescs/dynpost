import { TestBed } from '@angular/core/testing';
import { Mock } from 'vitest';
import { RunResultDto } from '../models/messages';
import { Bridge } from './bridge';
import { RunStore } from './run-store';
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
      providers: [
        { provide: Bridge, useValue: { isDesktop: true, send } },
        { provide: Dialogs, useValue: dialogs },
      ],
    });
    store = TestBed.inject(RunStore);
    TestBed.inject(WorkspaceStore).workspace.set({ name: 'api', root: '/r' } as never);
    TestBed.tick();
    await vi.waitFor(() => expect(store.plans().length).toBe(1));
  });

  it('opens a plan and tracks unsaved edits', async () => {
    await store.open('a.run.yaml');
    expect(store.showing()).toBe(true);
    expect(store.dirty()).toBe(false);
    store.edit('name: B\n');
    expect(store.dirty()).toBe(true);
  });

  it('saves pending edits before starting and shows the result', async () => {
    await store.open('a.run.yaml');
    store.edit('name: B\n');
    await store.start();
    expect(send).toHaveBeenCalledWith('runPlan.save', { path: 'a.run.yaml', yaml: 'name: B\n' });
    expect(store.dirty()).toBe(false);
    expect(store.run()?.state).toBe('completed');
  });

  it('keeps normalized YAML returned by save as the current draft', async () => {
    handlers['runPlan.save'] = (p) => ({
      path: p.path,
      yaml: p.yaml.endsWith('\n') ? p.yaml : `${p.yaml}\n`,
      error: null,
    });
    await store.open('a.run.yaml');
    store.edit('name: B');

    await store.save();

    expect(store.draft()?.yaml).toBe('name: B\n');
    expect(store.dirty()).toBe(false);
  });

  it('follows the file when saving renames it', async () => {
    handlers['runPlan.save'] = (p) => ({ path: 'renamed.run.yaml', yaml: p.yaml, error: null });
    await store.open('a.run.yaml');
    store.edit('name: Renamed\n');

    await store.save();

    expect(store.draft()?.path).toBe('renamed.run.yaml');
    expect(store.dirty()).toBe(false);
  });

  it('preserves edits made while a save is in flight', async () => {
    let finishSave!: (value: { path: string; yaml: string; error: null }) => void;
    handlers['runPlan.save'] = (p) =>
      new Promise((resolve) => {
        finishSave = resolve;
      });
    await store.open('a.run.yaml');
    store.edit('name: B\n');

    const saving = store.save();
    store.edit('name: C\n');
    finishSave({ path: 'a.run.yaml', yaml: 'name: B\n', error: null });
    await saving;

    expect(store.draft()?.yaml).toBe('name: C\n');
    expect(store.dirty()).toBe(true);
  });

  it('reuses the existing draft when opening the same plan', async () => {
    await store.open('a.run.yaml');
    store.edit('name: Unsaved\n');

    await store.open('a.run.yaml');

    expect(store.draft()?.yaml).toBe('name: Unsaved\n');
    expect(send.mock.calls.filter(([type]) => type === 'runPlan.open')).toHaveLength(1);
  });

  it('does not reset the draft when the workspace object refreshes at the same root', async () => {
    await store.open('a.run.yaml');
    store.edit('name: Unsaved\n');
    const workspace = TestBed.inject(WorkspaceStore);

    workspace.workspace.set({ name: 'refreshed', root: '/r' } as never);
    TestBed.tick();

    expect(store.draft()?.yaml).toBe('name: Unsaved\n');
  });

  it('hides the run editor when the currently selected request tab is activated again', async () => {
    const workspace = TestBed.inject(WorkspaceStore);
    workspace.activeTab.set('request');
    await store.open('a.run.yaml');
    expect(store.showing()).toBe(true);

    workspace.activateTab('request');
    TestBed.tick();

    expect(store.showing()).toBe(false);
  });

  it('blocks switching plans or history while a run is active', async () => {
    await store.open('a.run.yaml');
    store.run.set(result({ state: 'running' }));

    await store.open('b.run.yaml');
    await store.show('old-run');

    expect(store.run()?.runId).toBe('r1');
    expect(store.running()).toBe(true);
    expect(send).not.toHaveBeenCalledWith('runPlan.open', { path: 'b.run.yaml' });
    expect(send).not.toHaveBeenCalledWith('run.progress', { runId: 'old-run' });
  });

  it('deletes a plan after confirming and closes it when open', async () => {
    const confirm = (dialogs.confirm = vi.fn().mockResolvedValue(true));
    await store.open('a.run.yaml');
    handlers['runPlan.list'] = () => [];

    await store.delete('a.run.yaml');

    expect(confirm).toHaveBeenCalled();
    expect(send).toHaveBeenCalledWith('runPlan.delete', { path: 'a.run.yaml' });
    expect(store.draft()).toBeNull();
    expect(store.showing()).toBe(false);
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
    store.run.set(result({ state: 'running' }));

    await store.delete('a.run.yaml');

    expect(confirm).not.toHaveBeenCalled();
    expect(send).not.toHaveBeenCalledWith('runPlan.delete', expect.anything());
    expect(store.error()).toContain('Stop the running plan');
  });
});
