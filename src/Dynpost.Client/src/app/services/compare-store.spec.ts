import { TestBed } from '@angular/core/testing';
import { Mock } from 'vitest';
import { RequestDto, WorkspaceDto } from '../models/messages';
import { Bridge, BridgeError } from './bridge';
import { CompareItem, CompareStore } from './compare-store';
import { Dialogs } from './dialogs';
import { RunStore } from './run-store';
import { WorkspaceStore } from './workspace-store';

const workspace = { name: 'api', root: '/home/me/api', nodes: [] } as unknown as WorkspaceDto;

const request = (name: string, url: string): RequestDto => ({
  name,
  method: 'GET',
  url,
  params: [],
  headers: [],
  auth: { type: 'none' },
  body: { type: 'none', content: '' },
});

const files: Record<string, RequestDto> = {
  'requests/a.yaml': request('A', 'https://a.test'),
  'requests/b.yaml': request('B', 'https://b.test'),
};
const plans: Record<string, string> = {
  'plans/one.run.yaml': 'name: One\nrequests:\n  - requests/a.yaml\nload:\n  threads: 5\n',
  'plans/two.run.yaml': 'name: Two\nrequests:\n  - requests/a.yaml\nload:\n  threads: 10\n',
};

const itemA: CompareItem = { kind: 'request', path: 'requests/a.yaml', name: 'A' };
const itemB: CompareItem = { kind: 'request', path: 'requests/b.yaml', name: 'B' };
const planOne: CompareItem = { kind: 'plan', path: 'plans/one.run.yaml', name: 'One' };
const planTwo: CompareItem = { kind: 'plan', path: 'plans/two.run.yaml', name: 'Two' };

describe('CompareStore', () => {
  let send: Mock;
  let store: CompareStore;
  let workspaceStore: WorkspaceStore;

  beforeEach(async () => {
    send = vi.fn(async (type: string, payload: any) => {
      switch (type) {
        case 'workspace.openLast':
          return workspace;
        case 'workspace.recent':
        case 'runPlan.list':
        case 'runFolder.list':
        case 'run.list':
          return [];
        case 'request.load': {
          const file = files[payload.path];
          if (!file) throw new BridgeError('not_found', 'File not found.');
          return structuredClone(file);
        }
        case 'request.viewFile':
          return `name: ${payload.request.name}\nurl: ${payload.request.url}\n`;
        case 'runPlan.open':
          return { path: payload.path, yaml: plans[payload.path], error: null };
        default:
          return null;
      }
    });
    TestBed.configureTestingModule({
      providers: [
        { provide: Bridge, useValue: { isDesktop: true, send, on: () => () => {} } },
        { provide: Dialogs, useValue: { confirm: vi.fn().mockResolvedValue(true) } },
      ],
    });
    store = TestBed.inject(CompareStore);
    workspaceStore = TestBed.inject(WorkspaceStore);
    await workspaceStore.restore();
    TestBed.tick();
  });

  it('compares two requests', async () => {
    await store.compare(itemA, itemB);

    const c = store.comparison()!;
    expect(c.kind).toBe('request');
    expect(c.left).toEqual(
      expect.objectContaining({ path: 'requests/a.yaml', name: 'A', unsaved: false }),
    );
    expect(c.left.request?.url).toBe('https://a.test');
    expect(c.left.text).toBe('name: A\nurl: https://a.test\n');
    expect(c.right.request?.url).toBe('https://b.test');
    expect(send).toHaveBeenCalledWith('request.viewFile', {
      request: files['requests/a.yaml'],
      path: 'requests/a.yaml',
    });
  });

  it('compares the unsaved edits of an open request', async () => {
    await workspaceStore.openRequest({ path: 'requests/a.yaml', name: 'A', kind: 'request' });
    const doc = workspaceStore.activeDocument()!;
    workspaceStore.update(doc.id, { url: 'https://edited.test' });
    TestBed.tick();

    await store.compare(itemA, itemB);

    const left = store.comparison()!.left;
    expect(left.unsaved).toBe(true);
    expect(left.request?.url).toBe('https://edited.test');
    expect(left.text).toContain('https://edited.test');
  });

  it('reads run plans into their fields', async () => {
    await store.compare(planOne, planTwo);

    const c = store.comparison()!;
    expect(c.kind).toBe('plan');
    expect(c.left.name).toBe('One');
    expect(c.left.plan?.threads).toBe('5');
    expect(c.right.plan?.threads).toBe('10');
    expect(c.left.text).toBe(plans['plans/one.run.yaml']);
  });

  it('swaps the sides', async () => {
    await store.compare(itemA, itemB);

    store.swap();

    expect(store.comparison()!.left.path).toBe('requests/b.yaml');
    expect(store.comparison()!.right.path).toBe('requests/a.yaml');
  });

  it('reads the sides again when their files change', async () => {
    await store.compare(itemA, itemB);
    files['requests/b.yaml'] = request('B', 'https://changed.test');

    await store.reload(['requests/other.yaml']);
    expect(store.comparison()!.right.request?.url).toBe('https://b.test');

    await store.reload(['requests/b.yaml']);
    expect(store.comparison()!.right.request?.url).toBe('https://changed.test');
    files['requests/b.yaml'] = request('B', 'https://b.test');
  });

  it("reports a file that can't be read and shows no comparison", async () => {
    await store.compare(itemA, { kind: 'request', path: 'requests/gone.yaml', name: 'Gone' });

    expect(store.comparison()).toBeNull();
    expect(store.open()).toBe(false);
    expect(workspaceStore.error()).toBe('File not found.');
  });

  it('closes when a tab is shown', async () => {
    await store.compare(itemA, itemB);
    expect(store.open()).toBe(true);

    await workspaceStore.openRequest({ path: 'requests/a.yaml', name: 'A', kind: 'request' });
    TestBed.tick();

    expect(store.comparison()).toBeNull();
  });
});
