import { Injectable, computed, inject, signal } from '@angular/core';
import { WorkspaceNode } from '../../../models/workspace';
import { WorkspaceStore } from '../../../services/workspace-store';

/** A folder name being typed in the tree: a new folder inside `parent`, or a rename of `path`. */
export type Editing = { kind: 'create'; parent: string } | { kind: 'rename'; path: string };

/**
 * State shared by the explorer tree's components: expanded folders, inline name editing and
 * drag and drop. Provided by the explorer view, so each explorer has its own.
 *
 * Drop targets are folders (and the workspace root). Folders nest, so their drop lists nest too;
 * the CDK picks the first connected list containing the pointer, which for nested lists may be
 * an outer one. To always target the folder the pointer is actually over, the explorer tracks the
 * pointer during a drag and only the innermost drop list under it accepts the item.
 */
@Injectable()
export class ExplorerState {
  readonly store = inject(WorkspaceStore);

  readonly editing = signal<Editing | null>(null);
  readonly busy = signal(false);
  private readonly collapsed = signal(new Set<string>());

  /** The explorer entry whose context menu is open, with the pointer position. */
  readonly menu = signal<{ node: WorkspaceNode; x: number; y: number } | null>(null);

  openMenu(node: WorkspaceNode, event: MouseEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.menu.set({ node, x: event.clientX, y: event.clientY });
  }

  closeMenu(): void {
    this.menu.set(null);
  }

  duplicateNode(node: WorkspaceNode): void {
    this.closeMenu();
    if (node.kind === 'folder') void this.store.duplicateFolder(node.path);
    else void this.store.duplicateRequest(node.path);
  }

  deleteNode(node: WorkspaceNode): void {
    this.closeMenu();
    if (node.kind === 'folder') void this.store.deleteFolder(node.path);
    else void this.store.deleteRequest(node.path);
  }

  /** The node being dragged, or `null`. */
  readonly dragged = signal<WorkspaceNode | null>(null);
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

  isExpanded(node: WorkspaceNode): boolean {
    return !this.collapsed().has(node.path);
  }

  toggle(node: WorkspaceNode): void {
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

  isRenaming(node: WorkspaceNode): boolean {
    const editing = this.editing();
    return editing?.kind === 'rename' && editing.path === node.path;
  }

  startCreate(parent: string): void {
    this.expand(parent);
    this.editing.set({ kind: 'create', parent });
  }

  startRename(node: WorkspaceNode): void {
    this.editing.set({ kind: 'rename', path: node.path });
  }

  async commitCreate(parent: string, name: string, viaBlur: boolean): Promise<void> {
    await this.run(
      () => this.store.createFolder(parent, name).then((path) => path !== null),
      viaBlur,
    );
  }

  async commitRename(path: string, name: string, viaBlur: boolean): Promise<void> {
    const wasCollapsed = this.collapsed().has(path);
    const renamed = await this.run(() => this.store.renameFolder(path, name), viaBlur);
    if (renamed && wasCollapsed) {
      this.collapsed.update((set) => new Set([...set, join(parentOf(path), name)]));
    }
  }

  registerDropList(element: Element, folder: () => string): void {
    this.dropLists.set(element, folder);
  }

  unregisterDropList(element: Element): void {
    this.dropLists.delete(element);
  }

  /** Whether the drop list `element` (standing for `folder`) may receive the dragged node now. */
  canEnter(node: WorkspaceNode, folder: string, element: Element): boolean {
    return (
      canMove(node, folder) && this.innermostListAt(this.pointer.x, this.pointer.y) === element
    );
  }

  startDrag(node: WorkspaceNode, event?: PointerEvent | MouseEvent | TouchEvent): void {
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

    const moved =
      node.kind === 'folder'
        ? await this.store.moveFolder(node.path, target)
        : await this.store.moveRequest(node.path, target);
    if (moved) this.expand(target);
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
export function canMove(node: WorkspaceNode, folder: string): boolean {
  if (folder === parentOf(node.path)) return false;
  return node.kind !== 'folder' || !(folder === node.path || folder.startsWith(`${node.path}/`));
}

function parentOf(path: string): string {
  return path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : '';
}

function join(folder: string, name: string): string {
  return folder ? `${folder}/${name}` : name;
}
