import { TestBed } from '@angular/core/testing';
import { Mock } from 'vitest';
import { RunResultDto } from '../models/messages';
import { Bridge } from './bridge';
import { RunStore } from './run-store';
import { WorkspaceStore } from './workspace-store';

const result = (patch: Partial<RunResultDto> = {}): RunResultDto =>
  ({ runId: 'r1', state: 'completed', ...patch }) as RunResultDto;

describe('RunStore', () => {
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
});
