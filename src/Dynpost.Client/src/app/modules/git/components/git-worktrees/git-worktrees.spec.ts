import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { Mock } from 'vitest';
import { BridgeErrorShape, GitBranch, GitStatus, GitWorktree } from '../../../../models/messages';
import { GitStore } from '../../../../services/git-store';
import { Dialogs } from '../../../../services/dialogs';
import { GitWorktrees } from './git-worktrees';

const worktree = (patch: Partial<GitWorktree>): GitWorktree => ({
  path: '/home/me/api',
  workspaceFolder: '/home/me/api',
  branch: 'main',
  head: 'abc1234def',
  main: true,
  current: true,
  locked: false,
  prunable: false,
  ...patch,
});

const branch = (name: string, current = false): GitBranch => ({
  name,
  remote: null,
  current,
  upstream: null,
  commitId: 'a1',
  date: null,
  subject: 'Init',
});

const feature = worktree({
  path: '/home/me/api-feature',
  workspaceFolder: '/home/me/api-feature',
  branch: 'feature',
  main: false,
  current: false,
});

describe('GitWorktrees', () => {
  let dialogs: { confirm: Mock };
  let git: {
    worktrees: ReturnType<typeof signal<GitWorktree[] | null>>;
    branches: ReturnType<typeof signal<GitBranch[] | null>>;
    status: ReturnType<typeof signal<GitStatus | null>>;
    busy: ReturnType<typeof signal<string | null>>;
    error: ReturnType<typeof signal<BridgeErrorShape | null>>;
    failedOperation: ReturnType<typeof signal<string | null>>;
    loadWorktrees: Mock;
    loadBranches: Mock;
    pickWorktreeFolder: Mock;
    addWorktree: Mock;
    removeWorktree: Mock;
    openWorktree: Mock;
    dismissError: Mock;
  };

  beforeEach(() => {
    dialogs = { confirm: vi.fn().mockResolvedValue(true) };
    git = {
      worktrees: signal<GitWorktree[] | null>([worktree({}), feature]),
      branches: signal<GitBranch[] | null>([
        branch('main', true),
        branch('feature'),
        branch('feature/login'),
      ]),
      status: signal<GitStatus | null>({
        branch: 'main',
        upstream: null,
        ahead: 0,
        behind: 0,
        merging: false,
        changes: [],
      }),
      busy: signal<string | null>(null),
      error: signal<BridgeErrorShape | null>(null),
      failedOperation: signal<string | null>(null),
      loadWorktrees: vi.fn().mockResolvedValue(undefined),
      loadBranches: vi.fn().mockResolvedValue(undefined),
      pickWorktreeFolder: vi.fn().mockResolvedValue('/elsewhere'),
      addWorktree: vi.fn().mockResolvedValue(feature),
      removeWorktree: vi.fn().mockResolvedValue(true),
      openWorktree: vi.fn().mockResolvedValue(undefined),
      dismissError: vi.fn(),
    };
  });

  async function render() {
    TestBed.configureTestingModule({
      providers: [
        { provide: GitStore, useValue: git },
        { provide: Dialogs, useValue: dialogs },
      ],
    });
    const fixture = TestBed.createComponent(GitWorktrees);
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('[aria-expanded]') as HTMLButtonElement).click();
    fixture.detectChanges();
    return fixture;
  }

  const button = (root: HTMLElement, label: string) =>
    root.querySelector(`[aria-label="${label}"]`) as HTMLButtonElement | null;

  async function openForm() {
    const fixture = await render();
    button(fixture.nativeElement, 'Create a worktree')!.click();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  function type(root: HTMLElement, label: string, value: string) {
    const input = button(root, label) as unknown as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('lists the worktrees, offering to open and remove only other linked ones', async () => {
    const root = (await render()).nativeElement as HTMLElement;

    expect(git.loadWorktrees).toHaveBeenCalled();
    expect(root.textContent).toMatch(/Worktrees\s*\(2\)/);
    expect(root.textContent).toContain('(open)');
    expect(button(root, 'Open api')).toBeNull();
    expect(button(root, 'Remove api')).toBeNull();

    button(root, 'Open api-feature')!.click();
    expect(git.openWorktree).toHaveBeenCalledWith(feature);
    expect(button(root, 'Remove api-feature')).not.toBeNull();
  });

  it('does not offer to remove a locked worktree, nor to open a missing one', async () => {
    git.worktrees.set([
      worktree({}),
      { ...feature, locked: true },
      { ...feature, path: '/gone', branch: 'old', prunable: true },
    ]);
    const root = (await render()).nativeElement as HTMLElement;

    expect(button(root, 'Remove api-feature')).toBeNull();
    expect(button(root, 'Open gone')).toBeNull();
    expect(button(root, 'Remove gone')).not.toBeNull();
    expect(root.textContent).toContain('(locked)');
    expect(root.textContent).toContain('(folder missing)');
  });

  it('suggests a folder next to the main worktree, named after the branch', async () => {
    const fixture = await openForm();
    const root = fixture.nativeElement as HTMLElement;

    // feature is in a worktree already, so the first free branch is feature/login.
    const select = button(root, 'Branch') as unknown as HTMLSelectElement;
    expect([...select.options].map((o) => o.value)).toEqual(['feature/login']);
    expect((button(root, 'Parent folder') as unknown as HTMLInputElement).value).toBe('/home/me');
    expect((button(root, 'Folder name') as unknown as HTMLInputElement).value).toBe(
      'api-feature-login',
    );
  });

  it('creates a worktree for a new branch, then offers to open it', async () => {
    const fixture = await openForm();
    const root = fixture.nativeElement as HTMLElement;
    (root.querySelectorAll('input[type="radio"]')[1] as HTMLInputElement).click();
    fixture.detectChanges();
    type(root, 'New branch name', 'login');
    fixture.detectChanges();
    expect((button(root, 'Folder name') as unknown as HTMLInputElement).value).toBe('api-login');

    (root.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(git.addWorktree).toHaveBeenCalledWith('/home/me', 'api-login', 'login', true);
    expect(dialogs.confirm.mock.calls[0][0]).toContain('Open the new worktree');
    expect(git.openWorktree).toHaveBeenCalledWith(feature);
  });

  it('keeps a folder name the user typed, and picks the parent folder', async () => {
    const fixture = await openForm();
    const root = fixture.nativeElement as HTMLElement;
    type(root, 'Folder name', 'mine');
    fixture.detectChanges();
    (root.querySelectorAll('input[type="radio"]')[1] as HTMLInputElement).click();
    fixture.detectChanges();
    type(root, 'New branch name', 'other');
    [...root.querySelectorAll('button')].find((b) => b.textContent?.includes('Browse'))!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect((button(root, 'Folder name') as unknown as HTMLInputElement).value).toBe('mine');
    expect((button(root, 'Parent folder') as unknown as HTMLInputElement).value).toBe('/elsewhere');
  });

  it('does not open the new worktree when declined', async () => {
    dialogs.confirm.mockResolvedValue(false);
    const fixture = await openForm();

    (fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(git.addWorktree).toHaveBeenCalledWith(
      '/home/me',
      'api-feature-login',
      'feature/login',
      false,
    );
    expect(git.openWorktree).not.toHaveBeenCalled();
  });

  it('asks a second time before removing a worktree with changes', async () => {
    git.removeWorktree.mockImplementation(async (_: GitWorktree, force?: boolean) => {
      if (force) return true;
      git.error.set({ code: 'git.worktree_has_changes', message: 'has changes' });
      return false;
    });
    const fixture = await render();

    button(fixture.nativeElement, 'Remove api-feature')!.click();
    await fixture.whenStable();

    expect(dialogs.confirm).toHaveBeenCalledTimes(2);
    expect(dialogs.confirm.mock.calls[0][0]).toContain("the branch 'feature' and its commits stay");
    expect(dialogs.confirm.mock.calls[1][0]).toContain('deletes them');
    expect(git.removeWorktree).toHaveBeenNthCalledWith(1, feature);
    expect(git.removeWorktree).toHaveBeenNthCalledWith(2, feature, true);
  });

  it('keeps a worktree with changes when the second confirmation is declined', async () => {
    dialogs.confirm = vi.fn().mockResolvedValueOnce(true).mockResolvedValueOnce(false);
    git.removeWorktree.mockImplementation(async () => {
      git.error.set({ code: 'git.worktree_has_changes', message: 'has changes' });
      return false;
    });
    const fixture = await render();

    button(fixture.nativeElement, 'Remove api-feature')!.click();
    await fixture.whenStable();

    expect(git.removeWorktree).toHaveBeenCalledTimes(1);
    expect(git.dismissError).toHaveBeenCalled();
  });
});
