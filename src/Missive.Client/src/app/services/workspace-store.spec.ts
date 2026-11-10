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

  it('removes a workspace from the recent list after the host confirms', async () => {
    store.recent.set([
      { name: 'api', path: '/home/me/api' },
      { name: 'other', path: '/home/me/other' },
    ]);
    respond({ 'workspace.recent.remove': () => null });

    await store.removeRecent('/home/me/other');

    expect(send).toHaveBeenCalledWith('workspace.recent.remove', { path: '/home/me/other' });
    expect(store.recent()).toEqual([{ name: 'api', path: '/home/me/api' }]);
  });

  it('keeps the recent list and reports a removal error', async () => {
    store.recent.set([{ name: 'other', path: '/home/me/other' }]);
    respond({
      'workspace.recent.remove': () =>
        Promise.reject(new BridgeError('settings.save_failed', 'Read-only')),
    });

    await store.removeRecent('/home/me/other');

    expect(store.recent()).toEqual([{ name: 'other', path: '/home/me/other' }]);
    expect(store.error()).toBe('Read-only');
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
    expect(store.activeDocument()?.response).toEqual({
      kind: 'response',
      response,
      sendId: 'send-1',
    });
    expect(store.activeDocument()?.sendId).toBeNull();

    respond({ 'request.send': () => Promise.reject(new BridgeError('network', 'Refused')) });
    await store.sendActive();
    expect(store.activeDocument()?.response).toEqual({
      kind: 'error',
      code: 'network',
      message: 'Refused',
    });
  });

  it('saves the last response body under the id it was sent with', async () => {
    const response = { status: 200, statusText: 'OK', headers: [], body: '', size: 0 };
    respond({
      'request.send': () => response,
      'response.saveBody': () => ({ path: '/home/me/users.json' }),
    });
    store.newRequest();
    const id = store.activeId()!;
    expect(await store.saveResponseBody(id)).toBeNull();

    await store.sendActive();
    expect(await store.saveResponseBody(id)).toBe('/home/me/users.json');
    expect(send).toHaveBeenLastCalledWith('response.saveBody', { id: 'send-1' });

    respond({ 'response.saveBody': () => Promise.reject(new BridgeError('not_found', 'Gone')) });
    expect(await store.saveResponseBody(id)).toBeNull();
    expect(store.error()).toContain('Gone');
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

  it('duplicates a request and opens the copy', async () => {
    const copied: WorkspaceDto = {
      ...workspace,
      nodes: [
        {
          ...workspace.nodes[0],
          children: [
            ...workspace.nodes[0].children!,
            { path: 'users/get-user-copy.yaml', name: 'Get user copy', kind: 'request' },
          ],
        },
      ],
    };
    respond({
      'workspace.openLast': () => workspace,
      'request.duplicate': () => ({
        path: 'users/get-user-copy.yaml',
        request: { ...request, name: 'Get user copy' },
      }),
      'workspace.tree': () => copied,
      'request.load': () => ({ ...request, name: 'Get user copy' }),
    });
    await store.restore();
    await store.duplicateRequest('users/get.yaml');

    expect(send).toHaveBeenCalledWith('request.duplicate', { path: 'users/get.yaml' });
    expect(store.activeDocument()).toMatchObject({
      path: 'users/get-user-copy.yaml',
      name: 'Get user copy',
      dirty: false,
    });
  });

  it('duplicates a folder and reloads the tree', async () => {
    respond({
      'workspace.openLast': () => workspace,
      'folder.duplicate': () => ({ path: 'users copy' }),
      'workspace.tree': () => workspace,
    });
    await store.restore();
    expect(await store.duplicateFolder('users')).toBe('users copy');
    expect(send).toHaveBeenLastCalledWith('workspace.tree');
  });

  it('loads the recent workspaces when restoring', async () => {
    const recent = [{ name: 'api', path: '/home/me/api' }];
    respond({ 'workspace.openLast': () => null, 'workspace.recent': () => recent });
    await store.restore();
    expect(store.recent()).toEqual(recent);
  });

  it('opens a recent workspace, closing the open documents', async () => {
    const other: WorkspaceDto = { name: 'other', root: '/home/me/other', nodes: [] };
    respond({
      'workspace.openLast': () => workspace,
      'workspace.recent': () => [],
      'workspace.open': () => other,
      'request.load': () => request,
    });
    await store.restore();
    await store.openRequest({ path: 'users/get.yaml', name: 'Get user', kind: 'request' });

    await store.openRecent('/home/me/other');

    expect(send).toHaveBeenCalledWith('workspace.open', { path: '/home/me/other' });
    expect(store.workspace()).toEqual({ name: 'other', root: '/home/me/other' });
    expect(store.documents()).toHaveLength(0);
    expect(send).toHaveBeenLastCalledWith('workspace.recent');
  });

  it('reports a recent workspace that can no longer be opened', async () => {
    respond({
      'workspace.open': () => Promise.reject(new BridgeError('not_found', 'Gone')),
      'workspace.recent': () => [],
    });
    await store.openRecent('/home/me/gone');
    expect(store.error()).toBe('Gone');
    expect(store.workspace()).toBeNull();
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

  it('asks before keeping a request local-only, then re-reads the tree', async () => {
    const confirm = vi.fn().mockReturnValue(false);
    vi.stubGlobal('confirm', confirm);
    respond({
      'sharing.set': (payload) => payload,
      'workspace.tree': () => workspace,
    });
    const node = { path: 'users/get.yaml', name: 'Get user', kind: 'request' as const };

    await store.setRequestLocalOnly(node, true);
    expect(confirm).toHaveBeenCalledOnce();
    expect(send).not.toHaveBeenCalled();

    confirm.mockReturnValue(true);
    await store.setRequestLocalOnly(node, true);
    expect(send).toHaveBeenCalledWith('sharing.set', { path: 'users/get.yaml', localOnly: true });
    expect(store.sharingChanged()).toBe(1);
  });

  it('shares a file again without asking, and reports failures', async () => {
    const confirm = vi.fn();
    vi.stubGlobal('confirm', confirm);
    respond({ 'sharing.set': () => Promise.reject(new BridgeError('io_error', 'Read-only')) });

    expect(await store.setLocalOnly('environments/dev.yaml', 'Dev', false)).toBe(false);
    expect(confirm).not.toHaveBeenCalled();
    expect(store.error()).toContain('Read-only');
    expect(store.sharingChanged()).toBe(0);
  });
});
