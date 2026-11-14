import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { Bridge, BridgeError } from './bridge';
import { FolderSettingsDto, InheritedSettingsDto } from '../models/messages';
import { KeyValue, REQUESTS_FOLDER, RequestAuth } from '../models/workspace';
import { EMPTY_AUTH, fromAuthDto, toAuthDto } from '../models/request-mapping';
import { WorkspaceStore } from './workspace-store';
import { DISCARD, Dialogs } from './dialogs';

/** The file holding the headers, auth and variables a folder sets. */
export const FOLDER_SETTINGS_FILE = '_folder.yaml';

/** The settings of a folder open in an editor tab. */
export interface FolderDraft {
  /** The folder: `requests` or a folder inside it. */
  path: string;
  /** The draft's tab key (see `WorkspaceStore.tabs`); never changes. */
  key: string;
  /** Headers sent with every request below the folder, unless a closer level sets them. */
  headers: KeyValue[];
  /** The auth of the requests below that inherit it; `inherit` passes on the parent's. */
  auth: RequestAuth;
  /** Variables for the requests below; the selected environment's win over them. */
  variables: KeyValue[];
  dirty: boolean;
  saving: boolean;
  /** Incremented on every edit, so a save can tell whether the draft changed meanwhile. */
  revision: number;
}

/**
 * The headers, auth and variables collection folders set for the requests and folders in them (their
 * `_folder.yaml`), open in editor tabs next to the requests. Files are read and written by the
 * .NET host, which applies them when sending.
 */
@Injectable({ providedIn: 'root' })
export class FolderSettingsStore {
  private readonly bridge = inject(Bridge);
  private readonly dialogs = inject(Dialogs);
  private readonly workspace = inject(WorkspaceStore);

  /** The folders open in editor tabs. */
  readonly drafts = signal<FolderDraft[]>([]);
  /** The folder in the active tab, if a folder tab is active. */
  readonly draft = computed(
    () => this.drafts().find((d) => d.key === this.workspace.activeTab()) ?? null,
  );
  /**
   * Incremented whenever folder settings may have changed on disk (saved here, or changed by git
   * or an AI assistant), so editors showing inherited values re-read them.
   */
  readonly changed = signal(0);

  private readonly root = computed(() => this.workspace.workspace()?.root ?? null);

  constructor() {
    effect(() => {
      this.root();
      untracked(() => {
        this.workspace.removeTabs(this.drafts().map((d) => d.key));
        this.drafts.set([]);
      });
    });
    effect(() => {
      const moved = this.workspace.moved();
      if (!moved) return;
      const { from, to } = moved;
      untracked(() =>
        this.drafts.update((drafts) =>
          drafts.map((d) =>
            isWithin(d.path, from) ? { ...d, path: to + d.path.slice(from.length) } : d,
          ),
        ),
      );
    });
    effect(() => {
      const deleted = this.workspace.folderDeleted();
      if (!deleted) return;
      untracked(() => this.remove(this.drafts().filter((d) => isWithin(d.path, deleted.path))));
    });
    effect(() => {
      const reloaded = this.workspace.reloaded();
      if (!reloaded) return;
      untracked(() => void this.reload(reloaded.paths));
    });
  }

  /** The display name of a folder: its own name, or "All requests" for `requests`. */
  nameOf(path: string): string {
    return path === REQUESTS_FOLDER ? 'All requests' : path.slice(path.lastIndexOf('/') + 1);
  }

  /** Opens a folder's settings in a tab, or shows its tab if it is already open. */
  async open(path: string): Promise<void> {
    if (this.showOpen(path)) return;
    const root = this.root();
    try {
      const settings = await this.bridge.send('folder.settings.load', { path });
      if (this.showOpen(path) || this.root() !== root) return;
      const key = this.workspace.newTabKey();
      this.drafts.update((drafts) => [
        ...drafts,
        { ...fromSettingsDto(settings), path, key, dirty: false, saving: false, revision: 0 },
      ]);
      this.workspace.openTab(key);
    } catch (error) {
      this.report(error);
    }
  }

  update(key: string, patch: Partial<Pick<FolderDraft, 'headers' | 'auth' | 'variables'>>): void {
    this.patchDraft(key, (d) => ({ ...d, ...patch, dirty: true, revision: d.revision + 1 }));
  }

