import { TestBed } from '@angular/core/testing';
import { Mock } from 'vitest';
import { RequestDto, WorkspaceDto } from '../models/messages';
import { Bridge, BridgeError } from './bridge';
import { SIDEBAR_MAX_WIDTH, SIDEBAR_MIN_WIDTH, WorkspaceStore } from './workspace-store';

const workspace: WorkspaceDto = {
  name: 'api',
  root: '/home/me/api',
  nodes: [
    {
      path: 'users',
      name: 'users',
      kind: 'folder',
      children: [{ path: 'users/get.yaml', name: 'Get user', kind: 'request', method: 'GET' }],
    },
  ],
} as WorkspaceDto;

const request: RequestDto = {
  name: 'Get user',
  method: 'GET',
  url: 'https://example.com/users/1',
  params: [],
  headers: [],
  auth: { type: 'none' },
  body: { type: 'none', content: '' },
};

describe('WorkspaceStore', () => {
  let send: Mock;
  let store: WorkspaceStore;

  beforeEach(() => {
    send = vi.fn();
    TestBed.configureTestingModule({
      providers: [{ provide: Bridge, useValue: { isDesktop: true, send } }],
    });
    store = TestBed.inject(WorkspaceStore);
  });

  afterEach(() => vi.unstubAllGlobals());

  /** Answers bridge messages by type. */
  function respond(handlers: Record<string, (payload: any) => unknown>): void {
    send.mockImplementation(async (type: string, payload: unknown) => {
      const handler = handlers[type];
      if (!handler) throw new Error(`Unexpected message ${type}`);
      return handler(payload);
    });
  }

  it('restores the last workspace', async () => {
    respond({ 'workspace.openLast': () => workspace });
    await store.restore();
    expect(store.workspace()).toEqual({ name: 'api', root: '/home/me/api' });
    expect(store.tree()[0].children?.[0].method).toBe('GET');
  });

  it('starts empty when restoring fails', async () => {
    respond({ 'workspace.openLast': () => Promise.reject(new Error('no settings')) });
    await store.restore();
    expect(store.workspace()).toBeNull();
    expect(store.error()).toBeNull();
  });

  it('opens a request once and focuses its tab when opened again', async () => {
    respond({ 'request.load': () => request });
    const node = { path: 'users/get.yaml', name: 'Get user', kind: 'request' } as const;
    await store.openRequest(node);
    await store.openRequest(node);

    expect(send).toHaveBeenCalledOnce();
    expect(store.documents()).toHaveLength(1);
    const doc = store.activeDocument()!;
    expect(doc).toMatchObject({ path: 'users/get.yaml', folder: 'users', dirty: false });
    expect(store.tabs()).toEqual([doc.key]);
  });

  it('reports bridge errors', async () => {
    respond({ 'request.load': () => Promise.reject(new BridgeError('invalid_yaml', 'Bad YAML')) });
    await store.openRequest({ path: 'bad.yaml', name: 'Bad', kind: 'request' });
    expect(store.error()).toBe('Bad YAML');
    store.dismissError();
    expect(store.error()).toBeNull();
  });

  it('creates new requests as dirty, untitled documents', () => {
    store.newRequest('users');
    expect(store.activeDocument()).toMatchObject({
      name: 'New request 1',
      path: null,
      folder: 'users',
      dirty: true,
    });
  });

  it('marks edits as dirty and counts revisions', () => {
    store.newRequest();
    const id = store.activeId()!;
    store.update(id, { url: 'https://a' });
    store.update(id, { url: 'https://ab' });
    expect(store.activeDocument()).toMatchObject({ url: 'https://ab', dirty: true, revision: 2 });
  });

  it('saves a new request into its folder and reloads the tree', async () => {
    respond({
      'workspace.openLast': () => workspace,
      'request.create': () => ({ path: 'users/new.yaml', request: { ...request, name: 'New' } }),
      'workspace.tree': () => workspace,
    });
    await store.restore();
    store.newRequest('users');
    await store.saveActive();

    expect(send).toHaveBeenCalledWith(
      'request.create',
      expect.objectContaining({ folder: 'users' }),
    );
    expect(store.activeDocument()).toMatchObject({
      id: 'users/new.yaml',
      path: 'users/new.yaml',
      name: 'New',
      dirty: false,
      saving: false,
    });
    expect(send).toHaveBeenLastCalledWith('workspace.tree');
  });

  it('refuses to save without a workspace', async () => {
    store.newRequest();
    await store.saveActive();
    expect(send).not.toHaveBeenCalled();
    expect(store.error()).toContain('Open a workspace folder');
  });

  it('keeps the response, or the send error, with the document', async () => {
    const response = { status: 200, statusText: 'OK', headers: [], body: '', size: 0 };
    respond({ 'request.send': () => response });
    store.newRequest();
    await store.sendActive();
    expect(store.activeDocument()?.response).toEqual({ kind: 'response', response });
    expect(store.activeDocument()?.sendId).toBeNull();

    respond({ 'request.send': () => Promise.reject(new BridgeError('network', 'Refused')) });
    await store.sendActive();
    expect(store.activeDocument()?.response).toEqual({
      kind: 'error',
      code: 'network',
      message: 'Refused',
    });
  });

  it('asks before closing a document with unsaved changes', () => {
    const confirm = vi.fn().mockReturnValue(false);
    vi.stubGlobal('confirm', confirm);
    store.newRequest();
    const id = store.activeId()!;

    store.close(id);
    expect(confirm).toHaveBeenCalledOnce();
    expect(store.documents()).toHaveLength(1);

    confirm.mockReturnValue(true);
    store.close(id);
    expect(store.documents()).toHaveLength(0);
    expect(store.activeTab()).toBeNull();
  });

  it('shows the nearest remaining tab when the active one is removed', () => {
    store.openTab('a');
    store.openTab('b');
    store.openTab('c');
    store.activateTab('b');
    store.removeTabs(['b']);
    expect(store.activeTab()).toBe('c');
    store.removeTabs(['c']);
    expect(store.activeTab()).toBe('a');
    expect(store.tabs()).toEqual(['a']);
  });

  it('keeps open documents attached when their folder is renamed', async () => {
    respond({
      'request.load': () => request,
      'folder.rename': () => ({ path: 'people' }),
    });
    await store.openRequest({ path: 'users/get.yaml', name: 'Get user', kind: 'request' });
    expect(await store.renameFolder('users', 'people')).toBe(true);
    expect(store.activeDocument()).toMatchObject({
      id: 'people/get.yaml',
      path: 'people/get.yaml',
      folder: 'people',
    });
  });

  it('clamps the sidebar width and toggles the sidebar', () => {
    store.setSidebarWidth(10);
    expect(store.sidebarWidth()).toBe(SIDEBAR_MIN_WIDTH);
    store.setSidebarWidth(10_000);
    expect(store.sidebarWidth()).toBe(SIDEBAR_MAX_WIDTH);
    store.setSidebarWidth(300.4);
    expect(store.sidebarWidth()).toBe(300);

    store.toggleSidebar();
    expect(store.sidebarOpen()).toBe(false);
    store.toggleSidebar(true);
    expect(store.sidebarOpen()).toBe(true);
  });
});
