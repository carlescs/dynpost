import { Injectable, Signal, computed, signal } from '@angular/core';

/** An entry of a folder tree: a folder, or a file of some kind. */
export interface TreeItem {
  /** Workspace-relative path; folders are the drop targets, as is the tree's `root` folder. */
  path: string;
  kind: string;
  /** Only for folders: what is inside. */
  children?: TreeItem[];
}

/**
 * What dropping the dragged nodes now would do: move `count` of them into `folder`, or nothing —
 * because the pointer is not over the tree (`outside`), they are all already there (`same`), or
 * the folder is one of them or inside one (`inside`).
 */
export type DropHint =
  | { kind: 'move'; folder: string; count: number }
  | { kind: 'none'; reason: 'outside' | 'same' | 'inside' };

/** The background of a selected row: the open entry's tint, with an accent bar on the left. */
const SELECTED_ROW = 'bg-sky-100 shadow-[inset_2px_0_0_var(--color-sky-600)]';

/** The modifier keys of a click that change how it selects. */
export interface SelectModifiers {
  ctrlKey?: boolean;
  metaKey?: boolean;
  shiftKey?: boolean;
}

/** How long the pointer has to rest on a collapsed folder during a drag for it to open. */
export const HOVER_EXPAND_DELAY_MS = 700;

/** A folder name being typed in the tree: a new folder inside `parent`, or a rename of `path`. */
export type Editing = { kind: 'create'; parent: string } | { kind: 'rename'; path: string };

/**
 * State shared by the components of a folder tree (the explorer's and the Load runs view's):
 * expanded folders, inline name editing and drag and drop. Each tree provides its own subclass,
 * which says how to create, rename and move entries, and also provides it as `TreeState` so the
 * shared `FolderDrop` directive finds it.
 *
 * Entries can be selected several at a time — Ctrl (Cmd on macOS) + click adds or removes one,
 * Shift + click selects the range from the last clicked entry — and dragging a selected entry moves
 * the whole selection.
 *
 * Drop targets are folders (and the tree's top level, its `root` folder). Folders nest, so their drop lists nest too;
 * the CDK picks the first connected list containing the pointer, which for nested lists may be
 * an outer one. To always target the folder the pointer is actually over, the tree tracks the
 * pointer during a drag and only the innermost drop list under it accepts the item.
 */
@Injectable()
export abstract class TreeState<T extends TreeItem = TreeItem> {
  readonly editing = signal<Editing | null>(null);
  readonly busy = signal(false);
  private readonly collapsed = signal(new Set<string>());

  /** The workspace folder the tree shows the contents of (such as `requests`): its top level. */
  abstract readonly root: string;
  /** The entries of the tree's top level, each folder with its `children`. */
  abstract readonly items: Signal<T[]>;

  /** Creates a folder; resolves to whether it worked. */
  protected abstract createFolder(parent: string, name: string): Promise<boolean>;
  /** Renames a folder; resolves to whether it worked. */
  protected abstract renameFolder(path: string, name: string): Promise<boolean>;
  /** Moves an entry into `folder`; resolves to whether it worked. */
  protected abstract move(node: T, folder: string): Promise<boolean>;

  /**
   * Paths of the selected entries. A plain click selects the entry clicked, so a drag that starts
   * from it moves only it.
   */
  readonly selected = signal<ReadonlySet<string>>(new Set());
  /**
   * Whether the selection was made with Ctrl or Shift, so the rows show it; a plain click's
   * selection isn't shown (the open entry is highlighted instead).
   */
  readonly multiSelecting = signal(false);
  /** The entry a Shift + click selects the range from: the last one clicked. */
  private anchor: string | null = null;

