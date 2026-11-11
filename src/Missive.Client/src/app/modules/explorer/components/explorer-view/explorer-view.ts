import { Component, HostListener, inject, signal } from '@angular/core';
import { CdkDropListGroup } from '@angular/cdk/drag-drop';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faArrowsRotate,
  faClone,
  faFileExport,
  faFileImport,
  faFolderOpen,
  faFolderPlus,
  faLaptop,
  faPlus,
  faShareNodes,
  faTrash,
} from '@fortawesome/free-solid-svg-icons';
import { InterchangeStore } from '../../../../services/interchange-store';
import { WorkspaceStore } from '../../../../services/workspace-store';
import { TreeState } from '../../../../services/tree-state';
import { ExplorerState } from '../../services/explorer-state';
import { FolderDrop } from '../../../../components/folder-drop/folder-drop';
import { TreeLevel } from '../tree-level/tree-level';

@Component({
  selector: 'app-explorer-view',
  imports: [CdkDropListGroup, FaIconComponent, FolderDrop, TreeLevel],
  providers: [ExplorerState, { provide: TreeState, useExisting: ExplorerState }],
  host: {
    class: 'relative flex min-h-0 flex-1 flex-col',
    '(keydown.escape)': 'exportOpen.set(false)',
  },
  templateUrl: './explorer-view.html',
})
export class ExplorerView {
  protected readonly store = inject(WorkspaceStore);
  protected readonly state = inject(ExplorerState);
  protected readonly interchange = inject(InterchangeStore);
  protected readonly exportOpen = signal(false);
  protected readonly selectedFormat = signal('postman');
  protected readonly includeEnvironments = signal(true);
  protected readonly faPlus = faPlus;
  protected readonly faClone = faClone;
  protected readonly faLaptop = faLaptop;
  protected readonly faShareNodes = faShareNodes;
  protected readonly faFileExport = faFileExport;
  protected readonly faFileImport = faFileImport;
  protected readonly faFolderOpen = faFolderOpen;
  protected readonly faFolderPlus = faFolderPlus;
  protected readonly faTrash = faTrash;
  protected readonly faArrowsRotate = faArrowsRotate;

  @HostListener('document:click')
  @HostListener('document:contextmenu')
  @HostListener('document:keydown.escape')
  @HostListener('window:blur')
  protected closeMenu(): void {
    this.state.closeMenu();
  }

  protected readonly headerButton =
    'flex size-6 items-center justify-center rounded text-slate-500 hover:bg-slate-200 hover:text-slate-800 disabled:opacity-50';

  protected toggleExport(): void {
    this.exportOpen.update((open) => !open);
    if (this.exportOpen()) void this.interchange.loadFormats();
  }

  protected async export(): Promise<void> {
    this.exportOpen.set(false);
    await this.interchange.export(this.selectedFormat(), this.includeEnvironments());
  }
}
