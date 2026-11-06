import { Component, HostListener, inject, signal } from '@angular/core';
import { CdkDropListGroup } from '@angular/cdk/drag-drop';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faArrowsRotate,
  faFileExport,
  faFileImport,
  faFolderOpen,
  faFolderPlus,
  faPlus,
  faTrash,
} from '@fortawesome/free-solid-svg-icons';
import { InterchangeStore } from '../../../../services/interchange-store';
import { WorkspaceStore } from '../../../../services/workspace-store';
import { ExplorerState } from '../../services/explorer-state';
import { FolderDrop } from '../folder-drop/folder-drop';
import { TreeLevel } from '../tree-level/tree-level';

@Component({
  selector: 'app-explorer-view',
  imports: [CdkDropListGroup, FaIconComponent, FolderDrop, TreeLevel],
  providers: [ExplorerState],
  host: {
    class: 'relative flex min-h-0 flex-1 flex-col',
    '(keydown.escape)': 'exportOpen.set(false)',
  },
  template: `
    <header class="flex h-9 shrink-0 items-center justify-between px-3">
      <h2 class="text-xs font-semibold tracking-wide text-slate-500 uppercase">Explorer</h2>
      @if (store.workspace()) {
        <div class="flex gap-0.5">
          <button
            type="button"
            [class]="headerButton"
            title="New request"
            aria-label="New request"
            (click)="store.newRequest()"
          >
            <fa-icon [icon]="faPlus" />
          </button>
          <button
            type="button"
            [class]="headerButton"
            title="New folder"
            aria-label="New folder"
            (click)="state.startCreate('')"
          >
            <fa-icon [icon]="faFolderPlus" />
          </button>
          <button
            type="button"
            [class]="headerButton"
            title="Import from Postman, Insomnia, HAR or OpenAPI"
            aria-label="Import"
            [disabled]="interchange.busy()"
            (click)="interchange.importInto('')"
          >
            <fa-icon [icon]="faFileImport" />
          </button>
          <button
            type="button"
            [class]="headerButton"
            title="Export for Postman or Insomnia"
            aria-label="Export"
            aria-haspopup="dialog"
            [attr.aria-expanded]="exportOpen()"
            [disabled]="interchange.busy()"
            (click)="toggleExport()"
          >
            <fa-icon [icon]="faFileExport" />
          </button>
          <button
            type="button"
            [class]="headerButton"
            title="Reload from disk"
            aria-label="Reload from disk"
            (click)="store.refresh()"
          >
            <fa-icon [icon]="faArrowsRotate" />
          </button>
        </div>
      }
    </header>

    @if (exportOpen() && store.workspace()) {
      <form
        class="absolute top-9 right-2 left-2 z-20 flex flex-col gap-2 rounded border border-slate-300 bg-white p-3 text-sm shadow-lg"
        role="dialog"
        aria-label="Export workspace"
        (submit)="$event.preventDefault(); export()"
      >
        <fieldset class="flex flex-col gap-1">
          <legend class="mb-1 text-xs font-semibold text-slate-500 uppercase">Export as</legend>
          @for (format of interchange.formats(); track format.id) {
            <label class="flex items-center gap-2">
              <input
                type="radio"
                name="export-format"
                [value]="format.id"
                [checked]="format.id === selectedFormat()"
                (change)="selectedFormat.set(format.id)"
              />
              {{ format.name }}
            </label>
          }
        </fieldset>
        <label class="flex items-center gap-2">
          <input
            type="checkbox"
            [checked]="includeEnvironments()"
            (change)="includeEnvironments.set($any($event.target).checked)"
          />
          Include environments
        </label>
        <p class="text-xs text-slate-500">Secret values are never exported.</p>
        <div class="flex justify-end gap-2">
          <button
            type="button"
            class="rounded px-3 py-1 text-slate-600 hover:bg-slate-100"
            (click)="exportOpen.set(false)"
          >
            Cancel
          </button>
          <button
            type="submit"
            class="rounded bg-sky-600 px-3 py-1 text-white hover:bg-sky-700 disabled:opacity-50"
            [disabled]="interchange.busy()"
          >
            Export…
          </button>
        </div>
      </form>
    }

    @if (store.workspace(); as workspace) {
      <div class="truncate px-3 pb-1 text-sm font-semibold text-slate-700" [title]="workspace.root">
        {{ workspace.name }}
      </div>

      <!-- The root list is also the drop area for moving entries to the workspace root. -->
      <div class="flex min-h-0 flex-1 flex-col" cdkDropListGroup>
        <ul
          app-tree-level
          class="min-h-0 flex-1 overflow-y-auto pb-2 text-sm"
          role="tree"
          appFolderDrop=""
          [nodes]="store.tree()"
        ></ul>
        @if (!store.tree().length && !state.isCreatingIn('')) {
          <p class="px-3 py-1 text-sm text-slate-500">No requests yet.</p>
        }
      </div>
      @if (state.menu(); as menu) {
        <div
          role="menu"
          class="fixed z-50 min-w-40 rounded border border-slate-300 bg-white py-1 text-sm shadow-lg"
          [style.left.px]="menu.x"
          [style.top.px]="menu.y"
          (click)="$event.stopPropagation()"
          (contextmenu)="$event.preventDefault()"
        >
          <button
            type="button"
            role="menuitem"
            class="flex w-full items-center gap-2 px-3 py-1.5 text-left text-rose-700 hover:bg-slate-100"
            (click)="state.deleteNode(menu.node)"
          >
            <fa-icon [icon]="faTrash" />
            Delete {{ menu.node.kind === 'folder' ? 'folder' : 'request' }}
          </button>
        </div>
      }
    } @else {
      <div class="flex flex-col gap-3 px-3 py-2 text-sm text-slate-500">
        <p>No folder open. Open a folder to see its requests.</p>
        <button
          type="button"
          class="flex items-center justify-center gap-2 rounded bg-sky-600 px-3 py-1.5 text-white hover:bg-sky-700 disabled:opacity-50"
          [disabled]="store.opening()"
          (click)="store.openFolder()"
        >
          <fa-icon [icon]="faFolderOpen" /> Open folder
        </button>
      </div>
    }
  `,
})
export class ExplorerView {
  protected readonly store = inject(WorkspaceStore);
  protected readonly state = inject(ExplorerState);
  protected readonly interchange = inject(InterchangeStore);
  protected readonly exportOpen = signal(false);
  protected readonly selectedFormat = signal('postman');
  protected readonly includeEnvironments = signal(true);
  protected readonly faPlus = faPlus;
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
