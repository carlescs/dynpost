import { Component, inject, input } from '@angular/core';
import { CdkDrag, CdkDragPlaceholder, CdkDragPreview, CdkDragStart } from '@angular/cdk/drag-drop';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faChevronDown,
  faChevronRight,
  faCircleExclamation,
  faFolder,
  faFolderOpen,
  faFolderPlus,
  faGaugeHigh,
  faPen,
  faPlus,
  faTrash,
} from '@fortawesome/free-solid-svg-icons';
import { FolderDrop } from '../../../../components/folder-drop/folder-drop';
import { NameInput } from '../../../../components/name-input/name-input';
import { SharingToggle } from '../../../../components/sharing-toggle/sharing-toggle';
import { RunTreeNode } from '../../models/run-tree';
import { RunTreeState } from '../../services/run-tree-state';

/**
 * One level of the Load runs tree: the folders and plans of a folder. Folders render their own
 * level recursively. Every entry can be dragged onto a folder (or the workspace root) to move it
 * there; like the explorer's tree, this is a component so each `cdkDrag` finds its folder's drop
 * list through dependency injection.
 */
@Component({
  selector: 'ul[app-run-tree-level]',
  imports: [
    CdkDrag,
    CdkDragPlaceholder,
    CdkDragPreview,
    FaIconComponent,
    FolderDrop,
    NameInput,
    SharingToggle,
    RunTreeLevel,
  ],
  templateUrl: './run-tree-level.html',
})
export class RunTreeLevel {
  readonly nodes = input.required<RunTreeNode[]>();
  readonly depth = input(0);
  /** Path of the folder these entries are in; `''` for the workspace root. */
  readonly parent = input('');

  protected readonly state = inject(RunTreeState);

  protected readonly faCircleExclamation = faCircleExclamation;
  protected readonly faFolder = faFolder;
  protected readonly faFolderOpen = faFolderOpen;
  protected readonly faFolderPlus = faFolderPlus;
  protected readonly faGaugeHigh = faGaugeHigh;
  protected readonly faPen = faPen;
  protected readonly faPlus = faPlus;
  protected readonly faTrash = faTrash;

  protected readonly rowButton =
    'flex size-5 items-center justify-center rounded text-xs text-slate-500 hover:bg-slate-300 hover:text-slate-800';

  protected indent(): number {
    return 12 + this.depth() * 16;
  }

  protected chevron(node: RunTreeNode) {
    return this.state.isExpanded(node) ? faChevronDown : faChevronRight;
  }

  protected dragStarted(node: RunTreeNode, event: CdkDragStart<RunTreeNode>): void {
    this.state.startDrag(node, event.event);
  }

  protected onPlanKeydown(event: KeyboardEvent, node: RunTreeNode): void {
    if (event.key === 'Delete') {
      event.preventDefault();
      void this.state.store.delete(node.path);
    }
  }

  protected onFolderKeydown(event: KeyboardEvent, node: RunTreeNode): void {
    if (event.key === 'F2') {
      event.preventDefault();
      this.state.startRename(node);
    } else if (event.key === 'Delete') {
      event.preventDefault();
      void this.state.store.deleteFolder(node.path);
    }
  }
}
