import { Injectable, computed, inject, signal } from '@angular/core';
import { PLANS_FOLDER } from '../../../models/workspace';
import { RunStore } from '../../../services/run-store';
import { TreeState } from '../../../services/tree-state';
import { RunTreeNode, buildRunTree } from '../models/run-tree';

/**
 * State shared by the Load runs view's tree: expanded folders, inline name editing, drag and drop
 * (see `TreeState`) and the context menu. Provided by the view.
 */
@Injectable()
export class RunTreeState extends TreeState<RunTreeNode> {
  readonly store = inject(RunStore);
  readonly root = PLANS_FOLDER;

  readonly tree = computed(() => buildRunTree(this.store.folders(), this.store.plans(), this.root));

  /** The folder whose context menu is open, with the pointer position. */
  readonly menu = signal<{ node: RunTreeNode; x: number; y: number } | null>(null);

  openMenu(node: RunTreeNode, event: MouseEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.menu.set({ node, x: event.clientX, y: event.clientY });
  }

  closeMenu(): void {
    this.menu.set(null);
  }

  duplicateNode(node: RunTreeNode): void {
    this.closeMenu();
    if (node.kind === 'folder') void this.store.duplicateFolder(node.path);
  }

  /** Keeps a plan on this computer only, or shares it through git again. */
  toggleLocalOnly(node: RunTreeNode): void {
    this.closeMenu();
    if (node.plan) void this.store.toggleLocalOnly(node.plan);
  }

  deleteNode(node: RunTreeNode): void {
    this.closeMenu();
    if (node.kind === 'folder') void this.store.deleteFolder(node.path);
    else void this.store.delete(node.path);
  }

  /** Creates a plan in `folder` and makes sure the folder is open to show it. */
  newPlan(folder: string): void {
    this.expand(folder);
    void this.store.create(folder);
  }

  protected createFolder(parent: string, name: string): Promise<boolean> {
    return this.store.createFolder(parent, name);
  }

  protected renameFolder(path: string, name: string): Promise<boolean> {
    return this.store.renameFolder(path, name);
  }

  protected move(node: RunTreeNode, folder: string): Promise<boolean> {
    return node.kind === 'folder'
      ? this.store.moveFolder(node.path, folder)
      : this.store.movePlan(node.path, folder);
  }
}
