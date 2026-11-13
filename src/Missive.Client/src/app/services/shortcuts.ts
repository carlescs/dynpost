import { Dialog } from '@angular/cdk/dialog';
import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import { ShortcutsDialog } from '../components/shortcuts-dialog/shortcuts-dialog';
import { Bridge } from './bridge';
import { DISCARD, Dialogs } from './dialogs';
import { EnvironmentStore } from './environment-store';
import { FolderSettingsStore } from './folder-settings-store';
import { GitHistoryStore } from './git-history-store';
import { GitStore } from './git-store';
import { RunStore, isRunPlanDirty, isRunPlanRunning } from './run-store';
import { WorkspaceStore } from './workspace-store';
import { SHORTCUTS, ShortcutAction, matches } from '../models/shortcuts';

/** The app-wide keyboard shortcuts, handled from the shell's `document:keydown` listener. */
@Injectable({ providedIn: 'root' })
export class Shortcuts {
  private readonly bridge = inject(Bridge);
  private readonly dialog = inject(Dialog);
  private readonly dialogs = inject(Dialogs);
  private readonly router = inject(Router);
  private readonly workspace = inject(WorkspaceStore);
  private readonly environments = inject(EnvironmentStore);
  private readonly runs = inject(RunStore);
  private readonly folders = inject(FolderSettingsStore);
  private readonly git = inject(GitStore);
  private readonly history = inject(GitHistoryStore);
  private quitting = false;

  /** The shortcuts this host offers. */
  readonly available = SHORTCUTS.filter((s) => !s.desktopOnly || this.bridge.isDesktop);

  /** Carries out the shortcut `event` presses, if any, and keeps the webview from acting on it. */
  handle(event: KeyboardEvent): void {
    // A dialog is modal: its own keys (Escape, Enter) are the only ones that apply.
    if (this.dialog.openDialogs.length) return;
    const shortcut = this.available.find((s) => s.keys.some((combo) => matches(combo, event)));
    if (!shortcut) return;
    event.preventDefault();
    // Holding a key down repeats only tab cycling, not sends, saves or dialogs.
    if (event.repeat && shortcut.action !== 'nextTab' && shortcut.action !== 'previousTab') return;
    void this.run(shortcut.action);
  }

  async run(action: ShortcutAction): Promise<void> {
    switch (action) {
      case 'save':
        return this.save();
      case 'send':
        if (!this.environments.draft() && !this.runs.draft()) await this.workspace.sendActive();
        return;
      case 'newRequest':
        if (this.workspace.workspace()) this.workspace.newRequest();
        return;
      case 'openFolder':
        if (!this.workspace.opening()) await this.workspace.openFolder();
        return;
      case 'close':
        return this.close();
      case 'nextTab':
        return this.cycleTab(1);
      case 'previousTab':
        return this.cycleTab(-1);
      case 'toggleSidebar':
        return this.workspace.toggleSidebar();
      case 'explorer':
      case 'environments':
      case 'runs':
      case 'git':
        this.workspace.toggleSidebar(true);
        await this.router.navigate(['/', action]);
        return;
      case 'help':
        this.dialogs.open(ShortcutsDialog, { ariaLabelledBy: 'shortcuts-title' });
        return;
      case 'quit':
        return this.quit();
    }
  }

  /** Whether a git screen (conflict, commit review or diff) covers the editor tabs. */
  private get gitScreen(): boolean {
    return !!this.git.conflict() || this.history.reviewing() || !!this.git.diff();
  }

  private async save(): Promise<void> {
    const draft = this.environments.draft();
    const plan = this.runs.draft();
    const folder = this.folders.draft();
    const editing = !this.git.conflict() && !this.git.diff();
    if (draft && editing) await this.environments.save(draft.key);
    else if (plan && editing) await this.runs.save(plan.key);
    else if (folder && editing) await this.folders.save(folder.key);
    else await this.workspace.saveActive();
  }

  /** Closes the screen in front (a conflict, commit or diff), or else the active tab. */
  private async close(): Promise<void> {
    if (this.git.conflict()) return this.git.closeConflict();
    if (this.history.reviewing()) return this.history.close();
    if (this.git.diff()) return this.git.closeDiff();
    const key = this.workspace.activeTab();
    if (!key) return;
    const doc = this.workspace.documents().find((d) => d.key === key);
    if (doc) await this.workspace.close(doc.id);
    else if (this.environments.drafts().some((d) => d.key === key))
      await this.environments.close(key);
    else if (this.folders.drafts().some((d) => d.key === key)) await this.folders.close(key);
    else await this.runs.close(key);
  }

  private cycleTab(step: number): void {
    if (this.gitScreen) return;
    const tabs = this.workspace.tabs();
    if (tabs.length < 2) return;
    const index = tabs.indexOf(this.workspace.activeTab() ?? '');
    this.workspace.activateTab(tabs[(index + step + tabs.length) % tabs.length]);
  }

  /** Asks the host to close the window, after confirming that unsaved changes may be lost. */
  private async quit(): Promise<void> {
    if (this.quitting) return;
    this.quitting = true;
    try {
      const unsaved =
        this.workspace.documents().filter((d) => d.dirty).length +
        this.environments.drafts().filter((d) => d.dirty).length +
        this.folders.drafts().filter((d) => d.dirty).length +
        this.runs.drafts().filter(isRunPlanDirty).length;
      const running = this.runs.drafts().filter(isRunPlanRunning).length;
      const notes = [
        unsaved && `${unsaved} open tab(s) have unsaved changes.`,
        running && `${running} load run(s) will be stopped.`,
      ].filter(Boolean);
      if (
        notes.length &&
        !(await this.dialogs.confirm(`Quit Missive? ${notes.join(' ')}`, {
          ...DISCARD,
          confirmLabel: 'Quit',
        }))
      ) {
        return;
      }
      await this.bridge.send('app.quit');
    } catch {
      // The window is closing, or the host refused; nothing to show either way.
    } finally {
      this.quitting = false;
    }
  }
}
