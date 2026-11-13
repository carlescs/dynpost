import { Component, HostListener, inject, signal } from '@angular/core';
import { CdkDropListGroup } from '@angular/cdk/drag-drop';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faArrowUpRightFromSquare,
  faArrowsRotate,
  faClone,
  faEllipsis,
  faFileExport,
  faFileImport,
  faFolderOpen,
  faFolderPlus,
  faLaptop,
  faPlus,
  faShareNodes,
  faSliders,
  faTrash,
} from '@fortawesome/free-solid-svg-icons';
import { revealLabel } from '../../../../models/reveal-label';
import { InterchangeStore } from '../../../../services/interchange-store';
import { FolderSettingsStore } from '../../../../services/folder-settings-store';
import { REQUESTS_FOLDER } from '../../../../models/workspace';
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
  private readonly folders = inject(FolderSettingsStore);
  protected readonly root = REQUESTS_FOLDER;
  protected readonly faSliders = faSliders;
  protected readonly exportOpen = signal(false);
  protected readonly moreOpen = signal(false);
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
  protected readonly faEllipsis = faEllipsis;
  protected readonly faArrowUpRightFromSquare = faArrowUpRightFromSquare;
  protected readonly revealLabel = revealLabel(navigator.userAgent);

  @HostListener('document:click')
  @HostListener('document:contextmenu')
  @HostListener('document:keydown.escape')
  @HostListener('window:blur')
  protected closeMenu(): void {
    this.state.closeMenu();
    this.moreOpen.set(false);
  }

  protected readonly headerButton =
    'flex size-6 items-center justify-center rounded text-slate-500 hover:bg-slate-200 hover:text-slate-800 disabled:opacity-50';

  protected toggleMore(): void {
    this.moreOpen.update((open) => !open);
    this.exportOpen.set(false);
    this.state.closeMenu();
  }

  /** Opens the headers and auth a folder (or, for `requests`, the whole workspace) sets. */
  protected openFolderSettings(path: string): void {
    this.closeMenu();
    void this.folders.open(path);
  }

  protected reveal(): void {
    this.moreOpen.set(false);
    void this.store.revealFolder();
  }

  protected toggleExport(): void {
    this.exportOpen.update((open) => !open);
    if (this.exportOpen()) void this.interchange.loadFormats();
  }

  protected async export(): Promise<void> {
    this.exportOpen.set(false);
    await this.interchange.export(this.selectedFormat(), this.includeEnvironments());
  }
}
