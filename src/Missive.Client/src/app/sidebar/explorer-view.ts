import { Component, inject, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faChevronDown,
  faChevronRight,
  faFolder,
  faFolderOpen,
  faPlus,
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
      <button
        type="button"
        class="flex size-6 items-center justify-center rounded text-slate-500 hover:bg-slate-200 hover:text-slate-800"
        title="New request"
        aria-label="New request"
        (click)="store.newRequest()"
      >
        <fa-icon [icon]="faPlus" />
      </button>
    </header>

    <div class="px-3 pb-1 text-sm font-semibold text-slate-700">{{ store.workspaceName() }}</div>

    <ul class="min-h-0 flex-1 overflow-y-auto pb-2 text-sm" role="tree">
      <ng-container
        [ngTemplateOutlet]="nodes"
        [ngTemplateOutletContext]="{ $implicit: store.tree(), depth: 0 }"
      />
    </ul>

    <ng-template #nodes let-items let-depth="depth">
      @for (node of items; track node.path) {
        <li role="treeitem" [attr.aria-expanded]="node.kind === 'folder' ? isExpanded(node) : null">
          <button
            type="button"
            class="flex w-full items-center gap-1.5 truncate py-0.5 pr-2 text-left hover:bg-slate-200"
            [class.bg-sky-100]="node.path === store.activeId()"
            [style.padding-left.px]="12 + depth * 12"
            (click)="node.kind === 'folder' ? toggle(node) : store.openRequest(node)"
          >
            @if (node.kind === 'folder') {
              <fa-icon
                class="w-3 text-[10px] text-slate-400"
                [icon]="isExpanded(node) ? faChevronDown : faChevronRight"
              />
              <fa-icon class="text-amber-500" [icon]="isExpanded(node) ? faFolderOpen : faFolder" />
              <span class="truncate">{{ node.name }}</span>
            } @else {
              <span
                class="w-12 shrink-0 text-right font-mono text-[10px] font-bold"
                [class]="methodColor(node)"
                >{{ node.method }}</span
              >
              <span class="truncate">{{ node.name }}</span>
            }
          </button>
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
