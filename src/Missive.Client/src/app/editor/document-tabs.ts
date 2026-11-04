import { Component, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faPlus, faXmark } from '@fortawesome/free-solid-svg-icons';
import { METHOD_COLORS } from '../workspace/models';
import { WorkspaceStore } from '../workspace/workspace-store';

@Component({
  selector: 'app-document-tabs',
  imports: [FaIconComponent],
  host: { class: 'flex h-9 shrink-0 items-stretch border-b border-slate-200 bg-slate-100' },
  template: `
    <div class="flex min-w-0 overflow-x-auto" role="tablist">
      @for (doc of store.documents(); track doc.id) {
        <div
          class="group flex max-w-56 shrink-0 items-center gap-2 border-r border-slate-200 pr-1 pl-3 text-sm"
          [class.bg-white]="doc.id === store.activeId()"
          [class.text-slate-500]="doc.id !== store.activeId()"
        >
          <button
            type="button"
            role="tab"
            class="flex min-w-0 items-center gap-2"
            [attr.aria-selected]="doc.id === store.activeId()"
            [title]="doc.path ?? 'Unsaved request'"
            (click)="store.activate(doc.id)"
          >
            <span class="font-mono text-[10px] font-bold" [class]="methodColors[doc.method]">{{
              doc.method
            }}</span>
            <span class="truncate" [class.italic]="!doc.path">{{ doc.name }}</span>
          </button>
          <button
            type="button"
            class="relative flex size-5 shrink-0 items-center justify-center rounded text-xs text-slate-400 hover:bg-slate-200 hover:text-slate-700"
            [attr.aria-label]="'Close ' + doc.name"
            (click)="store.close(doc.id)"
          >
            @if (doc.dirty) {
              <span class="size-2 rounded-full bg-slate-400 group-hover:hidden"></span>
              <fa-icon class="hidden group-hover:inline" [icon]="faXmark" />
            } @else {
              <fa-icon [icon]="faXmark" />
            }
          </button>
        </div>
      }
    </div>
    <button
      type="button"
      class="flex w-9 shrink-0 items-center justify-center text-slate-500 hover:bg-slate-200 hover:text-slate-800"
      title="New request"
      aria-label="New request"
      (click)="store.newRequest()"
    >
      <fa-icon [icon]="faPlus" />
    </button>
  `,
})
export class DocumentTabs {
  protected readonly store = inject(WorkspaceStore);
  protected readonly methodColors = METHOD_COLORS;
  protected readonly faPlus = faPlus;
  protected readonly faXmark = faXmark;
}
