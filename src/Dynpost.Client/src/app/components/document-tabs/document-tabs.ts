import { Component, computed, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faGaugeHigh,
  faLayerGroup,
  faPlus,
  faSliders,
  faXmark,
} from '@fortawesome/free-solid-svg-icons';
import { EnvironmentDraft, EnvironmentStore } from '../../services/environment-store';
import { METHOD_COLORS, RequestDocument } from '../../models/workspace';
import { WorkspaceStore } from '../../services/workspace-store';
import { RunPlanDraft, RunStore, isRunPlanDirty, isRunPlanRunning } from '../../services/run-store';
import { FolderDraft, FolderSettingsStore } from '../../services/folder-settings-store';

type Tab =
  | { kind: 'request'; key: string; doc: RequestDocument }
  | { kind: 'environment'; key: string; draft: EnvironmentDraft }
  | { kind: 'run'; key: string; plan: RunPlanDraft }
  | { kind: 'folder'; key: string; folder: FolderDraft };

/**
 * The editor's tabs: open requests, environments, run plans and folder settings, in the order they
 * were opened.
 */
@Component({
  selector: 'app-document-tabs',
  imports: [FaIconComponent],
  host: { class: 'flex h-9 shrink-0 items-stretch border-b border-slate-200 bg-slate-100' },
  templateUrl: './document-tabs.html',
})
export class DocumentTabs {
  protected readonly store = inject(WorkspaceStore);
  private readonly environments = inject(EnvironmentStore);
  private readonly runs = inject(RunStore);
  private readonly folders = inject(FolderSettingsStore);
  protected readonly methodColors = METHOD_COLORS;
  protected readonly faLayerGroup = faLayerGroup;
  protected readonly faGaugeHigh = faGaugeHigh;
  protected readonly faSliders = faSliders;
  protected readonly isRunning = isRunPlanRunning;
  protected readonly faPlus = faPlus;
  protected readonly faXmark = faXmark;

  protected readonly tabs = computed(() => {
    const docs = new Map(this.store.documents().map((d) => [d.key, d]));
    const drafts = new Map(this.environments.drafts().map((d) => [d.key, d]));
    const plans = new Map(this.runs.drafts().map((d) => [d.key, d]));
    const folders = new Map(this.folders.drafts().map((d) => [d.key, d]));
    return this.store.tabs().flatMap((key): Tab[] => {
      const doc = docs.get(key);
      if (doc) return [{ kind: 'request', key, doc }];
      const draft = drafts.get(key);
      if (draft) return [{ kind: 'environment', key, draft }];
      const plan = plans.get(key);
      if (plan) return [{ kind: 'run', key, plan }];
      const folder = folders.get(key);
      return folder ? [{ kind: 'folder', key, folder }] : [];
    });
  });

  protected tabName(tab: Tab): string {
    switch (tab.kind) {
      case 'request':
        return tab.doc.name;
      case 'environment':
        return tab.draft.name || 'Untitled';
      case 'run':
        return this.runs.nameOf(tab.plan.path);
      case 'folder':
        return this.folders.nameOf(tab.folder.path);
    }
  }

  protected tabTitle(tab: Tab): string {
    switch (tab.kind) {
      case 'request':
        return tab.doc.path ?? 'Unsaved request';
      case 'environment':
        return tab.draft.id ? `environments/${tab.draft.id}` : 'Unsaved environment';
      case 'run':
        return isRunPlanRunning(tab.plan) ? `${tab.plan.path} (running)` : tab.plan.path;
      case 'folder':
        return `Settings of ${tab.folder.path}`;
    }
  }

  protected isDirty(tab: Tab): boolean {
    switch (tab.kind) {
      case 'request':
        return tab.doc.dirty;
      case 'environment':
        return tab.draft.dirty;
      case 'run':
        return isRunPlanDirty(tab.plan);
      case 'folder':
        return tab.folder.dirty;
    }
  }

  protected close(tab: Tab): void {
    if (tab.kind === 'request') void this.store.close(tab.doc.id);
    else if (tab.kind === 'environment') void this.environments.close(tab.key);
    else if (tab.kind === 'folder') void this.folders.close(tab.key);
    else void this.runs.close(tab.key);
  }
}
