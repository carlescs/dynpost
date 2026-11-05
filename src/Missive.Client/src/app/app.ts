import { Component, OnInit, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faCircleExclamation,
  faFolderOpen,
  faPaperPlane,
  faPlus,
  faXmark,
} from '@fortawesome/free-solid-svg-icons';
import { DocumentTabs } from './editor/document-tabs';
import { EnvironmentEditor } from './environments/environment-editor';
import { EnvironmentStore } from './environments/environment-store';
import { RequestEditor } from './editor/request-editor';
import { ConflictView } from './git/conflict-view';
import { GitStore } from './git/git-store';
import { ActivityBar } from './shell/activity-bar';
import { AppHeader } from './shell/app-header';
import { Sidebar } from './sidebar/sidebar';
import { UpdateStore } from './updates/update-store';
import { WorkspaceStore } from './workspace/workspace-store';

@Component({
  imports: [
    AppHeader,
    ActivityBar,
    Sidebar,
    DocumentTabs,
    RequestEditor,
    ConflictView,
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
    <div class="flex min-h-0 flex-1">
      <app-activity-bar />
      @if (store.sidebarOpen()) {
        <app-sidebar />
      }
      <main class="flex min-w-0 flex-1 flex-col">
        @if (git.conflict()) {
          <app-conflict-view />
        } @else if (environments.draft()) {
          <app-environment-editor />
        } @else {
          @if (store.documents().length) {
            <app-document-tabs />
          }
          @if (store.activeDocument(); as doc) {
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
  protected readonly environments = inject(EnvironmentStore);
  private readonly updates = inject(UpdateStore);
  protected readonly faPaperPlane = faPaperPlane;
  protected readonly faPlus = faPlus;
  protected readonly faFolderOpen = faFolderOpen;
  protected readonly faXmark = faXmark;
  protected readonly faCircleExclamation = faCircleExclamation;

  ngOnInit(): void {
    void this.store.restore();
    void this.updates.check();
  }

  protected onKeydown(event: KeyboardEvent): void {
    if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === 's') {
      event.preventDefault();
      if (this.environments.draft() && !this.git.conflict()) void this.environments.save();
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
