import { of } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { Mock } from 'vitest';
import { RequestDto, WorkspaceDto } from '../models/messages';
import { Bridge, BridgeError } from './bridge';
import { SIDEBAR_MAX_WIDTH, SIDEBAR_MIN_WIDTH, WorkspaceStore } from './workspace-store';
import { Dialogs } from './dialogs';

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
  let dialogs: { confirm: Mock };
  beforeEach(() => (dialogs = { confirm: vi.fn().mockResolvedValue(true) }));
  let send: Mock;
  let store: WorkspaceStore;

  beforeEach(() => {
    send = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        { provide: Bridge, useValue: { isDesktop: true, send } },
        { provide: Dialogs, useValue: dialogs },
      ],
    });
    store = TestBed.inject(WorkspaceStore);
  });

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

  it('asks the host to show the workspace folder and reports failures', async () => {
    respond({ 'workspace.reveal': () => null });
    await store.revealFolder();
    expect(send).toHaveBeenCalledWith('workspace.reveal');
    expect(store.error()).toBeNull();

    respond({
      'workspace.reveal': () => Promise.reject(new BridgeError('io_error', 'No file manager')),
    });
    await store.revealFolder();
    expect(store.error()).toBe('No file manager');
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
    // An unsaved request has no file to keep its responses' history for.
    expect(send).toHaveBeenCalledWith('request.send', expect.objectContaining({ path: null }));
    // It inherits the headers and auth of the folder it will be saved in.
    expect(send).toHaveBeenCalledWith(
      'request.send',
      expect.objectContaining({ folder: 'requests' }),
    );

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

  it('asks before closing a document with unsaved changes', async () => {
    const confirm = vi.fn().mockResolvedValue(false);
    dialogs.confirm = confirm;
    store.newRequest();
    const id = store.activeId()!;

    await store.close(id);
    expect(confirm).toHaveBeenCalledOnce();
    expect(store.documents()).toHaveLength(1);

    confirm.mockResolvedValue(true);
    await store.close(id);
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

  it('clones a repository and opens it, closing the open documents', async () => {
    store.newRequest();
    store.documents.update((docs) => docs.map((d) => ({ ...d, dirty: false })));
    respond({
      'workspace.clone': () => workspace,
      'workspace.recent': () => [],
    });

    const result = await store.cloneRepository('https://example.com/api.git', '/home/me', 'api');

    expect(result).toBeNull();
    expect(send).toHaveBeenCalledWith('workspace.clone', {
      url: 'https://example.com/api.git',
      parentFolder: '/home/me',
      name: 'api',
      signIn: null,
    });
    expect(store.workspace()).toEqual({ name: 'api', root: '/home/me/api' });
    expect(store.documents()).toEqual([]);
  });

  it('returns why a clone failed without showing it as the store error', async () => {
    respond({
      'workspace.clone': () =>
        Promise.reject(new BridgeError('git.authentication_failed', 'Denied')),
      'workspace.recent': () => [],
    });

    const result = await store.cloneRepository('https://example.com/api.git', '/home/me');

    expect(result).toBe('Denied');
    expect(send).toHaveBeenCalledWith('workspace.clone', {
      url: 'https://example.com/api.git',
      parentFolder: '/home/me',
      name: null,
      signIn: null,
    });
    expect(store.workspace()).toBeNull();
    expect(store.error()).toBeNull();
  });

  it('asks for credentials when git cannot sign in, and clones with them', async () => {
    const signIn = { url: 'https://github.com', username: 'me', password: 'token' };
    (dialogs as unknown as { open: Mock }).open = vi.fn(() => ({ closed: of(signIn) }));
    let tries = 0;
    respond({
      'workspace.clone': (payload) => {
        tries++;
        if (!payload.signIn) {
          throw new BridgeError('git.authentication_failed', 'Denied', 'https://github.com');
        }
        return workspace;
      },
      'workspace.recent': () => [],
    });

    const result = await store.cloneRepository('https://github.com/team/api.git', '/home/me');

    expect(result).toBeNull();
    expect(tries).toBe(2);
    expect(send).toHaveBeenCalledWith('workspace.clone', {
      url: 'https://github.com/team/api.git',
      parentFolder: '/home/me',
      name: null,
      signIn,
    });
  });

  it('does not clone when the user keeps their unsaved changes', async () => {
    store.newRequest();
    dialogs.confirm.mockResolvedValue(false);
    respond({});

    expect(await store.cloneRepository('https://example.com/api.git', '/home/me')).toBe(false);
    expect(send).not.toHaveBeenCalled();
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
    const confirm = vi.fn().mockResolvedValue(false);
    dialogs.confirm = confirm;
    respond({
      'sharing.set': (payload) => payload,
      'workspace.tree': () => workspace,
    });
    const node = { path: 'users/get.yaml', name: 'Get user', kind: 'request' as const };

    await store.setRequestLocalOnly(node, true);
    expect(confirm).toHaveBeenCalledOnce();
    expect(send).not.toHaveBeenCalled();

    confirm.mockResolvedValue(true);
    await store.setRequestLocalOnly(node, true);
    expect(send).toHaveBeenCalledWith('sharing.set', { path: 'users/get.yaml', localOnly: true });
    expect(store.sharingChanged()).toBe(1);
  });

  it('shares a file again without asking, and reports failures', async () => {
    const confirm = vi.fn();
    dialogs.confirm = confirm;
    respond({ 'sharing.set': () => Promise.reject(new BridgeError('io_error', 'Read-only')) });

    expect(await store.setLocalOnly('environments/dev.yaml', 'Dev', false)).toBe(false);
    expect(confirm).not.toHaveBeenCalled();
    expect(store.error()).toContain('Read-only');
    expect(store.sharingChanged()).toBe(0);
  });

  describe('files changed on disk', () => {
    const node = { path: 'requests/get.yaml', name: 'Get user', kind: 'request' } as const;
    let onDisk: RequestDto;

    beforeEach(async () => {
      onDisk = request;
      respond({
        'request.load': () => onDisk,
        'request.save': (p) => ({ path: p.path, request: p.request }),
        'workspace.tree': () => workspace,
      });
      store.workspace.set({ name: 'api', root: '/home/me/api' });
      await store.openRequest(node);
    });

    const doc = () => store.documents()[0];

    it('reloads a document without unsaved edits silently', async () => {
      onDisk = { ...request, url: 'https://example.com/changed' };

      await store.reloadFromDisk(['requests/get.yaml']);

      expect(doc()).toMatchObject({ url: 'https://example.com/changed', dirty: false });
      expect(dialogs.confirm).not.toHaveBeenCalled();
    });

    it('reloads documents inside a folder that changed', async () => {
      onDisk = { ...request, url: 'https://example.com/changed' };

      await store.reloadFromDisk(['requests']);

      expect(doc().url).toBe('https://example.com/changed');
    });

    it('leaves a document whose file did not change alone, such as after its own save', async () => {
      store.update(doc().id, { url: 'https://example.com/saved' });
      await store.save(doc().id);
      onDisk = { ...request, url: 'https://example.com/saved' };
      const revision = doc().revision;
      store.update(doc().id, { url: 'https://example.com/typing' });

      await store.reloadFromDisk(['requests/get.yaml']);

      expect(dialogs.confirm).not.toHaveBeenCalled();
      expect(doc()).toMatchObject({ url: 'https://example.com/typing', dirty: true });
      expect(doc().revision).toBe(revision + 1);
    });

    it('asks before replacing unsaved edits, and reloads when the user chooses to', async () => {
      store.update(doc().id, { url: 'https://example.com/mine' });
      onDisk = { ...request, url: 'https://example.com/theirs' };

      await store.reloadFromDisk(['requests/get.yaml']);

      expect(dialogs.confirm).toHaveBeenCalledWith(
        expect.stringContaining('"Get user" changed on disk'),
        expect.objectContaining({ confirmLabel: 'Reload', cancelLabel: 'Keep mine' }),
      );
      expect(doc()).toMatchObject({ url: 'https://example.com/theirs', dirty: false });
    });

    it('keeps unsaved edits when the user chooses to, and does not ask again for the same change', async () => {
      dialogs.confirm.mockResolvedValue(false);
      store.update(doc().id, { url: 'https://example.com/mine' });
      onDisk = { ...request, url: 'https://example.com/theirs' };

      await store.reloadFromDisk(['requests/get.yaml']);
      await store.reloadFromDisk(['requests/get.yaml']);

      expect(dialogs.confirm).toHaveBeenCalledTimes(1);
      expect(doc()).toMatchObject({ url: 'https://example.com/mine', dirty: true });
    });

    it('closes a document without unsaved edits whose file was deleted', async () => {
      respond({
        'request.load': () => Promise.reject(new BridgeError('not_found', 'Gone')),
      });

      await store.reloadFromDisk(['requests/get.yaml']);

      expect(store.documents()).toEqual([]);
      expect(store.tabs()).toEqual([]);
    });

    it('detaches a document with unsaved edits whose file was deleted', async () => {
      store.update(doc().id, { url: 'https://example.com/mine' });
      respond({
        'request.load': () => Promise.reject(new BridgeError('not_found', 'Gone')),
      });

      await store.reloadFromDisk(['requests/get.yaml']);

      expect(doc()).toMatchObject({ path: null, dirty: true, url: 'https://example.com/mine' });
    });
  });
});
