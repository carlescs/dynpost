import { Dialog } from '@angular/cdk/dialog';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { Mock } from 'vitest';
import { formatCombo } from '../models/shortcuts';
import { Bridge } from './bridge';
import { Dialogs } from './dialogs';
import { GitStore } from './git-store';
import { Shortcuts } from './shortcuts';
import { WorkspaceStore } from './workspace-store';

const press = (key: string, init: KeyboardEventInit = {}) =>
  new KeyboardEvent('keydown', { key, cancelable: true, ...init });

describe('Shortcuts', () => {
  let send: Mock;
  let dialogs: { confirm: Mock; open: Mock };
  let openDialogs: unknown[];
  let shortcuts: Shortcuts;
  let workspace: WorkspaceStore;

  function setup(isDesktop = true) {
    send = vi.fn().mockResolvedValue(null);
    dialogs = { confirm: vi.fn().mockResolvedValue(true), open: vi.fn() };
    openDialogs = [];
    TestBed.configureTestingModule({
      providers: [
        { provide: Bridge, useValue: { isDesktop, send } },
        { provide: Dialogs, useValue: dialogs },
        { provide: Dialog, useValue: { openDialogs } },
      ],
    });
    shortcuts = TestBed.inject(Shortcuts);
    workspace = TestBed.inject(WorkspaceStore);
  }

  /** Opens `count` untitled requests and marks them saved, so closing them doesn't ask. */
  function openRequests(count: number): string[] {
    workspace.workspace.set({ name: 'api', root: '/r' });
    for (let i = 0; i < count; i++) workspace.newRequest();
    workspace.documents.update((docs) => docs.map((d) => ({ ...d, dirty: false })));
    return workspace.tabs();
  }

  it('saves with Ctrl+S and keeps the webview from acting on it', () => {
    setup();
    const save = vi.spyOn(workspace, 'saveActive').mockResolvedValue();
    const event = press('s', { ctrlKey: true });

    shortcuts.handle(event);

    expect(save).toHaveBeenCalled();
    expect(event.defaultPrevented).toBe(true);
  });

  it('accepts Cmd as well as Ctrl', () => {
    setup();
    const sendActive = vi.spyOn(workspace, 'sendActive').mockResolvedValue();

    shortcuts.handle(press('Enter', { metaKey: true }));

    expect(sendActive).toHaveBeenCalled();
  });

  it('ignores other keys and combinations with Alt', () => {
    setup();
    const save = vi.spyOn(workspace, 'saveActive').mockResolvedValue();
    const plain = press('s');
    const alt = press('s', { ctrlKey: true, altKey: true });

    shortcuts.handle(plain);
    shortcuts.handle(alt);

    expect(save).not.toHaveBeenCalled();
    expect(plain.defaultPrevented).toBe(false);
    expect(alt.defaultPrevented).toBe(false);
  });

  it('does nothing while a dialog is open', () => {
    setup();
    openDialogs.push({});
    const save = vi.spyOn(workspace, 'saveActive').mockResolvedValue();

    shortcuts.handle(press('s', { ctrlKey: true }));

    expect(save).not.toHaveBeenCalled();
  });

  it('opens a new request only when a workspace is open', () => {
    setup();
    shortcuts.handle(press('n', { ctrlKey: true }));
    expect(workspace.tabs()).toEqual([]);

    workspace.workspace.set({ name: 'api', root: '/r' });
    shortcuts.handle(press('n', { ctrlKey: true }));
    expect(workspace.tabs().length).toBe(1);
  });

  it('closes the active tab with Ctrl+W, showing its neighbour', async () => {
    setup();
    const [first, second] = openRequests(2);
    workspace.activateTab(first);

    shortcuts.handle(press('w', { ctrlKey: true }));

    await vi.waitFor(() => expect(workspace.tabs()).toEqual([second]));
    expect(workspace.activeTab()).toBe(second);
  });

  it('closes an open diff before any tab', async () => {
    setup();
    openRequests(1);
    const git = TestBed.inject(GitStore);
    git.diff.set({ path: 'a.yaml' } as never);

    shortcuts.handle(press('w', { ctrlKey: true }));

    await vi.waitFor(() => expect(git.diff()).toBeNull());
    expect(workspace.tabs().length).toBe(1);
  });

  it('cycles through the tabs with Ctrl+Tab and Ctrl+Shift+Tab', () => {
    setup();
    const [first, second, third] = openRequests(3);

    shortcuts.handle(press('Tab', { ctrlKey: true }));
    expect(workspace.activeTab()).toBe(first);
    shortcuts.handle(press('Tab', { ctrlKey: true, shiftKey: true }));
    expect(workspace.activeTab()).toBe(third);
    shortcuts.handle(press('PageUp', { ctrlKey: true }));
    expect(workspace.activeTab()).toBe(second);
  });

  it('toggles the sidebar and routes to a view', async () => {
    setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    shortcuts.handle(press('b', { ctrlKey: true }));
    expect(workspace.sidebarOpen()).toBe(false);

    shortcuts.handle(press('4', { ctrlKey: true }));
    expect(workspace.sidebarOpen()).toBe(true);
    expect(navigate).toHaveBeenCalledWith(['/', 'git']);
  });

  it('opens the shortcuts dialog with F1', () => {
    setup();
    shortcuts.handle(press('F1'));
    expect(dialogs.open).toHaveBeenCalled();
  });

  it('quits without asking when nothing is unsaved', async () => {
    setup();
    openRequests(1);

    shortcuts.handle(press('q', { ctrlKey: true }));

    await vi.waitFor(() => expect(send).toHaveBeenCalledWith('app.quit'));
    expect(dialogs.confirm).not.toHaveBeenCalled();
  });

  it('asks before quitting with unsaved changes, and stays when declined', async () => {
    setup();
    workspace.workspace.set({ name: 'api', root: '/r' });
    workspace.newRequest();
    dialogs.confirm.mockResolvedValue(false);

    shortcuts.handle(press('q', { ctrlKey: true }));

    await vi.waitFor(() => expect(dialogs.confirm).toHaveBeenCalled());
    expect(dialogs.confirm.mock.calls[0][0]).toContain('1 open tab(s) have unsaved changes');
    expect(send).not.toHaveBeenCalledWith('app.quit');
  });

  it('offers no quit shortcut in the browser preview', () => {
    setup(false);
    const event = press('q', { ctrlKey: true });

    shortcuts.handle(event);

    expect(event.defaultPrevented).toBe(false);
    expect(send).not.toHaveBeenCalled();
  });
});

describe('formatCombo', () => {
  it('spells combinations out on Windows and Linux', () => {
    expect(formatCombo({ key: 'Tab', mod: true, shift: true }, false)).toBe('Ctrl+Shift+Tab');
    expect(formatCombo({ key: 'w', mod: true }, false)).toBe('Ctrl+W');
    expect(formatCombo({ key: 'F1' }, false)).toBe('F1');
  });

  it('uses the modifier symbols on macOS', () => {
    expect(formatCombo({ key: 'q', mod: true }, true)).toBe('⌘Q');
  });
});
