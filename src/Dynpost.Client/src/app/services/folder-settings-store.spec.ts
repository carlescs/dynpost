import { TestBed } from '@angular/core/testing';
import { Mock } from 'vitest';
import { FolderSettingsDto, WorkspaceDto } from '../models/messages';
import { Bridge, BridgeError } from './bridge';
import { Dialogs } from './dialogs';
import { FolderSettingsStore } from './folder-settings-store';
import { WorkspaceStore } from './workspace-store';

const workspace = { name: 'api', root: '/home/me/api', nodes: [] } as unknown as WorkspaceDto;

const settings: FolderSettingsDto = {
  headers: [{ key: 'Accept', value: 'application/json', enabled: true }],
  auth: { type: 'bearer', token: '{{token}}' },
};

describe('FolderSettingsStore', () => {
  let send: Mock;
  let dialogs: { confirm: Mock };
  let store: FolderSettingsStore;
  let workspaceStore: WorkspaceStore;

  /** Answers bridge messages by type. */
  function respond(handlers: Record<string, (payload: any) => unknown>): void {
    send.mockImplementation(async (type: string, payload: unknown) => {
      const handler = handlers[type];
      if (!handler) throw new Error(`Unexpected message ${type}`);
      return handler(payload);
    });
  }

  beforeEach(async () => {
    send = vi.fn();
    dialogs = { confirm: vi.fn().mockResolvedValue(true) };
    TestBed.configureTestingModule({
      providers: [
        { provide: Bridge, useValue: { isDesktop: true, send } },
        { provide: Dialogs, useValue: dialogs },
      ],
    });
    store = TestBed.inject(FolderSettingsStore);
    workspaceStore = TestBed.inject(WorkspaceStore);
    respond({ 'workspace.openLast': () => workspace, 'workspace.recent': () => [] });
    await workspaceStore.restore();
    TestBed.tick();
  });

  it('opens a folder in a tab, once', async () => {
    respond({ 'folder.settings.load': () => settings });

    await store.open('requests/users');
    await store.open('requests/users');

    expect(send).toHaveBeenCalledTimes(1 + 2 /* restore */);
    const draft = store.draft()!;
    expect(draft.path).toBe('requests/users');
    expect(draft.headers).toEqual(settings.headers);
    expect(draft.auth).toEqual(expect.objectContaining({ type: 'bearer', token: '{{token}}' }));
    expect(workspaceStore.tabs()).toEqual([draft.key]);
    expect(store.nameOf('requests/users')).toBe('users');
    expect(store.nameOf('requests')).toBe('All requests');
  });

  it('saves only the fields of the chosen auth type and tells editors to re-read', async () => {
    respond({
      'folder.settings.load': () => ({ headers: [], auth: { type: 'inherit' } }),
      'folder.settings.save': ({ settings }) => settings,
    });
    await store.open('requests');
    const key = store.draft()!.key;
    store.update(key, {
      auth: { ...store.draft()!.auth, type: 'apiKey', key: 'X-Key', value: 'k' },
    });
    expect(store.draft()!.dirty).toBe(true);

    await store.save(key);

    expect(send).toHaveBeenCalledWith('folder.settings.save', {
      path: 'requests',
      settings: {
        headers: [],
        auth: { type: 'apiKey', key: 'X-Key', value: 'k', in: 'header' },
        variables: [],
      },
    });
    expect(store.draft()!.dirty).toBe(false);
    expect(store.changed()).toBe(1);
  });

  it('loads and saves the variables a folder sets', async () => {
    const variables = [{ key: 'page', value: '1', enabled: true }];
    respond({
      'folder.settings.load': () => ({ headers: [], auth: { type: 'inherit' }, variables }),
      'folder.settings.save': ({ settings }) => settings,
    });
    await store.open('requests/users');
    const key = store.draft()!.key;
    expect(store.draft()!.variables).toEqual(variables);

    store.update(key, { variables: [...variables, { key: 'size', value: '20', enabled: false }] });
    await store.save(key);

    expect(send).toHaveBeenCalledWith('folder.settings.save', {
      path: 'requests/users',
      settings: {
        headers: [],
        auth: { type: 'inherit' },
        variables: [...variables, { key: 'size', value: '20', enabled: false }],
      },
    });
    expect(store.draft()!.variables).toHaveLength(2);
  });

  it('reports a failed save and keeps the changes', async () => {
    respond({
      'folder.settings.load': () => settings,
      'folder.settings.save': () => Promise.reject(new BridgeError('invalid_request', 'Duplicate')),
    });
    await store.open('requests/users');
    const key = store.draft()!.key;
    store.update(key, { headers: [] });

    await store.save(key);

    expect(store.draft()!.dirty).toBe(true);
    expect(workspaceStore.error()).toBe('Duplicate');
  });

  it('follows renamed folders and closes deleted ones', async () => {
    respond({ 'folder.settings.load': () => settings });
    await store.open('requests/users/admin');

    workspaceStore.moved.set({ from: 'requests/users', to: 'requests/people' });
    TestBed.tick();
    expect(store.drafts()[0].path).toBe('requests/people/admin');

    workspaceStore.folderDeleted.set({ path: 'requests/people' });
    TestBed.tick();
    expect(store.drafts()).toEqual([]);
    expect(workspaceStore.tabs()).toEqual([]);
  });

  it('asks before closing a tab with unsaved changes', async () => {
    respond({ 'folder.settings.load': () => settings });
    await store.open('requests/users');
    const key = store.draft()!.key;
    store.update(key, { headers: [] });
    dialogs.confirm.mockResolvedValue(false);

    await store.close(key);
    expect(store.drafts().length).toBe(1);

    dialogs.confirm.mockResolvedValue(true);
    await store.close(key);
    expect(store.drafts()).toEqual([]);
  });

  it('re-reads clean drafts whose file changed on disk', async () => {
    respond({ 'folder.settings.load': () => settings });
    await store.open('requests/users');
    respond({ 'folder.settings.load': () => ({ headers: [], auth: { type: 'none' } }) });

    workspaceStore.reloaded.set({ paths: ['requests/users/get.yaml'] });
    TestBed.tick();
    await Promise.resolve();
    expect(store.draft()!.headers).toEqual(settings.headers);

    workspaceStore.reloaded.set({ paths: ['requests/users/_folder.yaml'] });
    TestBed.tick();
    await new Promise((resolve) => setTimeout(resolve));
    expect(store.draft()!.headers).toEqual([]);
    expect(store.draft()!.auth.type).toBe('none');
    expect(store.changed()).toBe(1);
  });

  it('reads what a folder passes on, or null when it cannot', async () => {
    const inherited = { headers: [], auth: { type: 'none' }, authFolder: null, variables: [] };
    respond({ 'folder.settings.inherited': () => inherited });
    expect(await store.inherited('requests/users')).toEqual(inherited);
    expect(send).toHaveBeenLastCalledWith('folder.settings.inherited', { path: 'requests/users' });

    respond({ 'folder.settings.inherited': () => Promise.reject(new Error('bad yaml')) });
    expect(await store.inherited('requests/users')).toBeNull();
  });
});
