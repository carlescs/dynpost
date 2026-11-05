import { Component, ElementRef, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faCircleArrowUp,
  faGear,
  faMoon,
  faFolderOpen,
  faLayerGroup,
  faPaperPlane,
} from '@fortawesome/free-solid-svg-icons';
import { EnvironmentStore } from '../environments/environment-store';
import { ThemeStore } from '../settings/theme-store';
import { UpdateStore } from '../updates/update-store';
import { WorkspaceStore } from '../workspace/workspace-store';

/** Main application header: app name, open workspace and global options. */
@Component({
  selector: 'app-header',
  imports: [FaIconComponent],
  host: {
    class:
      'always-dark flex h-10 shrink-0 items-center gap-2 border-b border-slate-700 bg-slate-900 px-3',
    role: 'banner',
    '(document:click)': 'onDocumentClick($event)',
    '(document:keydown.escape)': 'menuOpen.set(false)',
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
          [title]="'Missive ' + version + ' is ready. Restart to install it.'"
          [disabled]="updates.restarting()"
          (click)="updates.restart()"
        >
          <fa-icon [icon]="faCircleArrowUp" /> Restart to update
        </button>
      }
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
      <div class="relative">
        <button
          type="button"
          class="flex size-7 items-center justify-center rounded text-slate-300 hover:bg-slate-700 hover:text-white"
          title="Settings"
          aria-label="Settings"
          aria-haspopup="menu"
          [attr.aria-expanded]="menuOpen()"
          (click)="menuOpen.set(!menuOpen())"
        >
          <fa-icon [icon]="faGear" />
        </button>
        @if (menuOpen()) {
          <div
            class="adaptive absolute top-full right-0 z-50 mt-1 w-56 rounded border border-slate-300 bg-white py-1 text-sm text-slate-800 shadow-lg"
            role="menu"
            aria-label="Settings"
          >
            <button
              type="button"
              class="flex w-full items-center gap-2 px-3 py-1.5 text-left hover:bg-slate-100"
              role="menuitemcheckbox"
              [attr.aria-checked]="theme.isDark()"
              (click)="theme.setDark(!theme.isDark())"
            >
              <fa-icon class="w-4 text-slate-500" [icon]="faMoon" />
              <span class="flex-1">Dark mode</span>
              <span
                class="relative h-4 w-7 shrink-0 rounded-full transition-colors"
                [class]="theme.isDark() ? 'bg-sky-600' : 'bg-slate-300'"
                aria-hidden="true"
              >
                <span
                  class="absolute top-0.5 left-0.5 size-3 rounded-full bg-[#fff] shadow transition-transform"
                  [class.translate-x-3]="theme.isDark()"
                ></span>
              </span>
            </button>
          </div>
        }
      </div>
    </div>
  `,
})
export class AppHeader {
  protected readonly store = inject(WorkspaceStore);
  protected readonly environments = inject(EnvironmentStore);
  protected readonly updates = inject(UpdateStore);
  protected readonly theme = inject(ThemeStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  protected readonly menuOpen = signal(false);
  protected readonly faGear = faGear;
  protected readonly faMoon = faMoon;
  protected readonly faCircleArrowUp = faCircleArrowUp;
  protected readonly faPaperPlane = faPaperPlane;
  protected readonly faLayerGroup = faLayerGroup;
  protected readonly faFolderOpen = faFolderOpen;

  protected onDocumentClick(event: MouseEvent): void {
    if (!this.host.nativeElement.contains(event.target as Node)) this.menuOpen.set(false);
  }
}
