import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faCodeBranch, faFolderTree, faLayerGroup } from '@fortawesome/free-solid-svg-icons';
import { GitStore } from '../../services/git-store';
import { filter, map } from 'rxjs';
import { WorkspaceStore } from '../../services/workspace-store';

/** The routed modules, one per activity: `#/explorer`, `#/environments` and `#/git`. */
type Activity = 'explorer' | 'environments' | 'git';

@Component({
  selector: 'app-activity-bar',
  imports: [FaIconComponent],
  host: { class: 'always-dark flex w-12 shrink-0 flex-col items-center gap-1 bg-slate-800 py-2' },
  template: `
    @for (item of items; track item.view) {
      <button
        type="button"
        class="relative flex size-10 items-center justify-center rounded-md text-lg text-slate-400 hover:text-white"
        [class.text-white]="isActive(item.view)"
        [attr.aria-label]="item.label"
        [attr.aria-pressed]="isActive(item.view)"
        [title]="item.label"
        (click)="show(item.view)"
      >
        @if (isActive(item.view)) {
          <span class="absolute inset-y-1 -left-1 w-0.5 rounded bg-sky-400"></span>
        }
        <fa-icon [icon]="item.icon" />
        @if (item.view === 'git' && git.changeCount()) {
          <span
            class="absolute right-0.5 bottom-0.5 min-w-4 rounded-full bg-sky-500 px-1 text-[10px] leading-4 text-white"
          >
            {{ git.changeCount() }}
          </span>
        }
      </button>
    }
  `,
})
export class ActivityBar {
  protected readonly store = inject(WorkspaceStore);
  protected readonly git = inject(GitStore);
  private readonly router = inject(Router);

  /** The activity whose module is routed, from the first segment of the URL. */
  private readonly current = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.activeRoute()),
    ),
    { initialValue: this.activeRoute() },
  );

  protected readonly items: { view: Activity; label: string; icon: typeof faFolderTree }[] = [
    { view: 'explorer', label: 'Explorer', icon: faFolderTree },
    { view: 'environments', label: 'Environments', icon: faLayerGroup },
    { view: 'git', label: 'Source control', icon: faCodeBranch },
  ];

  protected isActive(view: Activity): boolean {
    return this.store.sidebarOpen() && this.current() === view;
  }

  /** Routes to the activity's module, or toggles the sidebar when it is already routed. */
  protected show(view: Activity): void {
    if (this.current() === view) {
      this.store.toggleSidebar();
    } else {
      this.store.toggleSidebar(true);
      void this.router.navigate(['/', view]);
    }
  }

  private activeRoute(): string {
    return this.router.parseUrl(this.router.url).root.children['primary']?.segments[0]?.path ?? '';
  }
}
