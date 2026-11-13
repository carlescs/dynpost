import { Injectable, computed, inject, signal } from '@angular/core';
import { Bridge, BridgeError } from './bridge';
import { ERROR_CODES, RecentWorkspaceDto, WorkspaceDto } from '../models/messages';
import { describeMigration } from '../models/layout-migration';
import {
  REQUESTS_FOLDER,
  RequestContent,
  RequestDocument,
  ResponseState,
  WorkspaceInfo,
  WorkspaceNode,
} from '../models/workspace';
import { EMPTY_AUTH, fromNodeDto, fromRequestDto, toRequestDto } from '../models/request-mapping';
import { DISCARD, Dialogs } from './dialogs';

export const SIDEBAR_MIN_WIDTH = 180;
export const SIDEBAR_MAX_WIDTH = 600;
export const SIDEBAR_DEFAULT_WIDTH = 256;

/**
 * UI state for the open workspace: the file tree, git status and the documents open in the
 * editor. All file work happens in the .NET host through the {@link Bridge}; this store only
 * mirrors what is on disk.
 *
 * Git state lives in the `GitStore`.
 */
@Injectable({ providedIn: 'root' })
export class WorkspaceStore {
  private readonly bridge = inject(Bridge);
  private readonly dialogs = inject(Dialogs);

  readonly isDesktop = this.bridge.isDesktop;
  readonly workspace = signal<WorkspaceInfo | null>(null);
  readonly tree = signal<WorkspaceNode[]>([]);
  readonly opening = signal(false);
  /** Workspace folders opened recently, the most recent first (the open one included). */
  readonly recent = signal<RecentWorkspaceDto[]>([]);
  /** The last error to show the user, or `null`. */
  readonly error = signal<string | null>(null);
  /** Incremented whenever a response's captured values were stored, so variables can be re-read. */
  readonly capturesStored = signal(0);
  /** Incremented whenever a file was made local-only or shared again, so git status can be re-read. */
  readonly sharingChanged = signal(0);
  /** The last request or folder renamed or moved, so the editors of folders inside it can follow it. */
  readonly moved = signal<{ from: string; to: string } | null>(null);
  /** The last folder deleted, so the editors of folders inside it can close. */
  readonly folderDeleted = signal<{ path: string } | null>(null);
  /**
   * Set whenever open files are re-read from disk after git or an AI assistant changed them: the
   * paths that changed, or all files when `paths` is missing.
   */
  readonly reloaded = signal<{ paths?: string[] } | null>(null);

  readonly sidebarOpen = signal(true);
  /** Sidebar width in pixels; kept here so it survives closing and reopening the sidebar. */
  readonly sidebarWidth = signal(SIDEBAR_DEFAULT_WIDTH);

  readonly documents = signal<RequestDocument[]>([]);
  /**
   * Keys of the open tabs, in order. A tab shows a request document from {@link documents} or an
   * environment from the `EnvironmentStore`, matched by their `key`.
   */
  readonly tabs = signal<string[]>([]);
  /** Key of the tab shown in the editor area, or `null`. */
  readonly activeTab = signal<string | null>(null);
  /**
   * Incremented whenever the user opens or selects a tab (even the one already shown), so views
   * shown instead of the editor area can make way for it.
   */
  readonly shown = signal(0);
  /** The request document in the active tab; `null` when an environment tab (or none) is active. */
  readonly activeDocument = computed(
    () => this.documents().find((d) => d.key === this.activeTab()) ?? null,
  );
  readonly activeId = computed(() => this.activeDocument()?.id ?? null);

  private untitledCount = 0;
  private tabCount = 0;
  private sendCount = 0;

