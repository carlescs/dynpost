import { Component, HostListener, inject } from '@angular/core';
import { CdkDropListGroup } from '@angular/cdk/drag-drop';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faArrowsRotate,
  faClone,
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
  protected readonly faPlus = faPlus;
  protected readonly faFolderPlus = faFolderPlus;
  protected readonly faArrowsRotate = faArrowsRotate;
  protected readonly faClone = faClone;
  protected readonly faLaptop = faLaptop;
  protected readonly faShareNodes = faShareNodes;
  protected readonly faTrash = faTrash;
  protected readonly headerButton =
    'flex size-6 items-center justify-center rounded text-slate-500 hover:bg-slate-200 hover:text-slate-800';

  @HostListener('document:click')
  @HostListener('document:contextmenu')
  @HostListener('document:keydown.escape')
  @HostListener('window:blur')
  protected closeMenu(): void {
    this.state.closeMenu();
  }
}
