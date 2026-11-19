import { Component, HostListener, inject } from '@angular/core';
import { CdkDropListGroup } from '@angular/cdk/drag-drop';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faArrowsRotate,
  faClone,
  faCodeCompare,
  faFolderPlus,
  faLaptop,
  faPlus,
  faShareNodes,
  faTrash,
} from '@fortawesome/free-solid-svg-icons';
import { FolderDrop } from '../../../../components/folder-drop/folder-drop';
import { RunStore } from '../../../../services/run-store';
import { TreeState } from '../../../../services/tree-state';
import { WorkspaceStore } from '../../../../services/workspace-store';
import { RunTreeState } from '../../services/run-tree-state';
import { RunTreeLevel } from '../run-tree-level/run-tree-level';
import { RunTreeNode } from '../../models/run-tree';
import { CompareItem, CompareStore } from '../../../../services/compare-store';

/**
 * Lists the workspace's run plans (`*.run.yaml`) in their folders; click one to edit and run it.
 * Folders can be created, renamed, duplicated and deleted here, and plans and folders dragged
 * into other folders.
 */
@Component({
  selector: 'app-runs-view',
  imports: [CdkDropListGroup, FaIconComponent, FolderDrop, RunTreeLevel],
  providers: [RunTreeState, { provide: TreeState, useExisting: RunTreeState }],
  host: { class: 'relative flex min-h-0 flex-1 flex-col' },
  templateUrl: './runs-view.html',
})
export class RunsView {
  protected readonly store = inject(RunStore);
  protected readonly state = inject(RunTreeState);
  protected readonly workspace = inject(WorkspaceStore);
  protected readonly compare = inject(CompareStore);
  protected readonly faPlus = faPlus;
  protected readonly faFolderPlus = faFolderPlus;
  protected readonly faArrowsRotate = faArrowsRotate;
  protected readonly faClone = faClone;
  protected readonly faCodeCompare = faCodeCompare;
  protected readonly faLaptop = faLaptop;
  protected readonly faShareNodes = faShareNodes;
  protected readonly faTrash = faTrash;
  protected readonly headerButton =
    'flex size-6 items-center justify-center rounded text-slate-500 hover:bg-slate-200 hover:text-slate-800';

  protected compareItem(node: RunTreeNode): CompareItem {
    return { kind: 'plan', path: node.path, name: node.name };
  }

  /** The two run plans selected with Ctrl or Shift, when `node` is one of them. */
  protected selectedPair(node: RunTreeNode): [CompareItem, CompareItem] | null {
    const selection = this.state.selection();
    if (selection.length !== 2 || selection.some((n) => n.kind !== 'plan')) return null;
    if (!selection.some((n) => n.path === node.path)) return null;
    return [this.compareItem(selection[0]), this.compareItem(selection[1])];
  }

  protected selectForCompare(node: RunTreeNode): void {
    this.state.closeMenu();
    this.compare.select(this.compareItem(node));
  }

  protected compareWithSelected(node: RunTreeNode): void {
    this.state.closeMenu();
    void this.compare.compareWithSelected(this.compareItem(node));
  }

  protected compareSelection([left, right]: [CompareItem, CompareItem]): void {
    this.state.closeMenu();
    void this.compare.compare(left, right);
  }

  @HostListener('document:click')
  @HostListener('document:contextmenu')
  @HostListener('document:keydown.escape')
  @HostListener('window:blur')
  protected closeMenu(): void {
    this.state.closeMenu();
  }
}
