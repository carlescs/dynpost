import { Component, inject, input } from '@angular/core';
import { CdkDrag, CdkDragPlaceholder, CdkDragPreview, CdkDragStart } from '@angular/cdk/drag-drop';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faChevronDown,
  faChevronRight,
  faFileImport,
  faFolder,
  faFolderOpen,
  faFolderPlus,
  faPen,
  faPlus,
  faTrash,
  faTriangleExclamation,
} from '@fortawesome/free-solid-svg-icons';
import { InterchangeStore } from '../../../../services/interchange-store';
import { METHOD_COLORS, WorkspaceNode } from '../../../../models/workspace';
import { ExplorerState } from '../../services/explorer-state';
import { FolderDrop } from '../folder-drop/folder-drop';
import { NameInput } from '../name-input/name-input';

/**
 * One level of the explorer tree: the entries of a folder. Folders render their own level
 * recursively. Every entry can be dragged onto a folder (or the workspace root) to move it there.
 *
 * This is a component rather than a recursive `ng-template` so each `cdkDrag` finds the drop list
 * of the folder it is in through dependency injection.
 */
@Component({
  selector: 'ul[app-tree-level]',
  imports: [
    CdkDrag,
    CdkDragPlaceholder,
    CdkDragPreview,
    FaIconComponent,
    FolderDrop,
    NameInput,
    TreeLevel,
  ],
  templateUrl: './tree-level.html',
})
export class TreeLevel {
  readonly nodes = input.required<WorkspaceNode[]>();
  readonly depth = input(0);
  /** Path of the folder these entries are in; `''` for the workspace root. */
  readonly parent = input('');

  protected readonly state = inject(ExplorerState);
  protected readonly interchange = inject(InterchangeStore);

  protected readonly faPlus = faPlus;
  protected readonly faFileImport = faFileImport;
  protected readonly faFolder = faFolder;
  protected readonly faFolderOpen = faFolderOpen;
  protected readonly faFolderPlus = faFolderPlus;
  protected readonly faPen = faPen;
  protected readonly faTrash = faTrash;
  protected readonly faWarning = faTriangleExclamation;

  protected readonly rowButton =
    'flex size-5 items-center justify-center rounded text-xs text-slate-500 hover:bg-slate-300 hover:text-slate-800';

  protected indent(): number {
    return 12 + this.depth() * 16;
  }

  protected chevron(node: WorkspaceNode) {
    return this.state.isExpanded(node) ? faChevronDown : faChevronRight;
  }

  protected methodColor(node: WorkspaceNode): string {
    return METHOD_COLORS[node.method ?? 'GET'];
  }

  protected dragStarted(node: WorkspaceNode, event: CdkDragStart<WorkspaceNode>): void {
    this.state.startDrag(node, event.event);
  }

  protected onRequestKeydown(event: KeyboardEvent, node: WorkspaceNode): void {
    if (event.key === 'Delete') {
      event.preventDefault();
      void this.state.store.deleteRequest(node.path);
    }
  }

  protected onFolderKeydown(event: KeyboardEvent, node: WorkspaceNode): void {
    if (event.key === 'F2') {
      event.preventDefault();
      this.state.startRename(node);
    } else if (event.key === 'Delete') {
      event.preventDefault();
      void this.state.store.deleteFolder(node.path);
    }
  }
}
