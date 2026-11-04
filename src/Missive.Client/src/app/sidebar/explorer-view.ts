import { Component, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faArrowsRotate,
  faChevronDown,
  faChevronRight,
  faFolder,
  faFolderOpen,
  faFolderPlus,
  faPen,
  faPlus,
  faTrash,
  faTriangleExclamation,
} from '@fortawesome/free-solid-svg-icons';
import { METHOD_COLORS, WorkspaceNode } from '../workspace/models';
import { WorkspaceStore } from '../workspace/workspace-store';
import { NameInput } from './name-input';

/** A folder name being typed in the tree: a new folder inside `parent`, or a rename of `path`. */
type Editing = { kind: 'create'; parent: string } | { kind: 'rename'; path: string };

@Component({
  selector: 'app-explorer-view',
  imports: [FaIconComponent, NgTemplateOutlet, NameInput],
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
            (click)="startCreate('')"
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

      <ul class="min-h-0 flex-1 overflow-y-auto pb-2 text-sm" role="tree">
        <ng-container
          [ngTemplateOutlet]="nodes"
          [ngTemplateOutletContext]="{ $implicit: store.tree(), depth: 0, parent: '' }"
        />
        @if (!store.tree().length && !isCreatingIn('')) {
          <li class="px-3 py-1 text-slate-500">No requests yet.</li>
        }
      </ul>
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

    <ng-template #nodes let-items let-depth="depth" let-parent="parent">
      @if (isCreatingIn(parent)) {
        <li class="flex items-center gap-1.5 py-0.5 pr-2" [style.padding-left.px]="12 + depth * 12">
          <span class="w-3"></span>
          <fa-icon class="text-amber-500" [icon]="faFolder" />
          <app-name-input
            label="New folder name"
            [busy]="busy()"
            (committed)="commitCreate(parent, $event.name, $event.viaBlur)"
            (cancelled)="editing.set(null)"
          />
        </li>
      }
      @for (node of items; track node.path) {
        <li role="treeitem" [attr.aria-expanded]="node.kind === 'folder' ? isExpanded(node) : null">
          @if (isRenaming(node)) {
            <div
              class="flex items-center gap-1.5 py-0.5 pr-2"
              [style.padding-left.px]="12 + depth * 12"
            >
              <fa-icon
                class="w-3 text-[10px] text-slate-400"
                [icon]="isExpanded(node) ? faChevronDown : faChevronRight"
              />
              <fa-icon class="text-amber-500" [icon]="faFolder" />
              <app-name-input
                label="Folder name"
                [initial]="node.name"
                [busy]="busy()"
                (committed)="commitRename(node.path, $event.name, $event.viaBlur)"
                (cancelled)="editing.set(null)"
              />
            </div>
          } @else {
            <div
              class="group flex items-center hover:bg-slate-200"
              [class.bg-sky-100]="node.path === store.activeId()"
            >
              <button
                type="button"
                class="flex min-w-0 flex-1 items-center gap-1.5 py-0.5 pr-2 text-left"
                [style.padding-left.px]="12 + depth * 12"
                [title]="node.error ?? node.path"
                (click)="node.kind === 'folder' ? toggle(node) : store.openRequest(node)"
                (keydown)="onKeydown($event, node)"
              >
                @if (node.kind === 'folder') {
                  <fa-icon
                    class="w-3 text-[10px] text-slate-400"
                    [icon]="isExpanded(node) ? faChevronDown : faChevronRight"
                  />
                  <fa-icon
                    class="text-amber-500"
                    [icon]="isExpanded(node) ? faFolderOpen : faFolder"
                  />
                  <span class="truncate">{{ node.name }}</span>
                } @else if (node.error) {
                  <fa-icon class="w-12 shrink-0 text-right text-rose-500" [icon]="faWarning" />
                  <span class="truncate text-rose-700">{{ node.name }}</span>
                } @else {
                  <span
                    class="w-12 shrink-0 text-right font-mono text-[10px] font-bold"
                    [class]="methodColor(node)"
                    >{{ node.method }}</span
                  >
                  <span class="truncate">{{ node.name }}</span>
                }
              </button>
              @if (node.kind === 'folder') {
                <div class="mr-1 hidden shrink-0 gap-0.5 group-focus-within:flex group-hover:flex">
                  <button
                    type="button"
                    [class]="rowButton"
                    [title]="'New request in ' + node.name"
                    [attr.aria-label]="'New request in ' + node.name"
                    (click)="store.newRequest(node.path)"
                  >
                    <fa-icon [icon]="faPlus" />
                  </button>
                  <button
                    type="button"
                    [class]="rowButton"
                    [title]="'New folder in ' + node.name"
                    [attr.aria-label]="'New folder in ' + node.name"
                    (click)="startCreate(node.path)"
                  >
                    <fa-icon [icon]="faFolderPlus" />
                  </button>
                  <button
                    type="button"
                    [class]="rowButton"
                    [title]="'Rename ' + node.name + ' (F2)'"
                    [attr.aria-label]="'Rename ' + node.name"
                    (click)="startRename(node)"
                  >
                    <fa-icon [icon]="faPen" />
                  </button>
                  <button
                    type="button"
                    [class]="rowButton + ' hover:text-rose-700'"
                    [title]="'Delete ' + node.name + ' (Delete)'"
                    [attr.aria-label]="'Delete ' + node.name"
                    (click)="store.deleteFolder(node.path)"
                  >
                    <fa-icon [icon]="faTrash" />
                  </button>
                </div>
              }
            </div>
          }
          @if (node.kind === 'folder' && isExpanded(node)) {
            <ul role="group">
              <ng-container
                [ngTemplateOutlet]="nodes"
                [ngTemplateOutletContext]="{
                  $implicit: node.children ?? [],
                  depth: depth + 1,
                  parent: node.path,
                }"
              />
            </ul>
          }
        </li>
      }
      @if (parent && !items.length && !isCreatingIn(parent)) {
        <li
          class="py-0.5 pr-2 text-xs text-slate-400 italic"
          [style.padding-left.px]="30 + depth * 12"
        >
          Empty folder
        </li>
      }
    </ng-template>
  `,
})
export class ExplorerView {
  protected readonly store = inject(WorkspaceStore);
  protected readonly faPlus = faPlus;
  protected readonly faFolder = faFolder;
  protected readonly faFolderOpen = faFolderOpen;
  protected readonly faFolderPlus = faFolderPlus;
  protected readonly faChevronDown = faChevronDown;
  protected readonly faChevronRight = faChevronRight;
  protected readonly faArrowsRotate = faArrowsRotate;
  protected readonly faPen = faPen;
  protected readonly faTrash = faTrash;
  protected readonly faWarning = faTriangleExclamation;

  protected readonly headerButton =
    'flex size-6 items-center justify-center rounded text-slate-500 hover:bg-slate-200 hover:text-slate-800';
  protected readonly rowButton =
    'flex size-5 items-center justify-center rounded text-xs text-slate-500 hover:bg-slate-300 hover:text-slate-800';

  protected readonly editing = signal<Editing | null>(null);
  protected readonly busy = signal(false);
  private readonly collapsed = signal(new Set<string>());

  protected methodColor(node: WorkspaceNode): string {
    return METHOD_COLORS[node.method ?? 'GET'];
  }

  protected isExpanded(node: WorkspaceNode): boolean {
    return !this.collapsed().has(node.path);
  }

  protected toggle(node: WorkspaceNode): void {
    this.collapsed.update((set) => {
      const next = new Set(set);
      if (next.has(node.path)) next.delete(node.path);
      else next.add(node.path);
      return next;
    });
  }

  protected isCreatingIn(parent: string): boolean {
    const editing = this.editing();
    return editing?.kind === 'create' && editing.parent === parent;
  }

  protected isRenaming(node: WorkspaceNode): boolean {
    const editing = this.editing();
    return editing?.kind === 'rename' && editing.path === node.path;
  }

  protected startCreate(parent: string): void {
    // Make sure the new folder's row is visible.
    this.collapsed.update((set) => {
      const next = new Set(set);
      next.delete(parent);
      return next;
    });
    this.editing.set({ kind: 'create', parent });
  }

  protected startRename(node: WorkspaceNode): void {
    this.editing.set({ kind: 'rename', path: node.path });
  }

  protected async commitCreate(parent: string, name: string, viaBlur: boolean): Promise<void> {
    await this.run(
      () => this.store.createFolder(parent, name).then((path) => path !== null),
      viaBlur,
    );
  }

  protected async commitRename(path: string, name: string, viaBlur: boolean): Promise<void> {
    const wasCollapsed = this.collapsed().has(path);
    const renamed = await this.run(() => this.store.renameFolder(path, name), viaBlur);
    if (renamed && wasCollapsed) {
      const parent = path.includes('/') ? path.slice(0, path.lastIndexOf('/') + 1) : '';
      this.collapsed.update((set) => new Set([...set, parent + name]));
    }
  }

  protected onKeydown(event: KeyboardEvent, node: WorkspaceNode): void {
    if (node.kind !== 'folder') return;
    if (event.key === 'F2') {
      event.preventDefault();
      this.startRename(node);
    } else if (event.key === 'Delete') {
      event.preventDefault();
      void this.store.deleteFolder(node.path);
    }
  }

  /**
   * Runs a create or rename. On failure the name box stays open so the name can be fixed — unless
   * the user had already moved on (left the box), in which case it closes and the error banner
   * explains what went wrong.
   */
  private async run(action: () => Promise<boolean>, viaBlur: boolean): Promise<boolean> {
    // A late blur from a box that is closing (or already submitting) must not submit again.
    if (!this.editing() || this.busy()) return false;
    this.busy.set(true);
    try {
      const ok = await action();
      if (ok || viaBlur) this.editing.set(null);
      return ok;
    } finally {
      this.busy.set(false);
    }
  }
}
