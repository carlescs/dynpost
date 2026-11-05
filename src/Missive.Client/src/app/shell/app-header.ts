import { Component, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faFolderOpen, faLayerGroup, faPaperPlane } from '@fortawesome/free-solid-svg-icons';
import { EnvironmentStore } from '../environments/environment-store';
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
      @if (store.workspace()) {
        <label
          class="flex items-center gap-2 rounded px-2 py-1 text-sm text-slate-300"
          title="The environment whose variables requests are sent with"
        >
          <fa-icon [icon]="faLayerGroup" />
          <span class="sr-only">Environment</span>
          <select
            class="max-w-48 rounded border border-slate-600 bg-slate-800 px-1.5 py-0.5 text-sm text-white focus:border-sky-500 focus:outline-none"
            aria-label="Environment"
            [value]="environments.activeId() ?? ''"
            (change)="environments.select($any($event.target).value || null)"
          >
            <option value="" [selected]="!environments.activeId()">No environment</option>
            @for (env of environments.environments(); track env.id) {
              <option
                [value]="env.id"
                [disabled]="!!env.error"
                [selected]="env.id === environments.activeId()"
              >
                {{ env.name }}
              </option>
            }
          </select>
        </label>
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
  protected readonly environments = inject(EnvironmentStore);
  protected readonly faPaperPlane = faPaperPlane;
  protected readonly faLayerGroup = faLayerGroup;
  protected readonly faFolderOpen = faFolderOpen;
}
