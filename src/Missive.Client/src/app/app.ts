import { Component, OnInit, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faCircleCheck,
  faCircleExclamation,
  faFolderOpen,
  faPaperPlane,
  faPlus,
  faXmark,
} from '@fortawesome/free-solid-svg-icons';
import { DocumentTabs } from './components/document-tabs/document-tabs';
import { EnvironmentEditor } from './modules/environments/components/environment-editor/environment-editor';
import { EnvironmentStore } from './services/environment-store';
import { RequestEditor } from './modules/explorer/components/request-editor/request-editor';
import { CommitView } from './modules/git/components/commit-view/commit-view';
import { ConflictView } from './modules/git/components/conflict-view/conflict-view';
import { DiffView } from './modules/git/components/diff-view/diff-view';
import { GitHistoryStore } from './services/git-history-store';
import { GitStore } from './services/git-store';
import { ActivityBar } from './components/activity-bar/activity-bar';
import { AppHeader } from './components/app-header/app-header';
import { Sidebar } from './components/sidebar/sidebar';
import { ThemeStore } from './services/theme-store';
import { UpdateStore } from './services/update-store';
import { InterchangeStore } from './services/interchange-store';
import { WorkspaceStore } from './services/workspace-store';

@Component({
  imports: [
    AppHeader,
    ActivityBar,
    Sidebar,
    DocumentTabs,
    RequestEditor,
    ConflictView,
    CommitView,
    DiffView,
    EnvironmentEditor,
    FaIconComponent,
  ],
  selector: 'app-root',
  host: {
    class: 'flex h-screen flex-col overflow-hidden bg-white font-sans text-slate-800',
    '(document:keydown)': 'onKeydown($event)',
  },
  template: `
    <app-header />
    @if (store.error(); as error) {
      <div
        class="flex shrink-0 items-start gap-2 border-b border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-800"
        role="alert"
      >
        <fa-icon class="mt-0.5" [icon]="faCircleExclamation" />
        <span class="min-w-0 flex-1 break-words">{{ error }}</span>
        <button
          type="button"
          class="flex size-5 shrink-0 items-center justify-center rounded hover:bg-rose-100"
          aria-label="Dismiss"
          (click)="store.dismissError()"
        >
          <fa-icon [icon]="faXmark" />
        </button>
      </div>
    }
    @if (interchange.notice(); as notice) {
      <div
        class="flex shrink-0 items-start gap-2 border-b border-emerald-200 bg-emerald-50 px-3 py-2 text-sm text-emerald-900"
        role="status"
      >
        <fa-icon class="mt-0.5" [icon]="faCircleCheck" />
        <div class="min-w-0 flex-1 break-words">
          <p>{{ notice.message }}</p>
          @if (notice.warnings.length) {
            <details class="mt-1">
              <summary class="cursor-pointer text-emerald-800">
                {{ notice.warnings.length }} note(s)
              </summary>
              <ul class="mt-1 max-h-40 list-disc overflow-y-auto pl-5 text-emerald-800">
                @for (warning of notice.warnings; track $index) {
                  <li>{{ warning }}</li>
                }
              </ul>
            </details>
          }
        </div>
        <button
          type="button"
          class="flex size-5 shrink-0 items-center justify-center rounded hover:bg-emerald-100"
          aria-label="Dismiss"
          (click)="interchange.dismiss()"
        >
          <fa-icon [icon]="faXmark" />
        </button>
      </div>
    }
    <div class="flex min-h-0 flex-1">
      <app-activity-bar />
      @if (store.sidebarOpen()) {
        <app-sidebar />
      }
      <main class="flex min-w-0 flex-1 flex-col">
        @if (git.conflict()) {
          <app-conflict-view />
        } @else if (history.reviewing()) {
          <!-- Lazy chunk, like the diff view: only needed once a commit is opened. -->
          @defer (on immediate) {
            <app-commit-view />
          }
        } @else if (git.diff()) {
          <!-- Lazy chunk: the diff views are only needed once a change is opened. -->
          @defer (on immediate) {
            <app-diff-view />
          }
        } @else {
          @if (store.tabs().length) {
            <app-document-tabs />
          }
          @if (environments.draft(); as draft) {
            <app-environment-editor [draft]="draft" />
          } @else if (store.activeDocument(); as doc) {
            <app-request-editor [document]="doc" />
          } @else {
            <div class="flex flex-1 flex-col items-center justify-center gap-4 text-slate-500">
              <fa-icon class="text-5xl text-slate-300" [icon]="faPaperPlane" />
              @if (store.workspace()) {
                <p>Open a request from the explorer, or create a new one.</p>
                <button
                  type="button"
                  class="flex items-center gap-2 rounded bg-sky-600 px-4 py-2 text-sm text-white hover:bg-sky-700"
                  (click)="store.newRequest()"
                >
                  <fa-icon [icon]="faPlus" /> New request
                </button>
              } @else {
                <p>Open a folder to work with the requests it contains.</p>
                <button
                  type="button"
                  class="flex items-center gap-2 rounded bg-sky-600 px-4 py-2 text-sm text-white hover:bg-sky-700 disabled:opacity-50"
                  [disabled]="store.opening()"
                  (click)="store.openFolder()"
                >
                  <fa-icon [icon]="faFolderOpen" /> Open folder
                </button>
              }
              @if (!store.isDesktop) {
                <p class="text-xs text-slate-400">
                  Browser preview: sample data in memory, nothing is written to disk.
                </p>
              }
            </div>
          }
        }
      </main>
    </div>
  `,
})
export class App implements OnInit {
  protected readonly store = inject(WorkspaceStore);
  protected readonly git = inject(GitStore);
  protected readonly history = inject(GitHistoryStore);
  protected readonly environments = inject(EnvironmentStore);
  protected readonly interchange = inject(InterchangeStore);
  private readonly updates = inject(UpdateStore);
  private readonly theme = inject(ThemeStore);
  protected readonly faPaperPlane = faPaperPlane;
  protected readonly faPlus = faPlus;
  protected readonly faFolderOpen = faFolderOpen;
  protected readonly faXmark = faXmark;
  protected readonly faCircleExclamation = faCircleExclamation;
  protected readonly faCircleCheck = faCircleCheck;

  ngOnInit(): void {
    void this.theme.load();
    void this.store.restore();
    void this.updates.check();
  }

  protected onKeydown(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === 's') {
      event.preventDefault();
      const draft = this.environments.draft();
      if (draft && !this.git.conflict() && !this.git.diff()) void this.environments.save(draft.key);
      else void this.store.saveActive();
    } else if (
      (event.ctrlKey || event.metaKey) &&
      !event.altKey &&
      event.key === 'Enter' &&
      !this.environments.draft()
    ) {
      event.preventDefault();
      void this.store.sendActive();
    }
  }
}