  /**
   * Reopens the workspace from the previous session, if it still exists. The host remembers it in
   * the user's settings file, which outlives the webview's storage.
   */
  async restore(): Promise<void> {
    if (this.workspace()) return;
    try {
      const workspace = await this.bridge.send('workspace.openLast');
      // Don't replace a folder the user opened in the meantime.
      if (workspace && !this.workspace()) this.applyWorkspace(workspace);
    } catch {
      // Start without a workspace; the user can open one.
    }
    await this.loadRecent();
  }

  /** Re-reads the recently opened workspaces; the host drops folders that no longer exist. */
  async loadRecent(): Promise<void> {
    try {
      this.recent.set(await this.bridge.send('workspace.recent'));
    } catch {
      // The list is a convenience; keep the one shown.
    }
  }

  /** Asks the user for a folder and opens it as the workspace. */
  async openFolder(): Promise<void> {
    await this.switchWorkspace(() => this.bridge.send('workspace.pick'));
  }

  /** Opens a recently used workspace folder. */
  async openRecent(path: string): Promise<void> {
    if (path === this.workspace()?.root) return;
    await this.switchWorkspace(() => this.bridge.send('workspace.open', { path }));
  }

  /** Removes a workspace from the recently opened list. */
  async removeRecent(path: string): Promise<void> {
    try {
      await this.bridge.send('workspace.recent.remove', { path });
      this.recent.update((recent) => recent.filter((workspace) => workspace.path !== path));
    } catch (error) {
      this.report(error);
    }
  }

  /** Shows the open workspace folder in the system file manager (File Explorer on Windows). */
  async revealFolder(): Promise<void> {
    try {
      await this.bridge.send('workspace.reveal');
    } catch (error) {
      this.report(error);
    }
  }

  /**
   * Replaces the open workspace with the one `open` returns (`null` when the user cancelled),
   * closing the open documents.
   */
  private async switchWorkspace(open: () => Promise<WorkspaceDto | null>): Promise<void> {
    if (!(await this.confirmDiscard('Open another folder?'))) return;
    this.opening.set(true);
    try {
      const workspace = await open();
      if (workspace) {
        this.removeTabs(this.documents().map((d) => d.key));
        this.documents.set([]);
        this.applyWorkspace(workspace);
        this.error.set(null);
      }
    } catch (error) {
      this.report(error);
    } finally {
      this.opening.set(false);
    }
    await this.loadRecent();
  }

  /** Re-reads the workspace tree from disk. */
  async refresh(): Promise<void> {
    if (!this.workspace()) return;
    try {
      this.applyWorkspace(await this.bridge.send('workspace.tree'));
    } catch (error) {
      this.report(error);
    }
  }

  /**
   * Re-reads open documents from disk after git changed files (a pull, a resolved conflict, an
   * aborted merge): all of them, or those at `paths`. Unsaved edits are preserved, and removed
   * files are detached from their open drafts.
   */
  async reloadFromDisk(paths?: string[]): Promise<void> {
    this.reloaded.set({ paths });
    const targets = this.documents().filter(
      (d) => d.path && !d.dirty && (!paths || paths.includes(d.path)),
    );
    for (const doc of targets) {
      try {
        const content = fromRequestDto(await this.bridge.send('request.load', { path: doc.path! }));
        this.documents.update((docs) =>
          docs.map((d) =>
            d.id === doc.id && !d.dirty ? { ...d, ...content, revision: d.revision + 1 } : d,
          ),
        );
      } catch (error) {
        // Keep unreadable files open, but reconcile files removed by git.
        if (!(error instanceof BridgeError) || error.code !== 'not_found') continue;
        const current = this.documents().find((d) => d.key === doc.key);
        if (!current || current.path !== doc.path) continue;
        if (current.dirty) {
          this.documents.update((docs) =>
            docs.map((d) =>
              d.key === current.key
                ? { ...d, id: `untitled-${++this.untitledCount}`, path: null }
                : d,
            ),
          );
        } else {
          this.discard(current);
        }
      }
    }
  }

  /** Shows or hides the sidebar; which panel it shows is chosen by the route. */
  toggleSidebar(open = !this.sidebarOpen()): void {
    this.sidebarOpen.set(open);
  }

