import { Component, computed, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faLayerGroup, faPlus, faXmark } from '@fortawesome/free-solid-svg-icons';
import { EnvironmentDraft, EnvironmentStore } from '../../services/environment-store';
import { METHOD_COLORS, RequestDocument } from '../../models/workspace';
import { WorkspaceStore } from '../../services/workspace-store';

type Tab =
  | { kind: 'request'; key: string; doc: RequestDocument }
  | { kind: 'environment'; key: string; draft: EnvironmentDraft };

/** The editor's tabs: open requests and environments, in the order they were opened. */
@Component({
  selector: 'app-document-tabs',
  imports: [FaIconComponent],
  host: { class: 'flex h-9 shrink-0 items-stretch border-b border-slate-200 bg-slate-100' },
  template: `
    <div class="flex min-w-0 overflow-x-auto" role="tablist">
      @for (tab of tabs(); track tab.key) {
        @let active = tab.key === store.activeTab();
        @let name = tab.kind === 'request' ? tab.doc.name : tab.draft.name || 'Untitled';
        @let dirty = tab.kind === 'request' ? tab.doc.dirty : tab.draft.dirty;
        <div
          class="group flex max-w-56 shrink-0 items-center gap-2 border-r border-slate-200 pr-1 pl-3 text-sm"
          [class.bg-white]="active"
          [class.text-slate-500]="!active"
        >
          <button
            type="button"
            role="tab"
            class="flex min-w-0 items-center gap-2"
            [attr.aria-selected]="active"
            [title]="tabTitle(tab)"
            (click)="store.activateTab(tab.key)"
          >
            @if (tab.kind === 'request') {
              <span
                class="font-mono text-[10px] font-bold"
                [class]="methodColors[tab.doc.method]"
                >{{ tab.doc.method }}</span
              >
              <span class="truncate" [class.italic]="!tab.doc.path">{{ name }}</span>
            } @else {
              <fa-icon class="text-xs text-emerald-600" [icon]="faLayerGroup" />
              <span class="truncate" [class.italic]="!tab.draft.id">{{ name }}</span>
            }
          </button>
          <button
            type="button"
            class="relative flex size-5 shrink-0 items-center justify-center rounded text-xs text-slate-400 hover:bg-slate-200 hover:text-slate-700"
            [attr.aria-label]="'Close ' + name"
            (click)="close(tab)"
          >
            @if (dirty) {
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
  private readonly environments = inject(EnvironmentStore);
  protected readonly methodColors = METHOD_COLORS;
  protected readonly faLayerGroup = faLayerGroup;
  protected readonly faPlus = faPlus;
  protected readonly faXmark = faXmark;

  protected readonly tabs = computed(() => {
    const docs = new Map(this.store.documents().map((d) => [d.key, d]));
    const drafts = new Map(this.environments.drafts().map((d) => [d.key, d]));
    return this.store.tabs().flatMap((key): Tab[] => {
      const doc = docs.get(key);
      if (doc) return [{ kind: 'request', key, doc }];
      const draft = drafts.get(key);
      return draft ? [{ kind: 'environment', key, draft }] : [];
    });
  });

  protected tabTitle(tab: Tab): string {
    return tab.kind === 'request'
      ? (tab.doc.path ?? 'Unsaved request')
      : tab.draft.id
        ? `environments/${tab.draft.id}`
        : 'Unsaved environment';
  }

  protected close(tab: Tab): void {
    if (tab.kind === 'request') this.store.close(tab.doc.id);
    else this.environments.close(tab.key);
  }
}