  /** The node the drag started on, or `null`. */
  readonly dragged = signal<T | null>(null);
  /**
   * The nodes being dragged: the selection when the drag started on a selected entry, else that
   * entry alone. Entries inside a selected folder are left out, as they move with it.
   */
  readonly draggedNodes = computed<T[]>(() => {
    const node = this.dragged();
    if (!node) return [];
    const selected = this.selected();
    if (!selected.has(node.path)) return [node];
    const nodes = flatten(this.items()).filter((item) => selected.has(item.path));
    const folders = nodes.filter((item) => item.kind === 'folder').map((item) => item.path);
    const outermost = nodes.filter((item) => !folders.some((f) => isWithin(item.path, f)));
    return outermost.some((item) => item.path === node.path) ? outermost : [node];
  });
  /** The folder path (`root` for the top level) whose drop list is under the pointer, or `null`. */
  readonly hovered = signal<string | null>(null);
  /** What dropping now would do, for the drag preview; `null` when nothing is being dragged. */
  readonly dropHint = computed<DropHint | null>(() => {
    const nodes = this.draggedNodes();
    if (!nodes.length) return null;
    const target = this.hovered();
    if (target === null) return { kind: 'none', reason: 'outside' };
    return dropHintFor(nodes, target);
  });
  /** The folder the dragged nodes would move into if dropped now, or `null` for no move. */
  readonly dropTarget = computed(() => {
    const hint = this.dropHint();
    return hint?.kind === 'move' ? hint.folder : null;
  });
  /** Whether some dragged node can move to the top level (it is not already there). */
  readonly canDropAtRoot = computed(() => {
    const nodes = this.draggedNodes();
    return nodes.length > 0 && dropHintFor(nodes, this.root).kind === 'move';
  });

  /** Drop list elements of the tree, with a getter for the folder each one stands for. */
  private readonly dropLists = new Map<Element, () => string>();
  private pointer = { x: 0, y: 0 };
  private readonly trackPointer = (event: PointerEvent) => {
    this.pointer = { x: event.clientX, y: event.clientY };
    const folder = this.folderAt(event.clientX, event.clientY);
    this.hovered.set(folder);
    this.scheduleExpand(folder);
  };
  /** The collapsed folder that opens if the pointer stays on it, and its timer. */
  private expandCandidate: string | null = null;
  private expandTimer: ReturnType<typeof setTimeout> | undefined;

  isExpanded(node: TreeItem): boolean {
    return !this.collapsed().has(node.path);
  }

  toggle(node: TreeItem): void {
    this.collapsed.update((set) => {
      const next = new Set(set);
      if (next.has(node.path)) next.delete(node.path);
      else next.add(node.path);
      return next;
    });
  }

  expand(path: string): void {
    this.collapsed.update((set) => {
      const next = new Set(set);
      next.delete(path);
      return next;
    });
  }

  /** Whether `node`'s row shows it as selected. */
  isSelected(node: TreeItem): boolean {
    return this.multiSelecting() && this.selected().has(node.path);
  }

  /**
   * The background classes of `node`'s row. A row has one highlight at a time: while entries are
   * selected with Ctrl or Shift only those are, with an accent bar along their left edge, and the
   * open entry's highlight (`active`) steps aside so the two can't be confused; otherwise the open
   * entry, or the entry whose menu is open. Plain rows darken on hover, except during a drag.
   */
  rowClasses(node: TreeItem, active: boolean, menuOpen = false): string {
    if (this.multiSelecting()) {
      if (this.selected().has(node.path)) return SELECTED_ROW;
    } else if (active) {
      return 'bg-sky-100';
    }
    if (menuOpen) return 'bg-slate-200';
    return this.dragged() ? '' : 'hover:bg-slate-200';
  }

  /** Whether `node` is one of the entries being dragged, so its row can be dimmed. */
  isDragging(node: TreeItem): boolean {
    return this.draggedNodes().some((item) => item.path === node.path);
  }