  setSidebarWidth(width: number): void {
    this.sidebarWidth.set(
      Math.round(Math.min(SIDEBAR_MAX_WIDTH, Math.max(SIDEBAR_MIN_WIDTH, width))),
    );
  }

  /** Opens an unsaved request that will be saved in `folder` (by default the top of `requests`). */
  newRequest(folder = REQUESTS_FOLDER): void {
    this.untitledCount++;
    const doc: RequestDocument = {
      id: `untitled-${this.untitledCount}`,
      key: this.newTabKey(),
      path: null,
      folder,
      name: `New request ${this.untitledCount}`,
      method: 'GET',
      url: '',
      params: [],
      headers: [],
      auth: { ...EMPTY_AUTH },
      bodyType: 'none',
      body: '',
      bodyVariables: '',
      bodyFields: null,
      description: '',
      captures: [],
      dirty: true,
      saving: false,
      sendId: null,
      response: null,
      revision: 0,
    };
    this.documents.update((docs) => [...docs, doc]);
    this.openTab(doc.key);
  }

  async openRequest(node: WorkspaceNode): Promise<void> {
    if (node.kind !== 'request') return;
    const open = this.documents().find((d) => d.id === node.path);
    if (open) {
      this.activateTab(open.key);
      return;
    }

    try {
      const content = fromRequestDto(await this.bridge.send('request.load', { path: node.path }));
      // Another click may have opened it while loading.
      const opened = this.documents().find((d) => d.id === node.path);
      if (opened) {
        this.activateTab(opened.key);
      } else {
        const doc: RequestDocument = {
          ...content,
          id: node.path,
          key: this.newTabKey(),
          path: node.path,
          folder: parentFolder(node.path),
          dirty: false,
          saving: false,
          sendId: null,
          response: null,
          revision: 0,
        };
        this.documents.update((docs) => [...docs, doc]);
        this.openTab(doc.key);
      }
    } catch (error) {
      this.report(error);
    }
  }

  /** Writes a document to its file (or creates the file for a new request). */
  async save(id: string): Promise<void> {
    const doc = this.documents().find((d) => d.id === id);
    if (!doc || doc.saving) return;
    if (!this.workspace()) {
      this.error.set('Open a workspace folder to save requests.');
      return;
    }

    this.patchDocument(id, { saving: true });
    try {
      const request = toRequestDto(doc);
      const saved = doc.path
        ? await this.bridge.send('request.save', { path: doc.path, request })
        : await this.bridge.send('request.create', { folder: doc.folder, request });

      this.documents.update((docs) =>
        docs.map((d) => {
          if (d.id !== id) return d;
          const base = { ...d, id: saved.path, path: saved.path, saving: false };
          // Keep edits typed while the save was in flight; they still need saving.
          return d.revision === doc.revision
            ? { ...base, ...fromRequestDto(saved.request), dirty: false }
            : base;
        }),
      );
      this.error.set(null);
      await this.refresh();
    } catch (error) {
      this.patchDocument(id, { saving: false });
      this.report(error);
    }
  }

  /**
   * Sends a document's current content (saved or not) from the host and keeps the response with
   * the document. Does nothing while it is already being sent.
   */
  async send(id: string): Promise<void> {
    const doc = this.documents().find((d) => d.id === id);
    if (!doc || doc.sendId) return;

    const sendId = `send-${++this.sendCount}`;
    this.patchDocument(id, { sendId });
    let response: ResponseState;
    try {
      const result = await this.bridge.send('request.send', {
        id: sendId,
        request: toRequestDto(doc),
        // A saved request's responses are kept in its history.
        path: doc.path,
        // An unsaved one inherits the headers and auth of the folder it will be saved in.
        folder: doc.path ? null : doc.folder,
      });
      response = { kind: 'response', response: result, sendId };
      if (result.captures?.some((c) => c.value != null)) this.capturesStored.update((n) => n + 1);
    } catch (error) {
      response =
        error instanceof BridgeError
          ? { kind: 'error', code: error.code, message: error.message }
          : { kind: 'error', code: ERROR_CODES.internal, message: String(error) };
    }
    // Match by send id: the document's id changes if it is saved or moved meanwhile.
    this.documents.update((docs) =>
      docs.map((d) => (d.sendId === sendId ? { ...d, sendId: null, response } : d)),
    );
  }

