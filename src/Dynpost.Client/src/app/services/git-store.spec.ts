import { of } from 'rxjs';
import { TestBed } from '@angular/core/testing';
import { Mock } from 'vitest';
import { GitBranch, GitStash, GitStatus, WorkspaceDto } from '../models/messages';
import { Bridge, BridgeError } from './bridge';
import { GitStore } from './git-store';
import { WorkspaceStore } from './workspace-store';
import { Dialogs } from './dialogs';

const workspace = { name: 'api', root: '/home/me/api', nodes: [] } as unknown as WorkspaceDto;

const status = (patch: Partial<GitStatus> = {}): GitStatus => ({
  branch: 'main',
  upstream: 'origin/main',
  ahead: 0,
  behind: 0,
  merging: false,
  changes: [],
  ...patch,
});

const main: GitBranch = {
  name: 'main',
  remote: null,
  current: true,
  upstream: 'origin/main',
  commitId: 'a1',
  date: null,
  subject: 'Init',
};
const feature: GitBranch = { ...main, name: 'feature', current: false, upstream: null };
const saved: GitStash = {
  id: 'aaa111',
  index: 0,
  message: 'Login work',
  branch: 'main',
  date: null,
};

describe('GitStore', () => {
  let dialogs: { confirm: Mock };
  beforeEach(() => (dialogs = { confirm: vi.fn().mockResolvedValue(true) }));
  let send: Mock;
  let handlers: Record<string, (payload: any) => unknown>;
  let store: GitStore;
  let workspaceStore: WorkspaceStore;

  beforeEach(async () => {
    handlers = {
      'git.status': () => status(),
      'git.remote.list': () => [{ name: 'origin', fetchUrl: 'u', pushUrl: 'u' }],
      'git.branch.list': () => ({ status: status(), branches: [main] }),
      'git.identity': () => ({ name: 'Ada', email: 'ada@example.com' }),
      'workspace.tree': () => workspace,
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
    store = TestBed.inject(GitStore);
    workspaceStore = TestBed.inject(WorkspaceStore);
    workspaceStore.workspace.set({ name: 'api', root: '/home/me/api' } as never);
    TestBed.tick();
    await vi.waitFor(() => expect(store.identity()).not.toBeNull());
  });

  it('loads the remotes and identity with the status', () => {
    expect(store.hasRemotes()).toBe(true);
    expect(store.needsIdentity()).toBe(false);
  });

  it('asks for an identity when git has none', async () => {
    handlers['git.identity'] = () => ({ name: null, email: null });
    await store.refresh();
    await vi.waitFor(() => expect(store.needsIdentity()).toBe(true));
  });

  it('stores the identity the user enters', async () => {
    handlers['git.identity.set'] = (p) => ({ name: p.name, email: p.email });

    expect(await store.setIdentity('Bea', 'bea@example.com', false)).toBe(true);

    expect(send).toHaveBeenCalledWith('git.identity.set', {
      name: 'Bea',
      email: 'bea@example.com',
      global: false,
    });
    expect(store.identity()).toEqual({ name: 'Bea', email: 'bea@example.com' });
  });

  it('reloads the identity when a commit fails for lack of one', async () => {
    handlers['git.commit'] = () => {
      throw new BridgeError('git.identity_unknown', 'Who are you?');
    };
    handlers['git.identity'] = () => ({ name: 'Ada', email: null });

    expect(await store.commit('Add users')).toBe(false);

    expect(store.error()?.code).toBe('git.identity_unknown');
    expect(store.needsIdentity()).toBe(true);
  });

  it('fetches and reports the commits to pull', async () => {
    handlers['git.fetch'] = () => status({ behind: 2 });

    await store.fetch();

    expect(store.status()?.behind).toBe(2);
    expect(store.notice()?.text).toBe('2 new commits to pull.');
  });

  it('fetches after a rejected push, keeping the rejection', async () => {
    handlers['git.push'] = () => {
      throw new BridgeError('git.push_rejected', 'Pull first.');
    };
    handlers['git.fetch'] = () => status({ behind: 1, ahead: 1 });

    await store.push();

    expect(store.error()?.code).toBe('git.push_rejected');
    expect(store.status()?.behind).toBe(1);
    expect(store.busy()).toBeNull();
  });

  it('counts unsaved tabs, and saves them all', async () => {
    workspaceStore.documents.set([
      { id: 'a.yaml', key: 'a', path: 'a.yaml', dirty: true, saving: false } as never,
      { id: 'b.yaml', key: 'b', path: 'b.yaml', dirty: false, saving: false } as never,
    ]);
    expect(store.unsavedCount()).toBe(1);
    const save = vi.spyOn(workspaceStore, 'save').mockResolvedValue();

    await store.saveAll();

    expect(save).toHaveBeenCalledExactlyOnceWith('a.yaml');
  });

  it('reads the branches when asked, then keeps them current with the status', async () => {
    expect(store.branches()).toBeNull();
    await store.loadBranches();
    expect(store.branches()).toEqual([main]);

    handlers['git.branch.list'] = () => ({ status: status(), branches: [main, feature] });
    await store.refresh();
    await vi.waitFor(() => expect(store.branches()).toHaveLength(2));
  });

  it('reloads the shown branches after a fetch, dropping ones deleted on the remote', async () => {
    handlers['git.branch.list'] = () => ({ status: status(), branches: [main, feature] });
    await store.loadBranches();
    handlers['git.fetch'] = () => status();
    handlers['git.branch.list'] = () => ({ status: status(), branches: [main] });

    await store.fetch();

    await vi.waitFor(() => expect(store.branches()).toEqual([main]));
  });

  it('signs in when git cannot, and pushes with the credentials entered', async () => {
    const signIn = { url: 'https://github.com', username: 'me', password: 'token' };
    (dialogs as unknown as { open: Mock }).open = vi.fn(() => ({ closed: of(signIn) }));
    handlers['git.push'] = (payload) => {
      if (!payload.signIn) {
        throw new BridgeError('git.authentication_failed', 'Denied', 'https://github.com');
      }
      return status();
    };

    await store.push();

    expect(send).toHaveBeenCalledWith('git.push', { signIn: null });
    expect(send).toHaveBeenCalledWith('git.push', { signIn });
    expect(store.error()).toBeNull();
    expect(store.notice()?.text).toContain('Pushed');
  });

  it('reloads the shown branches after a failed pull, keeping its error', async () => {
    handlers['git.branch.list'] = () => ({ status: status(), branches: [main, feature] });
    await store.loadBranches();
    handlers['git.pull'] = () => {
      throw new BridgeError('git.upstream_gone', 'Gone.');
    };
    handlers['git.branch.list'] = () => ({ status: status(), branches: [main] });

    await store.pull();

    expect(store.error()?.code).toBe('git.upstream_gone');
    await vi.waitFor(() => expect(store.branches()).toEqual([main]));
  });

  it('deletes a branch on the remote and shows the remaining branches', async () => {
    handlers['git.branch.deleteRemote'] = () => ({ status: status(), branches: [main] });

    expect(await store.deleteRemoteBranch('origin/feature')).toBe(true);

    expect(send).toHaveBeenCalledWith('git.branch.deleteRemote', { name: 'origin/feature' });
    expect(store.branches()).toEqual([main]);
    expect(store.notice()?.text).toBe('Deleted origin/feature on the remote.');
  });

  it('switches branches and reloads the workspace from disk', async () => {
    handlers['git.branch.switch'] = () => ({
      status: status({ branch: 'feature' }),
      branches: [
        { ...main, current: false },
        { ...feature, current: true },
      ],
    });
    const refresh = vi.spyOn(workspaceStore, 'refresh').mockResolvedValue();
    const reload = vi.spyOn(workspaceStore, 'reloadFromDisk').mockResolvedValue();

    expect(await store.switchBranch('feature')).toBe(true);

    expect(send).toHaveBeenCalledWith('git.branch.switch', { name: 'feature' });
    expect(store.status()?.branch).toBe('feature');
    expect(store.branches()?.find((b) => b.current)?.name).toBe('feature');
    expect(store.notice()?.text).toBe('Switched to feature.');
    expect(refresh).toHaveBeenCalled();
    expect(reload).toHaveBeenCalled();
  });

  it('asks before switching with unsaved tabs', async () => {
    workspaceStore.documents.set([
      { id: 'a.yaml', key: 'a', path: 'a.yaml', dirty: true, saving: false } as never,
    ]);
    const confirm = vi.fn().mockResolvedValue(false);
    dialogs.confirm = confirm;

    expect(await store.switchBranch('feature')).toBe(false);

    expect(confirm.mock.calls[0][0]).toContain('1 open tab has unsaved changes');
    expect(send).not.toHaveBeenCalledWith('git.branch.switch', expect.anything());
  });

  it('reports a switch refused over uncommitted changes', async () => {
    handlers['git.branch.switch'] = () => {
      throw new BridgeError('git.uncommitted_changes', 'You have uncommitted changes to: a.yaml.');
    };

    expect(await store.switchBranch('feature')).toBe(false);

    expect(store.failedOperation()).toBe('branch');
    expect(store.error()?.message).toContain('a.yaml');
  });

  it('merges a branch, reports the outcome and reloads the workspace from disk', async () => {
    handlers['git.branch.merge'] = () => ({
      outcome: 'merged',
      status: status({ ahead: 2 }),
      branches: [main, feature],
      conflictsOutsideWorkspace: [],
    });
    const refresh = vi.spyOn(workspaceStore, 'refresh').mockResolvedValue();
    const reload = vi.spyOn(workspaceStore, 'reloadFromDisk').mockResolvedValue();

    expect(await store.mergeBranch(feature)).toBe(true);

    expect(send).toHaveBeenCalledWith('git.branch.merge', { name: 'feature', remote: false });
    expect(store.status()?.ahead).toBe(2);
    expect(store.branches()).toHaveLength(2);
    expect(store.notice()).toEqual({ kind: 'info', text: 'Merged feature into main.' });
    expect(refresh).toHaveBeenCalled();
    expect(reload).toHaveBeenCalled();
  });

  it('warns when a merge stops on conflicts', async () => {
    handlers['git.branch.merge'] = () => ({
      outcome: 'conflicted',
      status: status({ merging: true, changes: [{ path: 'a.yaml', status: 'conflicted' }] }),
      branches: [main, feature],
      conflictsOutsideWorkspace: ['docs/notes.md'],
    });
    vi.spyOn(workspaceStore, 'reloadFromDisk').mockResolvedValue();
    const topic: GitBranch = { ...feature, name: 'origin/topic', remote: 'origin' };

    expect(await store.mergeBranch(topic)).toBe(true);

    expect(send).toHaveBeenCalledWith('git.branch.merge', { name: 'origin/topic', remote: true });
    expect(store.notice()?.kind).toBe('warning');
    expect(store.notice()?.text).toContain('Merging origin/topic stopped on conflicts');
    expect(store.notice()?.text).toContain('outside the workspace folder');
    expect(store.notice()?.text).toContain('docs/notes.md');
    expect(store.conflicts()).toHaveLength(1);
  });

  it('asks before merging with unsaved tabs', async () => {
    workspaceStore.documents.set([
      { id: 'a.yaml', key: 'a', path: 'a.yaml', dirty: true, saving: false } as never,
    ]);
    const confirm = vi.fn().mockResolvedValue(false);
    dialogs.confirm = confirm;

    expect(await store.mergeBranch(feature)).toBe(false);

    expect(confirm.mock.calls[0][0]).toContain('Merge feature into main?');
    expect(confirm.mock.calls[0][0]).toContain('overwrite the merged versions');
    expect(send).not.toHaveBeenCalledWith('git.branch.merge', expect.anything());
  });

  it('reads the stashes when asked, then keeps them current with the status', async () => {
    handlers['git.stash.list'] = () => ({ status: status(), stashes: [saved] });
    expect(store.stashes()).toBeNull();

    await store.loadStashes();
    expect(store.stashes()).toEqual([saved]);

    handlers['git.stash.list'] = () => ({ status: status(), stashes: [] });
    await store.refresh();
    await vi.waitFor(() => expect(store.stashes()).toEqual([]));
  });

  it('stashes the changes with their message and reloads the workspace from disk', async () => {
    handlers['git.stash.push'] = () => ({ status: status(), stashes: [saved] });
    const reload = vi.spyOn(workspaceStore, 'reloadFromDisk').mockResolvedValue();

    expect(await store.stash('  Login work ')).toBe(true);

    expect(send).toHaveBeenCalledWith('git.stash.push', { message: 'Login work' });
    expect(store.stashes()).toEqual([saved]);
    expect(store.notice()?.text).toContain('Stashed your changes');
    expect(reload).toHaveBeenCalled();
  });

  it('lets git describe a stash without a message', async () => {
    handlers['git.stash.push'] = () => ({ status: status(), stashes: [saved] });
    vi.spyOn(workspaceStore, 'reloadFromDisk').mockResolvedValue();

    await store.stash('  ');

    expect(send).toHaveBeenCalledWith('git.stash.push', {});
  });

  it('asks before stashing with unsaved tabs, which are not stashed', async () => {
    workspaceStore.documents.set([
      { id: 'a.yaml', key: 'a', path: 'a.yaml', dirty: true, saving: false } as never,
    ]);
    const confirm = vi.fn().mockResolvedValue(false);
    dialogs.confirm = confirm;

    expect(await store.stash('Login work')).toBe(false);

    expect(confirm.mock.calls[0][0]).toContain("Their edits aren't stashed");
    expect(send).not.toHaveBeenCalledWith('git.stash.push', expect.anything());
  });

  it('pops a stash and reloads the workspace from disk', async () => {
    handlers['git.stash.apply'] = () => ({
      outcome: 'applied',
      status: status({ changes: [{ path: 'a.yaml', status: 'modified' }] }),
      stashes: [],
      conflictsOutsideWorkspace: [],
    });
    const reload = vi.spyOn(workspaceStore, 'reloadFromDisk').mockResolvedValue();

    expect(await store.applyStash(saved, true)).toBe(true);

    expect(send).toHaveBeenCalledWith('git.stash.apply', { id: 'aaa111', pop: true });
    expect(store.stashes()).toEqual([]);
    expect(store.changeCount()).toBe(1);
    expect(store.notice()).toEqual({
      kind: 'info',
      text: 'Popped the stash "Login work": its changes are back, and it was dropped.',
    });
    expect(reload).toHaveBeenCalled();
  });

  it('warns when a stash conflicts, saying it was kept', async () => {
    handlers['git.stash.apply'] = () => ({
      outcome: 'conflicted',
      status: status({ changes: [{ path: 'a.yaml', status: 'conflicted' }] }),
      stashes: [saved],
      conflictsOutsideWorkspace: ['docs/notes.md'],
    });
    vi.spyOn(workspaceStore, 'reloadFromDisk').mockResolvedValue();

    expect(await store.applyStash(saved, true)).toBe(true);

    expect(store.notice()?.kind).toBe('warning');
    expect(store.notice()?.text).toContain('conflicts with the current files');
    expect(store.notice()?.text).toContain('The stash was kept; drop it once you are done');
    expect(store.notice()?.text).toContain('docs/notes.md');
    expect(store.conflicts()).toHaveLength(1);
  });

  it('drops a stash', async () => {
    handlers['git.stash.drop'] = () => ({ status: status(), stashes: [] });

    expect(await store.dropStash(saved)).toBe(true);

    expect(send).toHaveBeenCalledWith('git.stash.drop', { id: 'aaa111' });
    expect(store.stashes()).toEqual([]);
    expect(store.notice()?.text).toBe('Dropped the stash "Login work".');
  });

  it('stashes the changes, then switches branch', async () => {
    handlers['git.stash.push'] = () => ({ status: status(), stashes: [saved] });
    handlers['git.branch.switch'] = () => ({
      status: status({ branch: 'feature' }),
      branches: [
        { ...main, current: false },
        { ...feature, current: true },
      ],
    });
    vi.spyOn(workspaceStore, 'reloadFromDisk').mockResolvedValue();

    expect(await store.stashAndSwitch('feature')).toBe(true);

    const types = send.mock.calls.map(([type]) => type);
    expect(types.indexOf('git.stash.push')).toBeLessThan(types.indexOf('git.branch.switch'));
    expect(store.status()?.branch).toBe('feature');
    expect(store.notice()?.text).toContain('Stashed your changes and switched to feature');
  });

  it('does not switch when stashing fails', async () => {
    handlers['git.stash.push'] = () => {
      throw new BridgeError('git.nothing_to_stash', 'There are no changes to stash.');
    };

    expect(await store.stashAndSwitch('feature')).toBe(false);

    expect(send).not.toHaveBeenCalledWith('git.branch.switch', expect.anything());
    expect(store.failedOperation()).toBe('stash');
  });

  it('creates a worktree, keeping its list and reporting what was copied', async () => {
    const linked = {
      path: '/home/me/api-login',
      workspaceFolder: '/home/me/api-login',
      branch: 'login',
      head: 'a1',
      main: false,
      current: false,
      locked: false,
      prunable: false,
    };
    handlers['git.worktree.add'] = () => ({ worktree: linked, worktrees: [linked], warning: null });

    expect(await store.addWorktree('/home/me', ' api-login ', ' login ', true)).toEqual(linked);

    expect(send).toHaveBeenCalledWith('git.worktree.add', {
      parentFolder: '/home/me',
      name: 'api-login',
      branch: 'login',
      createBranch: true,
    });
    expect(store.worktrees()).toEqual([linked]);
    expect(store.notice()?.text).toContain('with your secrets, .env file and client certificates');
  });

  it('shows the warning of a worktree whose local data could not all be copied', async () => {
    const linked = {
      path: '/x',
      workspaceFolder: '/x',
      branch: 'b',
      head: 'a1',
      main: false,
      current: false,
      locked: false,
      prunable: false,
    };
    handlers['git.worktree.add'] = () => ({
      worktree: linked,
      worktrees: [linked],
      warning: "Dynpost couldn't copy secret values.",
    });

    await store.addWorktree('/', 'x', 'b', false);

    expect(store.notice()).toEqual({
      kind: 'warning',
      text: "Dynpost couldn't copy secret values.",
    });
  });

  it('reports a worktree that could not be created', async () => {
    handlers['git.worktree.add'] = () => {
      throw new BridgeError(
        'git.branch_in_worktree',
        "'b' is checked out in the worktree at '/y'.",
      );
    };

    expect(await store.addWorktree('/', 'x', 'b', false)).toBeNull();

    expect(store.failedOperation()).toBe('worktree');
  });

  it('removes a worktree, forcing only when asked', async () => {
    const linked = {
      path: '/x',
      workspaceFolder: '/x',
      branch: 'b',
      head: 'a1',
      main: false,
      current: false,
      locked: false,
      prunable: false,
    };
    handlers['git.worktree.remove'] = () => [];

    expect(await store.removeWorktree(linked)).toBe(true);
    expect(await store.removeWorktree(linked, true)).toBe(true);

    expect(send).toHaveBeenCalledWith('git.worktree.remove', { path: '/x', force: false });
    expect(send).toHaveBeenCalledWith('git.worktree.remove', { path: '/x', force: true });
    expect(store.worktrees()).toEqual([]);
    expect(store.notice()?.text).toBe('Removed the worktree at /x.');
  });

  it('opens a worktree through its workspace folder', async () => {
    const open = vi.spyOn(workspaceStore, 'openRecent').mockResolvedValue();

    await store.openWorktree({
      path: '/x',
      workspaceFolder: '/x/api',
      branch: 'b',
      head: 'a1',
      main: false,
      current: false,
      locked: false,
      prunable: false,
    });

    expect(open).toHaveBeenCalledWith('/x/api');
  });

  it('asks for an identity when a merge commit has none', async () => {
    handlers['git.branch.merge'] = () => {
      throw new BridgeError('git.identity_unknown', 'Git does not know who you are.');
    };
    handlers['git.identity'] = () => ({ name: null, email: null });

    expect(await store.mergeBranch(feature)).toBe(false);

    expect(store.failedOperation()).toBe('branch');
    expect(store.needsIdentity()).toBe(true);
  });

  describe('background refresh', () => {
    const changed = status({ changes: [{ path: 'requests/a.yaml', status: 'modified' }] as never });

    it('reads the status without showing an operation', async () => {
      handlers['git.status'] = () => changed;
      const read = store.refreshInBackground();

      expect(store.busy()).toBeNull();
      await read;
      expect(store.changeCount()).toBe(1);
    });

    it('reads again once a running operation finishes, instead of dropping the change', async () => {
      let finishFetch!: () => void;
      handlers['git.fetch'] = () =>
        new Promise((resolve) => (finishFetch = () => resolve(status())));
      const fetching = store.fetch();
      await vi.waitFor(() => expect(store.busy()).toBe('fetch'));

      handlers['git.status'] = () => changed;
      await store.refreshInBackground();
      expect(store.changeCount()).toBe(0);

      finishFetch();
      await fetching;
      await vi.waitFor(() => expect(store.changeCount()).toBe(1));
    });

    it('reads once more when asked during a read, so the latest change is seen', async () => {
      let finishFirst!: () => void;
      handlers['git.status'] = () =>
        new Promise((resolve) => (finishFirst = () => resolve(status())));
      const first = store.refreshInBackground();
      void store.refreshInBackground();
      void store.refreshInBackground();

      handlers['git.status'] = () => changed;
      finishFirst();
      await first;

      await vi.waitFor(() => expect(store.changeCount()).toBe(1));
      expect(send.mock.calls.filter(([type]) => type === 'git.status')).toHaveLength(3);
    });

    it('drops a read an operation overtook', async () => {
      let finishRead!: () => void;
      handlers['git.status'] = () =>
        new Promise((resolve) => (finishRead = () => resolve(changed)));
      const read = store.refreshInBackground();
      store.status.set(status({ ahead: 2 }));

      finishRead();
      await read;

      expect(store.status()?.ahead).toBe(2);
      expect(store.changeCount()).toBe(0);
    });

    it('keeps the errors of other operations', async () => {
      store.failedOperation.set('push');
      store.error.set({ code: 'git.push_rejected', message: 'Rejected' });

      await store.refreshInBackground();

      expect(store.error()?.code).toBe('git.push_rejected');
    });

    it('reports a status that can no longer be read', async () => {
      handlers['git.status'] = () => {
        throw new BridgeError('git.not_a_repository', 'Not a repository');
      };

      await store.refreshInBackground();

      expect(store.notARepository()).toBe(true);
    });
  });
});
