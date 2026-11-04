import { Component, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faFolderOpen, faPaperPlane } from '@fortawesome/free-solid-svg-icons';
import { Bridge } from '../bridge/bridge';
import { WorkspaceStore } from '../workspace/workspace-store';

/** Main application header: the app name, and global actions on the right. */
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
    <div class="ml-auto flex items-center gap-1">
      @if (bridge.available) {
        <button
          type="button"
          class="flex items-center gap-2 rounded px-2 py-1 text-sm text-slate-300 hover:bg-slate-700 hover:text-white"
          [title]="store.workspacePath() ?? 'Open a workspace folder'"
          (click)="store.openWorkspace()"
        >
          <fa-icon [icon]="faFolderOpen" />
          {{ store.workspacePath() ? store.workspaceName() : 'Open folder' }}
        </button>
      }
    </div>
  `,
})
export class AppHeader {
  protected readonly store = inject(WorkspaceStore);
  protected readonly bridge = inject(Bridge);
  protected readonly faPaperPlane = faPaperPlane;
  protected readonly faFolderOpen = faFolderOpen;
}