  async sendActive(): Promise<void> {
    const id = this.activeId();
    if (id) await this.send(id);
  }

  /**
   * Asks where to save the body of a document's last response and writes it exactly as received.
   * Returns the path written, or `null` when cancelled or when it failed (the error is reported).
   */
  async saveResponseBody(id: string): Promise<string | null> {
    const state = this.documents().find((d) => d.id === id)?.response;
    if (state?.kind !== 'response') return null;
    try {
      const saved = await this.bridge.send('response.saveBody', { id: state.sendId });
      return saved?.path ?? null;
    } catch (error) {
      this.report(error);
      return null;
    }
  }

  /** Cancels the send in progress for a document, if any. */
  async cancelSend(id: string): Promise<void> {
    const sendId = this.documents().find((d) => d.id === id)?.sendId;
    if (!sendId) return;
    try {
      await this.bridge.send('request.cancel', { id: sendId });
    } catch (error) {
      this.report(error);
    }
  }

  /** Creates a folder inside `parent` (`requests` for the top level). Returns its path, or `null` on failure. */
  async createFolder(parent: string, name: string): Promise<string | null> {
    try {
      const { path } = await this.bridge.send('folder.create', { parent, name });
      this.error.set(null);
      await this.refresh();
      return path;
    } catch (error) {
      this.report(error);
      return null;
    }
  }

  /** Renames a folder; open requests inside it follow. Returns whether it worked. */
  async renameFolder(path: string, name: string): Promise<boolean> {
    try {
      const renamed = await this.bridge.send('folder.rename', { path, name });
      await this.relocated(path, renamed.path);
      return true;
    } catch (error) {
      this.report(error);
      return false;
    }
  }

  /** Moves a folder into `parent` (`requests` for the top level); open requests inside it follow. */
  async moveFolder(path: string, parent: string): Promise<boolean> {
    if (parent === parentFolder(path) || isWithin(parent, path)) return false;
    try {
      const moved = await this.bridge.send('folder.move', { path, parent });
      await this.relocated(path, moved.path);
      return true;
    } catch (error) {
      this.report(error);
      return false;
    }
  }

  /** Moves a request file into `folder` (`requests` for the top level); its open tab follows. */
  async moveRequest(path: string, folder: string): Promise<boolean> {
    if (folder === parentFolder(path)) return false;
    try {
      const moved = await this.bridge.send('request.move', { path, folder });
      await this.relocated(path, moved.path);
      return true;
    } catch (error) {
      this.report(error);
      return false;
    }
  }

  /** Copies a request next to it as "… copy" and opens the copy. */
  async duplicateRequest(path: string): Promise<void> {
    try {
      const saved = await this.bridge.send('request.duplicate', { path });
      this.error.set(null);
      await this.refresh();
      const node = findNode(this.tree(), saved.path);
      if (node) await this.openRequest(node);
    } catch (error) {
      this.report(error);
    }
  }

  /** Copies a folder with everything in it next to it as "… copy". Returns the copy's path. */
  async duplicateFolder(path: string): Promise<string | null> {
    try {
      const copy = await this.bridge.send('folder.duplicate', { path });
      this.error.set(null);
      await this.refresh();
      return copy.path;
    } catch (error) {
      this.report(error);
      return null;
    }
  }