  async save(key: string): Promise<void> {
    const draft = this.drafts().find((d) => d.key === key);
    if (!draft || draft.saving) return;
    this.patchDraft(key, (d) => ({ ...d, saving: true }));
    try {
      const saved = await this.bridge.send('folder.settings.save', {
        path: draft.path,
        settings: {
          headers: draft.headers.map((h) => ({ ...h })),
          auth: toAuthDto(draft.auth),
          variables: draft.variables.map((v) => ({ ...v })),
        },
      });
      this.patchDraft(key, (d) =>
        // Keep edits typed while the save was in flight; they still need saving.
        d.revision === draft.revision
          ? { ...d, ...fromSettingsDto(saved), saving: false, dirty: false }
          : { ...d, saving: false },
      );
      this.workspace.dismissError();
      this.changed.update((n) => n + 1);
    } catch (error) {
      this.patchDraft(key, (d) => ({ ...d, saving: false }));
      this.report(error);
    }
  }

  /** Closes a draft's tab, after asking if it has unsaved changes. */
  async close(key: string): Promise<void> {
    const draft = this.drafts().find((d) => d.key === key);
    if (!draft) return;
    if (
      draft.dirty &&
      !(await this.dialogs.confirm(
        `Discard unsaved changes to the settings of "${this.nameOf(draft.path)}"?`,
        DISCARD,
      ))
    ) {
      return;
    }
    this.remove([draft]);
  }

  /**
   * What the requests and folders directly inside `folder` inherit; `null` when it can't be read
   * (sending reports why).
   */
  async inherited(folder: string): Promise<InheritedSettingsDto | null> {
    if (!this.root()) return null;
    try {
      return await this.bridge.send('folder.settings.inherited', { path: folder });
    } catch {
      return null;
    }
  }

  /** Re-reads the clean drafts whose `_folder.yaml` changed on disk (all of them without `paths`). */
  private async reload(paths?: string[]): Promise<void> {
    const changed = (path: string) => !paths || paths.includes(`${path}/${FOLDER_SETTINGS_FILE}`);
    if (paths && !paths.some((p) => p.endsWith(`/${FOLDER_SETTINGS_FILE}`))) return;
    this.changed.update((n) => n + 1);
    const root = this.root();
    for (const draft of this.drafts().filter((d) => !d.dirty && changed(d.path))) {
      try {
        const settings = await this.bridge.send('folder.settings.load', { path: draft.path });
        if (this.root() !== root) return;
        this.patchDraft(draft.key, (current) =>
          current.path === draft.path && !current.dirty
            ? { ...current, ...fromSettingsDto(settings), revision: current.revision + 1 }
            : current,
        );
      } catch {
        // Keep the draft open; saving reports what is wrong.
      }
    }
  }

  private showOpen(path: string): boolean {
    const open = this.drafts().find((d) => d.path === path);
    if (open) this.workspace.activateTab(open.key);
    return !!open;
  }

  private remove(drafts: FolderDraft[]): void {
    const keys = new Set(drafts.map((d) => d.key));
    if (!keys.size) return;
    this.drafts.update((all) => all.filter((d) => !keys.has(d.key)));
    this.workspace.removeTabs([...keys]);
  }

  private patchDraft(key: string, change: (draft: FolderDraft) => FolderDraft): void {
    this.drafts.update((drafts) => drafts.map((d) => (d.key === key ? change(d) : d)));
  }

  private report(error: unknown): void {
    this.workspace.error.set(
      error instanceof BridgeError || error instanceof Error ? error.message : String(error),
    );
  }
}

function fromSettingsDto(
  dto: FolderSettingsDto,
): Pick<FolderDraft, 'headers' | 'auth' | 'variables'> {
  return {
    headers: dto.headers.map((h) => ({ ...h })),
    auth: dto.auth ? fromAuthDto(dto.auth) : { ...EMPTY_AUTH },
    variables: (dto.variables ?? []).map((v) => ({ ...v })),
  };
}

function isWithin(path: string, folder: string): boolean {
  return path === folder || path.startsWith(`${folder}/`);
}
