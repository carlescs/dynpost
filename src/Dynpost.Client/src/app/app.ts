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
import { RunEditor } from './modules/runs/components/run-editor/run-editor';
import { FolderEditor } from './modules/explorer/components/folder-editor/folder-editor';
import { FolderSettingsStore } from './services/folder-settings-store';
import { RunStore } from './services/run-store';
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
import { DateFormatStore } from './services/date-format-store';
import { UpdateStore } from './services/update-store';
import { InterchangeStore } from './services/interchange-store';
import { Shortcuts } from './services/shortcuts';
import { WorkspaceStore } from './services/workspace-store';
import { WorkspaceSync } from './services/workspace-sync';

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
    RunEditor,
    FolderEditor,
    FaIconComponent,
  ],
  selector: 'app-root',
  host: {
    class: 'flex h-screen flex-col overflow-hidden bg-white font-sans text-slate-800',
    '(document:keydown)': 'onKeydown($event)',
    // The webview's own menu (reload, inspect, ...) never fits a desktop app; menus the app
    // shows itself (trees, tabs, Monaco) call preventDefault too and still open.
    '(document:contextmenu)': '$event.preventDefault()',
  },
  templateUrl: './app.html',
})
export class App implements OnInit {
  protected readonly store = inject(WorkspaceStore);
  protected readonly git = inject(GitStore);
  protected readonly history = inject(GitHistoryStore);
  protected readonly environments = inject(EnvironmentStore);
  protected readonly runs = inject(RunStore);
  protected readonly folders = inject(FolderSettingsStore);
  protected readonly interchange = inject(InterchangeStore);
  private readonly updates = inject(UpdateStore);
  private readonly theme = inject(ThemeStore);
  private readonly dateFormat = inject(DateFormatStore);
  private readonly shortcuts = inject(Shortcuts);
  /** Shows what AI assistants change through the MCP server. */
  protected readonly sync = inject(WorkspaceSync);
  protected readonly faPaperPlane = faPaperPlane;
  protected readonly faPlus = faPlus;
  protected readonly faFolderOpen = faFolderOpen;
  protected readonly faXmark = faXmark;
  protected readonly faCircleExclamation = faCircleExclamation;
  protected readonly faCircleCheck = faCircleCheck;

  ngOnInit(): void {
    void this.theme.load();
    void this.dateFormat.load();
    void this.store.restore();
    void this.updates.check();
  }

  protected onKeydown(event: KeyboardEvent): void {
    this.shortcuts.handle(event);
  }
}
