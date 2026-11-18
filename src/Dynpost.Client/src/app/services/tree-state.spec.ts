import { Injectable, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HOVER_EXPAND_DELAY_MS, TreeItem, TreeState, canMove, folderName } from './tree-state';

@Injectable()
class TestTreeState extends TreeState {
  readonly root = '';
  readonly items = signal<TreeItem[]>([]);
  readonly moved = vi.fn().mockResolvedValue(true);
  protected createFolder = vi.fn().mockResolvedValue(true);
  protected renameFolder = vi.fn().mockResolvedValue(true);
  protected move(node: TreeItem, folder: string): Promise<boolean> {
    return this.moved(node, folder);
  }
}

describe('TreeState', () => {
  let state: TestTreeState;
  let lists: Record<string, HTMLElement>;
  let under: HTMLElement | null;

  const request: TreeItem = { path: 'users/get-user.yaml', kind: 'request' };
  const folder: TreeItem = { path: 'users', kind: 'folder' };

  /** Moves the pointer during a drag over the drop list of `path` (or off the tree for `null`). */
  function pointAt(path: string | null): void {
    under = path === null ? null : lists[path];
    window.dispatchEvent(new MouseEvent('pointermove', { clientX: 1, clientY: 1 }));
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [TestTreeState] });
    state = TestBed.inject(TestTreeState);
    lists = {};
    for (const path of ['', 'users', 'users/admin', 'orders']) {
      const element = document.createElement('div');
      lists[path] = element;
      state.registerDropList(element, () => path);
    }
    under = null;
    document.elementFromPoint = () => under;
  });

  afterEach(() => vi.useRealTimers());

  it('describes what dropping would do', () => {
    expect(state.dropHint()).toBeNull();
    state.startDrag(request);

    pointAt(null);
    expect(state.dropHint()).toEqual({ kind: 'none', reason: 'outside' });
    pointAt('users');
    expect(state.dropHint()).toEqual({ kind: 'none', reason: 'same' });
    pointAt('orders');
    expect(state.dropHint()).toEqual({ kind: 'move', folder: 'orders', count: 1 });
    expect(state.dropTarget()).toBe('orders');
  });

  it('refuses moving a folder into itself', () => {
    state.startDrag(folder);
    pointAt('users/admin');
    expect(state.dropHint()).toEqual({ kind: 'none', reason: 'inside' });
    expect(state.dropTarget()).toBeNull();
  });

  it('offers the root only for entries inside a folder', () => {
    expect(state.canDropAtRoot()).toBe(false);
    state.startDrag(request);
    expect(state.canDropAtRoot()).toBe(true);
    state.dragged.set({ path: 'top.yaml', kind: 'request' });
    expect(state.canDropAtRoot()).toBe(false);
  });

  it('moves the entry into the hovered folder on drop', async () => {
    state.startDrag(request);
    pointAt('');
    await state.endDrag();
    expect(state.moved).toHaveBeenCalledWith(request, '');
    expect(state.dragged()).toBeNull();
  });

  it('opens a collapsed folder the pointer rests on', () => {
    vi.useFakeTimers();
    state.toggle({ path: 'orders', kind: 'folder' });
    state.startDrag(request);

    pointAt('orders');
    vi.advanceTimersByTime(HOVER_EXPAND_DELAY_MS - 1);
    expect(state.isExpanded({ path: 'orders', kind: 'folder' })).toBe(false);
    vi.advanceTimersByTime(1);
    expect(state.isExpanded({ path: 'orders', kind: 'folder' })).toBe(true);
  });

  it('does not open a folder the pointer only passes over', async () => {
    vi.useFakeTimers();
    state.toggle({ path: 'orders', kind: 'folder' });
    state.startDrag(request);

    pointAt('orders');
    pointAt('');
    vi.advanceTimersByTime(HOVER_EXPAND_DELAY_MS);
    expect(state.isExpanded({ path: 'orders', kind: 'folder' })).toBe(false);

    // A refused move: a successful one opens the destination on purpose.
    state.moved.mockResolvedValue(false);
    pointAt('orders');
    await state.endDrag();
    vi.advanceTimersByTime(HOVER_EXPAND_DELAY_MS);
    expect(state.isExpanded({ path: 'orders', kind: 'folder' })).toBe(false);
  });
});

