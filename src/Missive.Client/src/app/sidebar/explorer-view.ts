import { Component, HostListener, inject } from '@angular/core';
import { CdkDropListGroup } from '@angular/cdk/drag-drop';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faArrowsRotate,
  faFolderOpen,
  faFolderPlus,
  faPlus,
  faTrash,
} from '@fortawesome/free-solid-svg-icons';
import { WorkspaceStore } from '../workspace/workspace-store';
import { ExplorerState } from './explorer-state';
import { FolderDrop } from './folder-drop';
import { TreeLevel } from './tree-level';

@Component({
  selector: 'app-explorer-view',
  imports: [CdkDropListGroup, FaIconComponent, FolderDrop, TreeLevel],
  providers: [ExplorerState],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
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
            title="Reload from disk"
            aria-label="Reload from disk"
            (click)="store.refresh()"
          >
            <fa-icon [icon]="faArrowsRotate" />
          </button>
        </div>
      }
    </header>

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
  protected readonly faPlus = faPlus;
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
    'flex size-6 items-center justify-center rounded text-slate-500 hover:bg-slate-200 hover:text-slate-800';
}
