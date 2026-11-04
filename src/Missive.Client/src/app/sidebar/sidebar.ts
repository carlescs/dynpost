import { Component, inject } from '@angular/core';
import { WorkspaceStore } from '../workspace/workspace-store';
import { ExplorerView } from './explorer-view';
import { GitView } from './git-view';

@Component({
  selector: 'app-sidebar',
  imports: [ExplorerView, GitView],
  host: {
    class: 'flex w-64 shrink-0 flex-col border-r border-slate-200 bg-slate-50',
    role: 'complementary',
  },
  template: `
    @switch (store.sidebarView()) {
      @case ('explorer') {
        <app-explorer-view />
      }
      @case ('git') {
        <app-git-view />
      }
    }
  `,
})
export class Sidebar {
  protected readonly store = inject(WorkspaceStore);
}
