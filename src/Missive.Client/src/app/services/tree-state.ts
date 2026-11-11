import { Injectable, computed, signal } from '@angular/core';

/** An entry of a folder tree: a folder, or a file of some kind. */
export interface TreeItem {
  /** Workspace-relative path; folders are the drop targets, `''` being the workspace root. */
  path: string;
  kind: string;
}

/** A folder name being typed in the tree: a new folder inside `parent`, or a rename of `path`. */
export type Editing = { kind: 'create'; parent: string } | { kind: 'rename'; path: string };

/**
 * State shared by the components of a folder tree (the explorer's and the Load runs view's):
 * expanded folders, inline name editing and drag and drop. Each tree provides its own subclass,
 * which says how to create, rename and move entries, and also provides it as `TreeState` so the
 * shared `FolderDrop` directive finds it.
 *
 * Drop targets are folders (and the workspace root). Folders nest, so their drop lists nest too;
 * the CDK picks the first connected list containing the pointer, which for nested lists may be
 * an outer one. To always target the folder the pointer is actually over, the tree tracks the
 * pointer during a drag and only the innermost drop list under it accepts the item.
 */
@Injectable()
export abstract class TreeState<T extends TreeItem = TreeItem> {
  readonly editing = signal<Editing | null>(null);
  readonly busy = signal(false);
  private readonly collapsed = signal(new Set<string>());

  /** Creates a folder; resolves to whether it worked. */
  protected abstract createFolder(parent: string, name: string): Promise<boolean>;
  /** Renames a folder; resolves to whether it worked. */
  protected abstract renameFolder(path: string, name: string): Promise<boolean>;
  /** Moves an entry into `folder`; resolves to whether it worked. */
  protected abstract move(node: T, folder: string): Promise<boolean>;

  /** The node being dragged, or `null`. */
  readonly dragged = signal<T | null>(null);
  /** The folder path (`''` for the root) whose drop list is under the pointer, or `null`. */
  readonly hovered = signal<string | null>(null);
  /** The folder the dragged node would move into if dropped now, or `null` for no move. */
  readonly dropTarget = computed(() => {
    const node = this.dragged();
    const target = this.hovered();
    return node && target !== null && canMove(node, target) ? target : null;
  });

  /** Drop list elements of the tree, with a getter for the folder each one stands for. */
  private readonly dropLists = new Map<Element, () => string>();
  private pointer = { x: 0, y: 0 };
  private readonly trackPointer = (event: PointerEvent) => {
    this.pointer = { x: event.clientX, y: event.clientY };
    this.hovered.set(this.folderAt(event.clientX, event.clientY));
  };

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

  /** Whether the drop list `element` (standing for `folder`) may receive the dragged node now. */
  canEnter(node: T, folder: string, element: Element): boolean {
    return (
      canMove(node, folder) && this.innermostListAt(this.pointer.x, this.pointer.y) === element
    );
  }

  startDrag(node: T, event?: PointerEvent | MouseEvent | TouchEvent): void {
    if (event && 'clientX' in event) this.pointer = { x: event.clientX, y: event.clientY };
    this.dragged.set(node);
    // Capture phase on window runs before the CDK's own pointer handling.
    window.addEventListener('pointermove', this.trackPointer, { capture: true });
  }

  /** Ends a drag; moves the node into the folder under the pointer, if that is a valid move. */
  async endDrag(): Promise<void> {
    window.removeEventListener('pointermove', this.trackPointer, { capture: true });
    const node = this.dragged();
    const target = this.dropTarget();
    this.dragged.set(null);
    this.hovered.set(null);
    if (!node || target === null) return;
    if (await this.move(node, target)) this.expand(target);
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

/** The folder a path is in; `''` for the workspace root. */
export function parentPath(path: string): string {
  return path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '';
}

/** The path of `name` inside `folder` (`''` is the root). */
export function joinPath(folder: string, name: string): string {
  return folder ? `${folder}/${name}` : name;
}
