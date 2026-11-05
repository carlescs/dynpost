import { Component, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faCircleArrowUp, faFolderOpen, faPaperPlane } from '@fortawesome/free-solid-svg-icons';
import { UpdateStore } from '../updates/update-store';
import { WorkspaceStore } from '../workspace/workspace-store';

/** Main application header: app name, open workspace and global options. */
@Component({
  selector: 'app-header',
  imports: [FaIconComponent],
  host: {
    class: 'flex h-10 shrink-0 items-center gap-2 border-b border-slate-700 bg-slate-900 px-3',
    role: 'banner',
  },
  template: `
    <fa-icon class="text-sky-400" [icon]="faPaperPlane" />
    <span class="text-sm font-semibold tracking-wide text-white">Missive</span>
    @if (store.workspace(); as workspace) {
      <span class="truncate text-sm text-slate-400" [title]="workspace.root">
        — {{ workspace.name }}
      </span>
    }
    <div class="ml-auto flex items-center gap-1">
      @if (updates.readyVersion(); as version) {
        <button
          type="button"
          class="flex items-center gap-2 rounded bg-emerald-600 px-2 py-1 text-sm text-white hover:bg-emerald-500 disabled:opacity-50"
          [title]="
            updates.error()?.message ?? 'Missive ' + version + ' is ready. Restart to install it.'
          "
          [disabled]="updates.restarting()"
          (click)="updates.restart()"
        >
          <fa-icon [icon]="faCircleArrowUp" /> Restart to update
        </button>
      }
      <button
        type="button"
        class="flex items-center gap-2 rounded px-2 py-1 text-sm text-slate-300 hover:bg-slate-700 hover:text-white disabled:opacity-50"
        title="Open a workspace folder"
        [disabled]="store.opening()"
        (click)="store.openFolder()"
      >
        <fa-icon [icon]="faFolderOpen" /> Open folder
      </button>
    </div>
  `,
})
export class AppHeader {
  protected readonly store = inject(WorkspaceStore);
  protected readonly updates = inject(UpdateStore);
  protected readonly faCircleArrowUp = faCircleArrowUp;
  protected readonly faPaperPlane = faPaperPlane;
  protected readonly faFolderOpen = faFolderOpen;
}
