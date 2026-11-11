import { Injectable, inject, signal } from '@angular/core';
import { WorkspaceNode } from '../../../models/workspace';
import { TreeState } from '../../../services/tree-state';
import { WorkspaceStore } from '../../../services/workspace-store';

export { canMove } from '../../../services/tree-state';

/**
 * State shared by the explorer tree's components: expanded folders, inline name editing, drag and
 * drop (see `TreeState`) and the context menu. Provided by the explorer view, so each explorer
 * has its own.
 */
@Injectable()
export class ExplorerState extends TreeState<WorkspaceNode> {
  readonly store = inject(WorkspaceStore);

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

  /** Keeps a request on this computer only, or shares it through git again. */
  toggleLocalOnly(node: WorkspaceNode): void {
    this.closeMenu();
    if (node.kind === 'request') void this.store.setRequestLocalOnly(node, !node.localOnly);
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
