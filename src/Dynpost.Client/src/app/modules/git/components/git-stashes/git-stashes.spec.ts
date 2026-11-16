import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { Mock } from 'vitest';
import { BridgeErrorShape, GitChange, GitStash, GitStatus } from '../../../../models/messages';
import { GitStore } from '../../../../services/git-store';
import { Dialogs } from '../../../../services/dialogs';
import { GitStashes } from './git-stashes';
import { testDate, useTestDateFormat } from '../../../../testing/date-format';

const stash = (patch: Partial<GitStash>): GitStash => ({
  id: 'aaa111',
  index: 0,
  message: 'Login work',
  branch: 'main',
  date: null,
  ...patch,
});

describe('GitStashes', () => {
  let dialogs: { confirm: Mock };
  let git: {
    stashes: ReturnType<typeof signal<GitStash[] | null>>;
    status: ReturnType<typeof signal<GitStatus | null>>;
    conflicts: ReturnType<typeof signal<GitChange[]>>;
    busy: ReturnType<typeof signal<string | null>>;
    error: ReturnType<typeof signal<BridgeErrorShape | null>>;
    failedOperation: ReturnType<typeof signal<string | null>>;
    loadStashes: Mock;
    stash: Mock;
    applyStash: Mock;
    dropStash: Mock;
    dismissError: Mock;
  };

  const status = (patch: Partial<GitStatus> = {}): GitStatus => ({
    branch: 'main',
    upstream: null,
    ahead: 0,
    behind: 0,
    merging: false,
    changes: [],
    ...patch,
  });

  beforeEach(() => {
    dialogs = { confirm: vi.fn().mockResolvedValue(true) };
    git = {
      stashes: signal<GitStash[] | null>([
        stash({}),
        stash({ id: 'bbb222', index: 1, message: 'abc1234 Init', branch: null }),
      ]),
      status: signal<GitStatus | null>(status()),
      conflicts: signal<GitChange[]>([]),
      busy: signal<string | null>(null),
      error: signal<BridgeErrorShape | null>(null),
      failedOperation: signal<string | null>(null),
      loadStashes: vi.fn().mockResolvedValue(undefined),
      stash: vi.fn().mockResolvedValue(true),
      applyStash: vi.fn().mockResolvedValue(true),
      dropStash: vi.fn().mockResolvedValue(true),
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
    const fixture = TestBed.createComponent(GitStashes);
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('[aria-expanded]') as HTMLButtonElement).click();
    fixture.detectChanges();
    return fixture;
  }

  const button = (root: HTMLElement, label: string) =>
    root.querySelector(`[aria-label="${label}"]`) as HTMLButtonElement | null;

  it('reads the stashes when opened and lists them with their branch', async () => {
    const root = (await render()).nativeElement as HTMLElement;

    expect(git.loadStashes).toHaveBeenCalled();
    expect(root.textContent).toMatch(/Stashes\s*\(2\)/);
    expect(root.textContent).toContain('Login work');
    expect(root.textContent).toContain('On main');
    expect(root.textContent).toContain('Detached HEAD');
  });

  it('pops and applies an entry', async () => {
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;

    button(root, 'Pop Login work')!.click();
    await fixture.whenStable();
    button(root, 'Apply abc1234 Init')!.click();
    await fixture.whenStable();

    expect(git.applyStash).toHaveBeenNthCalledWith(
      1,
      expect.objectContaining({ id: 'aaa111' }),
      true,
    );
    expect(git.applyStash).toHaveBeenNthCalledWith(
      2,
      expect.objectContaining({ id: 'bbb222' }),
      false,
    );
  });

  it('drops an entry only after a confirmation', async () => {
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;

    dialogs.confirm.mockResolvedValueOnce(false);
    button(root, 'Drop Login work')!.click();
    await fixture.whenStable();
    expect(git.dropStash).not.toHaveBeenCalled();

    button(root, 'Drop Login work')!.click();
    await fixture.whenStable();
    expect(dialogs.confirm.mock.calls[1][0]).toContain('Its changes are lost');
    expect(git.dropStash).toHaveBeenCalledWith(expect.objectContaining({ id: 'aaa111' }));
  });

  it('stashes the changes with the message from the form', async () => {
    git.status.set(status({ changes: [{ path: 'a.yaml', status: 'modified' }] }));
    const fixture = await render();
    const root = fixture.nativeElement as HTMLElement;

    button(root, 'Stash your changes')!.click();
    fixture.detectChanges();
    const input = button(root, 'Stash message') as unknown as HTMLInputElement;
    input.value = 'Half-done login';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (root.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(git.stash).toHaveBeenCalledWith('Half-done login');
  });

  it('only allows stashing when there are changes that can be set aside', async () => {
    const fixture = await render();
    const stashButton = button(fixture.nativeElement, 'Stash your changes')!;

    expect(stashButton.disabled).toBe(true);
    expect(stashButton.title).toBe('There are no changes to stash');

    git.status.set(status({ changes: [{ path: 'a.yaml', status: 'modified' }], merging: true }));
    fixture.detectChanges();
    expect(stashButton.disabled).toBe(true);
    expect(stashButton.title).toContain('abort the merge');

    git.status.set(status({ changes: [{ path: 'a.yaml', status: 'modified' }] }));
    fixture.detectChanges();
    expect(stashButton.disabled).toBe(false);
  });

  it('disables applying while there are changes or conflicts', async () => {
    git.status.set(status({ changes: [{ path: 'a.yaml', status: 'modified' }] }));
    const fixture = await render();
    const pop = button(fixture.nativeElement, 'Pop Login work')!;

    expect(pop.disabled).toBe(true);
    expect(pop.title).toContain('Commit, stash or discard');

    const conflicted: GitChange = { path: 'a.yaml', status: 'conflicted' };
    git.status.set(status({ changes: [conflicted] }));
    git.conflicts.set([conflicted]);
    fixture.detectChanges();
    expect(pop.disabled).toBe(true);
    expect(pop.title).toContain('Resolve the conflicted files');

    git.status.set(status());
    git.conflicts.set([]);
    fixture.detectChanges();
    expect(pop.disabled).toBe(false);
    expect(pop.title).toBe('Pop: bring back "Login work" and drop it');
  });

  it('shows the stash error with a way to dismiss it', async () => {
    git.failedOperation.set('stash');
    git.error.set({ code: 'git.stash_not_found', message: 'That stash no longer exists.' });
    const root = (await render()).nativeElement as HTMLElement;

    expect(root.querySelector('[role="alert"]')!.textContent).toContain('no longer exists');
    button(root, 'Dismiss')!.click();
    expect(git.dismissError).toHaveBeenCalled();
  });

  it('shows when each entry was made in the system format', async () => {
    git.stashes.set([stash({ date: '2026-05-06T07:08:09Z' })]);
    const fixture = await render();
    useTestDateFormat();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toMatch(
      new RegExp(`On main\\s*· ${testDate('2026-05-06T07:08:09Z')}`),
    );
  });
});
