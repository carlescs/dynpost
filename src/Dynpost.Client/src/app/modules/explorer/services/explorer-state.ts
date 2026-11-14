import { Injectable, inject, signal } from '@angular/core';
import { REQUESTS_FOLDER, WorkspaceNode } from '../../../models/workspace';
import { TreeState } from '../../../services/tree-state';
import { WorkspaceStore } from '../../../services/workspace-store';

export { canMove } from '../../../services/tree-state';

/** An open explorer menu: the entry it acts on and its position in the viewport. */
export interface ExplorerMenu {
  node: WorkspaceNode;
  x: number;
  y: number;
  above: number;
}

/**
 * State shared by the explorer tree's components: expanded folders, inline name editing, drag and
 * drop (see `TreeState`) and the context menu. Provided by the explorer view, so each explorer
 * has its own.
 */
@Injectable()
export class ExplorerState extends TreeState<WorkspaceNode> {
  readonly store = inject(WorkspaceStore);
  readonly root = REQUESTS_FOLDER;

  /**
   * The explorer entry whose menu is open and where: at the pointer for a right-click, or under
   * the row's "more actions" button, with `above` the y to open upwards from when it doesn't fit
   * below.
   */
  readonly menu = signal<ExplorerMenu | null>(null);

  /** Opens an entry's menu at the pointer (right-click). */
  openMenu(node: WorkspaceNode, event: MouseEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.menu.set({ node, x: event.clientX, y: event.clientY, above: event.clientY });
  }

  /** Opens an entry's menu under the button that was clicked, or closes it if it is open. */
  toggleMenuFrom(node: WorkspaceNode, event: MouseEvent): void {
    event.stopPropagation();
    if (this.menu()?.node.path === node.path) {
      this.closeMenu();
      return;
    }
    const button = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.menu.set({ node, x: button.left, y: button.bottom + 2, above: button.top - 2 });
  }

  /** Whether `node`'s menu is open, so its row keeps its buttons shown. */
  isMenuOpen(node: WorkspaceNode): boolean {
    return this.menu()?.node.path === node.path;
  }

  closeMenu(): void {
    this.menu.set(null);
  }

  duplicateNode(node: WorkspaceNode): void {
    this.closeMenu();
    if (node.kind === 'folder') void this.store.duplicateFolder(node.path);
    else void this.store.duplicateRequest(node.path);
  }

  /** Keeps a request on this computer only, or shares it through git again. */
  toggleLocalOnly(node: WorkspaceNode): void {
    this.closeMenu();
    if (node.kind === 'request') void this.store.setRequestLocalOnly(node, !node.localOnly);
  }

  newRequestIn(node: WorkspaceNode): void {
    this.closeMenu();
    this.store.newRequest(node.path);
  }

  newFolderIn(node: WorkspaceNode): void {
    this.closeMenu();
    this.startCreate(node.path);
  }

  rename(node: WorkspaceNode): void {
    this.closeMenu();
    this.startRename(node);
  }

  deleteNode(node: WorkspaceNode): void {
    this.closeMenu();
    if (node.kind === 'folder') void this.store.deleteFolder(node.path);
    else void this.store.deleteRequest(node.path);
  }

  protected createFolder(parent: string, name: string): Promise<boolean> {
    return this.store.createFolder(parent, name).then((path) => path !== null);
  }

  protected renameFolder(path: string, name: string): Promise<boolean> {
    return this.store.renameFolder(path, name);
  }

  protected move(node: WorkspaceNode, folder: string): Promise<boolean> {
    return node.kind === 'folder'
      ? this.store.moveFolder(node.path, folder)
      : this.store.moveRequest(node.path, folder);
  }
}
