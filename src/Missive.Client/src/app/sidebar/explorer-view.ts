import { Component, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faArrowsRotate,
  faChevronDown,
  faChevronRight,
  faFolder,
  faFolderOpen,
  faPlus,
  faTriangleExclamation,
} from '@fortawesome/free-solid-svg-icons';
import { METHOD_COLORS, WorkspaceNode } from '../workspace/models';
import { WorkspaceStore } from '../workspace/workspace-store';

@Component({
  selector: 'app-explorer-view',
  imports: [FaIconComponent, NgTemplateOutlet],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <header class="flex h-9 shrink-0 items-center justify-between px-3">
      <h2 class="text-xs font-semibold tracking-wide text-slate-500 uppercase">Explorer</h2>
      @if (store.workspace()) {
        <div class="flex gap-0.5">
          <button
            type="button"
            class="flex size-6 items-center justify-center rounded text-slate-500 hover:bg-slate-200 hover:text-slate-800"
            title="New request"
            aria-label="New request"
            (click)="store.newRequest()"
          >
            <fa-icon [icon]="faPlus" />
          </button>
          <button
            type="button"
            class="flex size-6 items-center justify-center rounded text-slate-500 hover:bg-slate-200 hover:text-slate-800"
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
          [ngTemplateOutletContext]="{ $implicit: store.tree(), depth: 0 }"
        />
        @if (!store.tree().length) {
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

    <ng-template #nodes let-items let-depth="depth">
      @for (node of items; track node.path) {
        <li role="treeitem" [attr.aria-expanded]="node.kind === 'folder' ? isExpanded(node) : null">
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
              <button
                type="button"
                class="mr-1 hidden size-5 shrink-0 items-center justify-center rounded text-xs text-slate-500 group-hover:flex hover:bg-slate-300 hover:text-slate-800 focus:flex"
                [title]="'New request in ' + node.name"
                [attr.aria-label]="'New request in ' + node.name"
                (click)="store.newRequest(node.path)"
              >
                <fa-icon [icon]="faPlus" />
              </button>
            }
          </div>
          @if (node.kind === 'folder' && isExpanded(node)) {
            <ul role="group">
              <ng-container
                [ngTemplateOutlet]="nodes"
                [ngTemplateOutletContext]="{ $implicit: node.children ?? [], depth: depth + 1 }"
              />
            </ul>
          }
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
  protected readonly faChevronDown = faChevronDown;
  protected readonly faChevronRight = faChevronRight;
  protected readonly faArrowsRotate = faArrowsRotate;
  protected readonly faWarning = faTriangleExclamation;

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
}