  /**
   * Selects an entry for a click: on its own for a plain click, adding or removing it with Ctrl
   * (Cmd), or the visible range from the last clicked entry with Shift. Returns whether it was a
   * plain click, which also opens the entry.
   */
  select(node: TreeItem, modifiers: SelectModifiers = {}): boolean {
    if (modifiers.shiftKey) {
      const visible = this.visible();
      const from = visible.findIndex((item) => item.path === this.anchor);
      const to = visible.findIndex((item) => item.path === node.path);
      if (from >= 0 && to >= 0) {
        const range = visible.slice(Math.min(from, to), Math.max(from, to) + 1);
        const paths = range.map((item) => item.path);
        const add = modifiers.ctrlKey || modifiers.metaKey;
        this.selected.update((set) => new Set([...(add ? set : []), ...paths]));
        this.multiSelecting.set(true);
        return false;
      }
    }
    if (modifiers.ctrlKey || modifiers.metaKey) {
      this.selected.update((set) => {
        const next = new Set(set);
        if (next.has(node.path)) next.delete(node.path);
        else next.add(node.path);
        return next;
      });
      this.anchor = node.path;
      this.multiSelecting.set(true);
      return false;
    }
    this.selected.set(new Set([node.path]));
    this.anchor = node.path;
    this.multiSelecting.set(false);
    return true;
  }

  clearSelection(): void {
    this.selected.set(new Set());
    this.anchor = null;
    this.multiSelecting.set(false);
  }

  isCreatingIn(parent: string): boolean {
    const editing = this.editing();
    return editing?.kind === 'create' && editing.parent === parent;
  }

  isRenaming(node: TreeItem): boolean {
    const editing = this.editing();
    return editing?.kind === 'rename' && editing.path === node.path;
  }

  startCreate(parent: string): void {
    this.expand(parent);
    this.editing.set({ kind: 'create', parent });
  }

  startRename(node: TreeItem): void {
    this.editing.set({ kind: 'rename', path: node.path });
  }

  async commitCreate(parent: string, name: string, viaBlur: boolean): Promise<void> {
    await this.run(() => this.createFolder(parent, name), viaBlur);
  }

  async commitRename(path: string, name: string, viaBlur: boolean): Promise<void> {
    const wasCollapsed = this.collapsed().has(path);
    const renamed = await this.run(() => this.renameFolder(path, name), viaBlur);
    if (renamed && wasCollapsed) {
      this.collapsed.update((set) => new Set([...set, joinPath(parentPath(path), name)]));
    }
  }

  registerDropList(element: Element, folder: () => string): void {
    this.dropLists.set(element, folder);
  }

  unregisterDropList(element: Element): void {
    this.dropLists.delete(element);
  }

  /** Whether the drop list `element` (standing for `folder`) may receive the dragged nodes now. */
  canEnter(node: T, folder: string, element: Element): boolean {
    const nodes = this.dragged() ? this.draggedNodes() : [node];
    return (
      dropHintFor(nodes, folder).kind === 'move' &&
      this.innermostListAt(this.pointer.x, this.pointer.y) === element
    );
  }

  startDrag(node: T, event?: PointerEvent | MouseEvent | TouchEvent): void {
    if (event && 'clientX' in event) this.pointer = { x: event.clientX, y: event.clientY };
    this.dragged.set(node);
    // Capture phase on window runs before the CDK's own pointer handling.
    window.addEventListener('pointermove', this.trackPointer, { capture: true });
  }

  /**
   * Ends a drag; moves the dragged nodes into the folder under the pointer, if that is a valid
   * move. Nodes already in that folder stay; the others move one at a time, and a move that fails
   * (the store reports why) doesn't stop the rest.
   */
  async endDrag(): Promise<void> {
    window.removeEventListener('pointermove', this.trackPointer, { capture: true });
    this.scheduleExpand(null);
    const target = this.dropTarget();
    const nodes = target === null ? [] : this.draggedNodes().filter((n) => canMove(n, target));
    this.dragged.set(null);
    this.hovered.set(null);
    if (target === null || !nodes.length) return;
    let moved = false;
    for (const node of nodes) moved = (await this.move(node, target)) || moved;
    if (!moved) return;
    this.expand(target);
    // The moved entries have new paths; a selection of the old ones would be stale.
    if (nodes.length > 1) this.clearSelection();
  }