  /**
   * Keeps a request, environment or run plan file on this computer only, or shares it through git
   * again. Making a file local-only asks the user first. Returns whether the file's sharing changed;
   * the caller re-reads its list.
   */
  async setLocalOnly(path: string, name: string, localOnly: boolean): Promise<boolean> {
    const question =
      `Keep "${name}" on this computer only?\n\n` +
      'It is added to .gitignore. If it was committed before, your next commit removes it from ' +
      'the repository, but the file stays here; others stop receiving changes to it.';
    if (
      localOnly &&
      !(await this.dialogs.confirm(question, {
        title: 'Make local-only?',
        confirmLabel: 'Keep local',
      }))
    ) {
      return false;
    }

    try {
      await this.bridge.send('sharing.set', { path, localOnly });
      this.error.set(null);
      this.sharingChanged.update((n) => n + 1);
      return true;
    } catch (error) {
      this.report(error);
      return false;
    }
  }

  /** Makes a request local-only or shared again, then re-reads the tree. */
  async setRequestLocalOnly(node: WorkspaceNode, localOnly: boolean): Promise<void> {
    if (await this.setLocalOnly(node.path, node.name, localOnly)) await this.refresh();
  }

  /** Deletes a request file, after asking the user, and closes its tab. */
  async deleteRequest(path: string): Promise<void> {
    const doc = this.documents().find((d) => d.path === path);
    const name = doc?.name || path;
    const question =
      `Delete the request "${name}"?` + (doc?.dirty ? ' Its unsaved changes will be lost.' : '');
    const ok = await this.dialogs.confirm(question, {
      title: 'Delete request?',
      confirmLabel: 'Delete',
      danger: true,
    });
    if (!ok) return;

    try {
      await this.bridge.send('request.delete', { path });
      if (doc) {
        this.documents.update((docs) => docs.filter((d) => d.key !== doc.key));
        this.removeTabs([doc.key]);
      }
      this.error.set(null);
      await this.refresh();
    } catch (error) {
      this.report(error);
    }
  }

  /**
   * Deletes a folder with the requests in it, after asking the user. Open requests from the folder
   * are closed.
   */
  async deleteFolder(path: string): Promise<void> {
    const requests = countRequests(findNode(this.tree(), path));
    const open = this.documents().filter((d) => isWithin(d.folder, path));
    const dirty = open.filter((d) => d.dirty).length;
    const question =
      `Delete the folder "${path}"` +
      (requests ? ` and the ${requests} request(s) in it?` : '?') +
      (dirty ? ` ${dirty} open request(s) with unsaved changes will be closed.` : '');
    const ok = await this.dialogs.confirm(question, {
      title: 'Delete folder?',
      confirmLabel: 'Delete',
      danger: true,
    });
    if (!ok) return;

    try {
      await this.bridge.send('folder.delete', { path });
      this.folderDeleted.set({ path });
      const closed = new Set(open.map((d) => d.key));
      this.documents.update((docs) => docs.filter((d) => !closed.has(d.key)));
      this.removeTabs([...closed]);
      this.error.set(null);
      await this.refresh();
    } catch (error) {
      this.report(error);
    }
  }

  async saveActive(): Promise<void> {
    const id = this.activeId();
    if (id) await this.save(id);
  }

  /** A key for a new tab, unique while the app runs. */
  newTabKey(): string {
    return `tab-${++this.tabCount}`;
  }

  /** Adds a tab after the others (if it isn't open yet) and shows it. */
  openTab(key: string): void {
    if (!this.tabs().includes(key)) this.tabs.update((tabs) => [...tabs, key]);
    this.activeTab.set(key);
    this.shown.update((n) => n + 1);
  }

  activateTab(key: string): void {
    this.activeTab.set(key);
    this.shown.update((n) => n + 1);
  }

