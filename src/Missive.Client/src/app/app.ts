import { Component, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faPaperPlane, faPlus } from '@fortawesome/free-solid-svg-icons';
import { DocumentTabs } from './editor/document-tabs';
import { RequestEditor } from './editor/request-editor';
import { ActivityBar } from './shell/activity-bar';
import { Sidebar } from './sidebar/sidebar';
import { WorkspaceStore } from './workspace/workspace-store';

@Component({
  imports: [ActivityBar, Sidebar, DocumentTabs, RequestEditor, FaIconComponent],
  selector: 'app-root',
  host: { class: 'flex h-screen overflow-hidden bg-white font-sans text-slate-800' },
  template: `
    <app-activity-bar />
    @if (store.sidebarOpen()) {
      <app-sidebar />
    }
    <main class="flex min-w-0 flex-1 flex-col">
      @if (store.documents().length) {
        <app-document-tabs />
      }
      @if (store.activeDocument(); as doc) {
        <app-request-editor [document]="doc" />
      } @else {
        <div class="flex flex-1 flex-col items-center justify-center gap-4 text-slate-500">
          <fa-icon class="text-5xl text-slate-300" [icon]="faPaperPlane" />
          <p>Open a request from the explorer, or create a new one.</p>
          <button
            type="button"
            class="flex items-center gap-2 rounded bg-sky-600 px-4 py-2 text-sm text-white hover:bg-sky-700"
            (click)="store.newRequest()"
          >
            <fa-icon [icon]="faPlus" /> New request
          </button>
        </div>
      }
    </main>
  `,
})
export class App {
  protected readonly store = inject(WorkspaceStore);
  protected readonly faPaperPlane = faPaperPlane;
  protected readonly faPlus = faPlus;
}