  /**
   * Opens a collapsed folder the pointer rests on during a drag, so entries can be dropped into
   * its sub-folders without first leaving the drag to expand it.
   */
  private scheduleExpand(folder: string | null): void {
    if (folder === this.expandCandidate) return;
    clearTimeout(this.expandTimer);
    this.expandTimer = undefined;
    this.expandCandidate = folder;
    const nodes = this.draggedNodes();
    if (!folder || !nodes.length || !this.collapsed().has(folder)) return;
    if (dropHintFor(nodes, folder).kind !== 'move') return;
    this.expandTimer = setTimeout(() => {
      this.expandTimer = undefined;
      if (this.dragged() && this.hovered() === folder) this.expand(folder);
    }, HOVER_EXPAND_DELAY_MS);
  }

  /** The entries shown now, in tree order: those inside collapsed folders are left out. */
  private visible(): TreeItem[] {
    const result: TreeItem[] = [];
    const walk = (nodes: TreeItem[]) => {
      for (const node of nodes) {
        result.push(node);
        if (node.kind === 'folder' && this.isExpanded(node)) walk(node.children ?? []);
      }
    };
    walk(this.items());
    return result;
  }

  private folderAt(x: number, y: number): string | null {
    const list = this.innermostListAt(x, y);
    return list ? (this.dropLists.get(list)?.() ?? null) : null;
  }

  private innermostListAt(x: number, y: number): Element | null {
    let element = document.elementFromPoint(x, y);
    while (element && !this.dropLists.has(element)) element = element.parentElement;
    return element;
  }

  /**
   * Runs a create or rename. On failure the name box stays open so the name can be fixed — unless
   * the user had already moved on (left the box), in which case it closes and the error banner
   * explains what went wrong.
   */
  private async run(action: () => Promise<boolean>, viaBlur: boolean): Promise<boolean> {
    // A late blur from a box that is closing (or already submitting) must not submit again.
    if (!this.editing() || this.busy()) return false;
    this.busy.set(true);
    try {
      const ok = await action();
      if (ok || viaBlur) this.editing.set(null);
      return ok;
    } finally {
      this.busy.set(false);
    }
  }
}

/** Whether `node` can move into `folder`: not where it already is, and not into itself. */
export function canMove(node: TreeItem, folder: string): boolean {
  if (folder === parentPath(node.path)) return false;
  return node.kind !== 'folder' || !(folder === node.path || folder.startsWith(`${node.path}/`));
}

/**
 * What dropping `nodes` on `folder` would do: nothing when the folder is one of them or inside
 * one, or when all of them are already there; otherwise move those that aren't.
 */
export function dropHintFor(nodes: TreeItem[], folder: string): DropHint {
  const inside = nodes.some(
    (node) => node.kind === 'folder' && (folder === node.path || isWithin(folder, node.path)),
  );
  if (inside) return { kind: 'none', reason: 'inside' };
  const count = nodes.filter((node) => canMove(node, folder)).length;
  return count ? { kind: 'move', folder, count } : { kind: 'none', reason: 'same' };
}

/** Every entry of a tree, folders before their contents. */
function flatten<T extends TreeItem>(nodes: T[]): T[] {
  return nodes.flatMap((node) => [node, ...flatten((node.children ?? []) as T[])]);
}

/** Whether `path` is inside the folder `folder`. */
function isWithin(path: string, folder: string): boolean {
  return path.startsWith(`${folder}/`);
}

/** The display name of a folder: its last path segment. */
export function folderName(path: string): string {
  return path.slice(path.lastIndexOf('/') + 1);
}

/** The folder a path is in; `''` for a top-level path. */
export function parentPath(path: string): string {
  return path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '';
}

/** The path of `name` inside `folder` (`''` for a top-level path). */
export function joinPath(folder: string, name: string): string {
  return folder ? `${folder}/${name}` : name;
}