  /** Removes tabs; if the active one is among them, its nearest remaining neighbour is shown. */
  removeTabs(keys: string[]): void {
    const removed = new Set(keys);
    const tabs = this.tabs();
    const active = this.activeTab();
    if (active && removed.has(active)) {
      const index = tabs.indexOf(active);
      const next =
        tabs.slice(index + 1).find((k) => !removed.has(k)) ??
        tabs
          .slice(0, Math.max(index, 0))
          .reverse()
          .find((k) => !removed.has(k)) ??
        null;
      this.activeTab.set(next);
    }
    this.tabs.set(tabs.filter((k) => !removed.has(k)));
  }

  /** Closes a request document's tab, after asking if it has unsaved changes. */
  async close(id: string): Promise<void> {
    const doc = this.documents().find((d) => d.id === id);
    if (!doc) return;
    if (
      doc.dirty &&
      !(await this.dialogs.confirm(`Discard unsaved changes to "${doc.name}"?`, DISCARD))
    ) {
      return;
    }
    this.discard(doc);
  }

  /** Closes a request document's tab without asking, cancelling its send if one is running. */
  private discard(doc: RequestDocument): void {
    if (doc.sendId) {
      void this.bridge.send('request.cancel', { id: doc.sendId }).catch(() => undefined);
    }
    this.documents.update((docs) => docs.filter((d) => d.key !== doc.key));
    this.removeTabs([doc.key]);
  }

  update(id: string, patch: Partial<RequestContent>): void {
    this.documents.update((docs) =>
      docs.map((d) =>
        d.id === id ? { ...d, ...patch, dirty: true, revision: d.revision + 1 } : d,
      ),
    );
  }

  dismissError(): void {
    this.error.set(null);
  }

  /**
   * Updates open documents after the file or folder at `from` moved to `to` on disk, then reloads
   * the tree.
   */
  private async relocated(from: string, to: string): Promise<void> {
    const move = (p: string) => (isWithin(p, from) ? to + p.slice(from.length) : p);
    this.moved.set({ from, to });
    this.documents.update((docs) =>
      docs.map((d) => {
        if (!d.path) return { ...d, folder: move(d.folder) };
        const path = move(d.path);
        return { ...d, id: path, path, folder: parentFolder(path) };
      }),
    );
    this.error.set(null);
    await this.refresh();
  }

  private applyWorkspace(workspace: WorkspaceDto): void {
    this.workspace.set({ name: workspace.name, root: workspace.root });
    this.tree.set(workspace.nodes.map(fromNodeDto));
    if (workspace.migration) {
      void this.dialogs.inform(describeMigration(workspace.migration), {
        title: 'Workspace reorganized',
      });
    }
  }

  private patchDocument(id: string, patch: Partial<RequestDocument>): void {
    this.documents.update((docs) => docs.map((d) => (d.id === id ? { ...d, ...patch } : d)));
  }

  private async confirmDiscard(question: string): Promise<boolean> {
    const dirty = this.documents().filter((d) => d.dirty).length;
    return (
      dirty === 0 ||
      this.dialogs.confirm(`${question} ${dirty} request(s) have unsaved changes.`, DISCARD)
    );
  }

  private report(error: unknown): void {
    this.error.set(
      error instanceof BridgeError || error instanceof Error ? error.message : String(error),
    );
  }
}

/** Whether `path` is `folder` itself or inside it. */
function isWithin(path: string, folder: string): boolean {
  return path === folder || path.startsWith(`${folder}/`);
}

function findNode(nodes: WorkspaceNode[], path: string): WorkspaceNode | null {
  for (const node of nodes) {
    if (node.path === path) return node;
    if (node.children && isWithin(path, node.path)) return findNode(node.children, path);
  }
  return null;
}

function countRequests(node: WorkspaceNode | null): number {
  if (!node) return 0;
  if (node.kind === 'request') return 1;
  return (node.children ?? []).reduce((sum, child) => sum + countRequests(child), 0);
}

function parentFolder(path: string): string {
  const index = path.lastIndexOf('/');
  return index < 0 ? '' : path.slice(0, index);
}
