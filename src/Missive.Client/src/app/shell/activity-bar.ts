import { Component, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faCodeBranch, faFolderTree } from '@fortawesome/free-solid-svg-icons';
import { SidebarView, WorkspaceStore } from '../workspace/workspace-store';

@Component({
  selector: 'app-activity-bar',
  imports: [FaIconComponent],
  host: { class: 'flex w-12 shrink-0 flex-col items-center gap-1 bg-slate-800 py-2' },
  template: `
    @for (item of items; track item.view) {
      <button
        type="button"
        class="relative flex size-10 items-center justify-center rounded-md text-lg text-slate-400 hover:text-white"
        [class.text-white]="isActive(item.view)"
        [attr.aria-label]="item.label"
        [attr.aria-pressed]="isActive(item.view)"
        [title]="item.label"
        (click)="store.showView(item.view)"
      >
        @if (isActive(item.view)) {
          <span class="absolute inset-y-1 -left-1 w-0.5 rounded bg-sky-400"></span>
        }
        <fa-icon [icon]="item.icon" />
        @if (item.view === 'git' && store.git().changes.length) {
          <span
            class="absolute right-0.5 bottom-0.5 min-w-4 rounded-full bg-sky-500 px-1 text-[10px] leading-4 text-white"
          >
            {{ store.git().changes.length }}
          </span>
        }
      </button>
    }
  `,
})
export class ActivityBar {
  protected readonly store = inject(WorkspaceStore);

  protected readonly items: { view: SidebarView; label: string; icon: typeof faFolderTree }[] = [
    { view: 'explorer', label: 'Explorer', icon: faFolderTree },
    { view: 'git', label: 'Source control', icon: faCodeBranch },
  ];

  protected isActive(view: SidebarView): boolean {
    return this.store.sidebarOpen() && this.store.sidebarView() === view;
  }
}
