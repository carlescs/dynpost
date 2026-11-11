import { Injectable } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HOVER_EXPAND_DELAY_MS, TreeItem, TreeState, canMove, folderName } from './tree-state';

@Injectable()
class TestTreeState extends TreeState {
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
    expect(state.dropHint()).toEqual({ kind: 'move', folder: 'orders' });
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
