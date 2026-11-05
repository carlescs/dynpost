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
import { InterchangeStore } from '../workspace/interchange-store';
import { METHOD_COLORS, WorkspaceNode } from '../workspace/models';
import { ExplorerState } from './explorer-state';
import { FolderDrop } from './folder-drop';
import { NameInput } from './name-input';

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
  template: `
    @if (state.isCreatingIn(parent())) {
      <li class="flex items-center gap-1.5 py-0.5 pr-2" [style.padding-left.px]="indent()">
        <span class="w-3"></span>
        <fa-icon class="text-amber-500" [icon]="faFolder" />
        <app-name-input
          label="New folder name"
          [busy]="state.busy()"
          (committed)="state.commitCreate(parent(), $event.name, $event.viaBlur)"
          (cancelled)="state.editing.set(null)"
        />
      </li>
    }
    @for (node of nodes(); track node.path) {
      <li
        role="treeitem"
        [attr.aria-expanded]="node.kind === 'folder' ? state.isExpanded(node) : null"
        cdkDrag
        [cdkDragData]="node"
        [cdkDragDisabled]="state.editing() !== null"
        (cdkDragStarted)="dragStarted(node, $event)"
        (cdkDragEnded)="state.endDrag()"
      >
        <!-- While dragging, the entry's row collapses; the preview follows the pointer. -->
        <div *cdkDragPlaceholder class="h-0"></div>
        <!-- The CDK resets the preview root's box styles, so the chip is an inner element. -->
        <div *cdkDragPreview>
          <div
            class="flex items-center gap-1.5 rounded border border-slate-300 bg-white px-2 py-1 text-sm text-slate-800 shadow-md"
          >
            @if (node.kind === 'folder') {
              <fa-icon class="text-amber-500" [icon]="faFolder" />
            } @else {
              <span class="font-mono text-[10px] font-bold" [class]="methodColor(node)">{{
                node.method
              }}</span>
            }
            {{ node.name }}
          </div>
        </div>

        @if (node.kind === 'folder') {
          <div [appFolderDrop]="node.path">
            @if (state.isRenaming(node)) {
              <div class="flex items-center gap-1.5 py-0.5 pr-2" [style.padding-left.px]="indent()">
                <fa-icon class="w-3 text-[10px] text-slate-400" [icon]="chevron(node)" />
                <fa-icon class="text-amber-500" [icon]="faFolder" />
                <app-name-input
                  label="Folder name"
                  [initial]="node.name"
                  [busy]="state.busy()"
                  (committed)="state.commitRename(node.path, $event.name, $event.viaBlur)"
                  (cancelled)="state.editing.set(null)"
                />
              </div>
            } @else {
              <div class="group flex items-center hover:bg-slate-200">
                <button
                  type="button"
                  class="flex min-w-0 flex-1 items-center gap-1.5 py-0.5 pr-2 text-left"
                  [style.padding-left.px]="indent()"
                  [title]="node.path"
                  (click)="state.toggle(node)"
                  (keydown)="onFolderKeydown($event, node)"
                >
                  <fa-icon class="w-3 text-[10px] text-slate-400" [icon]="chevron(node)" />
                  <fa-icon
                    class="text-amber-500"
                    [icon]="state.isExpanded(node) ? faFolderOpen : faFolder"
                  />
                  <span class="truncate">{{ node.name }}</span>
                </button>
                <div class="mr-1 hidden shrink-0 gap-0.5 group-focus-within:flex group-hover:flex">
                  <button
                    type="button"
                    [class]="rowButton"
                    [title]="'New request in ' + node.name"
                    [attr.aria-label]="'New request in ' + node.name"
                    (click)="state.store.newRequest(node.path)"
                  >
                    <fa-icon [icon]="faPlus" />
                  </button>
                  <button
                    type="button"
                    [class]="rowButton"
                    [title]="'New folder in ' + node.name"
                    [attr.aria-label]="'New folder in ' + node.name"
                    (click)="state.startCreate(node.path)"
                  >
                    <fa-icon [icon]="faFolderPlus" />
                  </button>
                  <button
                    type="button"
                    [class]="rowButton"
                    [title]="'Import into ' + node.name"
                    [attr.aria-label]="'Import into ' + node.name"
                    [disabled]="interchange.busy()"
                    (click)="interchange.importInto(node.path)"
                  >
                    <fa-icon [icon]="faFileImport" />
                  </button>
                  <button
                    type="button"
                    [class]="rowButton"
                    [title]="'Rename ' + node.name + ' (F2)'"
                    [attr.aria-label]="'Rename ' + node.name"
                    (click)="state.startRename(node)"
                  >
                    <fa-icon [icon]="faPen" />
                  </button>
                  <button
                    type="button"
                    [class]="rowButton + ' hover:text-rose-700'"
                    [title]="'Delete ' + node.name + ' (Delete)'"
                    [attr.aria-label]="'Delete ' + node.name"
                    (click)="state.store.deleteFolder(node.path)"
                  >
                    <fa-icon [icon]="faTrash" />
                  </button>
                </div>
              </div>
            }
            @if (state.isExpanded(node)) {
              <ul
                app-tree-level
                role="group"
                [nodes]="node.children ?? []"
                [depth]="depth() + 1"
                [parent]="node.path"
              ></ul>
            }
          </div>
        } @else {
          <button
            type="button"
            class="flex w-full min-w-0 items-center gap-1.5 py-0.5 pr-2 text-left hover:bg-slate-200"
            [class.bg-sky-100]="node.path === state.store.activeId()"
            [style.padding-left.px]="indent()"
            [title]="node.error ?? node.path"
            (click)="state.store.openRequest(node)"
          >
            <span class="w-3 shrink-0"></span>
            @if (node.error) {
              <fa-icon class="w-8 shrink-0 text-rose-500" [icon]="faWarning" />
              <span class="truncate text-rose-700">{{ node.name }}</span>
            } @else {
              <span
                class="w-8 shrink-0 font-mono text-[10px] font-bold"
                [class]="methodColor(node)"
                >{{ node.method }}</span
              >
              <span class="truncate">{{ node.name }}</span>
            }
          </button>
        }
      </li>
    }
    @if (parent() && !nodes().length && !state.isCreatingIn(parent())) {
      <li class="py-0.5 pr-2 text-xs text-slate-400 italic" [style.padding-left.px]="indent() + 18">
        Empty folder
      </li>
    }
  `,
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