describe('TreeState selection', () => {
  let state: TestTreeState;
  let under: HTMLElement | null;
  const lists: Record<string, HTMLElement> = {};

  const users: TreeItem = {
    path: 'users',
    kind: 'folder',
    children: [
      { path: 'users/admin', kind: 'folder', children: [] },
      { path: 'users/get-user.yaml', kind: 'request' },
    ],
  };
  const orders: TreeItem = { path: 'orders', kind: 'folder', children: [] };
  const top: TreeItem = { path: 'top.yaml', kind: 'request' };
  const getUser = users.children![1];
  const paths = () => [...state.selected()].sort();

  function pointAt(path: string | null): void {
    under = path === null ? null : lists[path];
    window.dispatchEvent(new MouseEvent('pointermove', { clientX: 1, clientY: 1 }));
  }

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [TestTreeState] });
    state = TestBed.inject(TestTreeState);
    state.items.set([users, orders, top]);
    for (const path of ['', 'users', 'users/admin', 'orders']) {
      lists[path] = document.createElement('div');
      state.registerDropList(lists[path], () => path);
    }
    under = null;
    document.elementFromPoint = () => under;
  });

  it('selects one entry on a plain click and opens it', () => {
    expect(state.select(top)).toBe(true);
    expect(paths()).toEqual(['top.yaml']);
    expect(state.isSelected(top)).toBe(false);
  });

  it('adds and removes entries with Ctrl or Cmd', () => {
    state.select(top);
    expect(state.select(getUser, { ctrlKey: true })).toBe(false);
    expect(paths()).toEqual(['top.yaml', 'users/get-user.yaml']);
    expect(state.isSelected(top)).toBe(true);
    state.select(top, { metaKey: true });
    expect(paths()).toEqual(['users/get-user.yaml']);
  });

  it('selects the visible range from the last clicked entry with Shift', () => {
    state.select(users.children![0]);
    expect(state.select(top, { shiftKey: true })).toBe(false);
    expect(paths()).toEqual(['orders', 'top.yaml', 'users/admin', 'users/get-user.yaml']);

    state.toggle(users);
    state.select(users);
    state.select(top, { shiftKey: true });
    expect(paths()).toEqual(['orders', 'top.yaml', 'users']);
  });

  it('drags the whole selection, leaving out what a selected folder carries', () => {
    state.select(users);
    state.select(getUser, { ctrlKey: true });
    state.select(top, { ctrlKey: true });
    state.startDrag(top);
    expect(state.draggedNodes().map((n) => n.path)).toEqual(['users', 'top.yaml']);
    expect(state.isDragging(users)).toBe(true);
    expect(state.isDragging(getUser)).toBe(false);
  });

  it('drags only the entry when it is not selected', () => {
    state.select(top);
    state.select(orders, { ctrlKey: true });
    state.startDrag(getUser);
    expect(state.draggedNodes()).toEqual([getUser]);
  });

  it('moves every dragged entry that is not already there', async () => {
    state.select(getUser);
    state.select(top, { ctrlKey: true });
    state.startDrag(getUser);

    pointAt('users');
    expect(state.dropHint()).toEqual({ kind: 'move', folder: 'users', count: 1 });
    pointAt('orders');
    expect(state.dropHint()).toEqual({ kind: 'move', folder: 'orders', count: 2 });
    await state.endDrag();

    expect(state.moved).toHaveBeenCalledTimes(2);
    expect(state.moved).toHaveBeenCalledWith(getUser, 'orders');
    expect(state.moved).toHaveBeenCalledWith(top, 'orders');
    expect(state.selected().size).toBe(0);
  });

  it('refuses dropping a selection into one of its folders', () => {
    state.select(users);
    state.select(top, { ctrlKey: true });
    state.startDrag(top);
    pointAt('users/admin');
    expect(state.dropHint()).toEqual({ kind: 'none', reason: 'inside' });
    expect(state.canEnter(top, 'users/admin', lists['users/admin'])).toBe(false);
    pointAt('');
    expect(state.dropHint()).toEqual({ kind: 'none', reason: 'same' });
    pointAt('orders');
    expect(state.dropHint()).toEqual({ kind: 'move', folder: 'orders', count: 2 });
  });

  it('clears the selection', () => {
    state.select(top);
    state.select(orders, { ctrlKey: true });
    state.clearSelection();
    expect(state.selected().size).toBe(0);
    expect(state.isSelected(orders)).toBe(false);
  });
});

describe('tree paths', () => {
  it('names a folder after its last segment', () => {
    expect(folderName('users/admin')).toBe('admin');
    expect(folderName('users')).toBe('users');
  });

  it('allows moves only to another folder outside the entry', () => {
    expect(canMove({ path: 'a/b.yaml', kind: 'request' }, '')).toBe(true);
    expect(canMove({ path: 'a/b.yaml', kind: 'request' }, 'a')).toBe(false);
    expect(canMove({ path: 'a', kind: 'folder' }, 'a/c')).toBe(false);
  });
});
