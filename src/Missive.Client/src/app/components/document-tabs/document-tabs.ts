import { Component, computed, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faLayerGroup, faPlus, faXmark } from '@fortawesome/free-solid-svg-icons';
import { EnvironmentDraft, EnvironmentStore } from '../../services/environment-store';
import { METHOD_COLORS, RequestDocument } from '../../models/workspace';
import { WorkspaceStore } from '../../services/workspace-store';

type Tab =
  | { kind: 'request'; key: string; doc: RequestDocument }
  | { kind: 'environment'; key: string; draft: EnvironmentDraft };

/** The editor's tabs: open requests and environments, in the order they were opened. */
@Component({
  selector: 'app-document-tabs',
  imports: [FaIconComponent],
  host: { class: 'flex h-9 shrink-0 items-stretch border-b border-slate-200 bg-slate-100' },
  templateUrl: './document-tabs.html',
})
export class DocumentTabs {
  protected readonly store = inject(WorkspaceStore);
  private readonly environments = inject(EnvironmentStore);
  protected readonly methodColors = METHOD_COLORS;
  protected readonly faLayerGroup = faLayerGroup;
  protected readonly faPlus = faPlus;
  protected readonly faXmark = faXmark;

  protected readonly tabs = computed(() => {
    const docs = new Map(this.store.documents().map((d) => [d.key, d]));
    const drafts = new Map(this.environments.drafts().map((d) => [d.key, d]));
    return this.store.tabs().flatMap((key): Tab[] => {
      const doc = docs.get(key);
      if (doc) return [{ kind: 'request', key, doc }];
      const draft = drafts.get(key);
      return draft ? [{ kind: 'environment', key, draft }] : [];
    });
  });

  protected tabTitle(tab: Tab): string {
    return tab.kind === 'request'
      ? (tab.doc.path ?? 'Unsaved request')
      : tab.draft.id
        ? `environments/${tab.draft.id}`
        : 'Unsaved environment';
  }

  protected close(tab: Tab): void {
    if (tab.kind === 'request') void this.store.close(tab.doc.id);
    else void this.environments.close(tab.key);
  }
}
