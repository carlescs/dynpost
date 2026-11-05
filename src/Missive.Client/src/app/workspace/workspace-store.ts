import { Injectable, computed, inject, signal } from '@angular/core';
import { Bridge, BridgeError } from '../bridge/bridge';
import { ERROR_CODES, WorkspaceDto } from '../bridge/messages';
import {
  RequestContent,
  RequestDocument,
  ResponseState,
  WorkspaceInfo,
  WorkspaceNode,
} from './models';
import { EMPTY_AUTH, fromNodeDto, fromRequestDto, toRequestDto } from './request-mapping';

export type SidebarView = 'explorer' | 'environments' | 'git';

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

  readonly isDesktop = this.bridge.isDesktop;
  readonly workspace = signal<WorkspaceInfo | null>(null);
  readonly tree = signal<WorkspaceNode[]>([]);
  readonly opening = signal(false);
  /** The last error to show the user, or `null`. */
  readonly error = signal<string | null>(null);

  readonly sidebarView = signal<SidebarView>('explorer');
  readonly sidebarOpen = signal(true);
  /** Sidebar width in pixels; kept here so it survives closing and reopening the sidebar. */
  readonly sidebarWidth = signal(SIDEBAR_DEFAULT_WIDTH);

  readonly documents = signal<RequestDocument[]>([]);
  readonly activeId = signal<string | null>(null);
  /**
   * Incremented whenever the user asks to see a request (opening, creating or selecting one, even
   * the one already selected), so views shown instead of the editor can make way for it.
   */
  readonly shown = signal(0);
  readonly activeDocument = computed(
    () => this.documents().find((d) => d.id === this.activeId()) ?? null,
  );

  private untitledCount = 0;
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
  }

  /** Asks the user for a folder and opens it as the workspace. */
  async openFolder(): Promise<void> {
    if (!this.confirmDiscard('Open another folder?')) return;
    this.opening.set(true);
    try {
      const workspace = await this.bridge.send('workspace.pick');
      if (workspace) {
        this.documents.set([]);
        this.activeId.set(null);
        this.applyWorkspace(workspace);
      }
    } catch (error) {
      this.report(error);
    } finally {
      this.opening.set(false);
    }
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
   * aborted merge): all of them, or those at `paths`. Documents with unsaved edits are left alone.
   */
  async reloadFromDisk(paths?: string[]): Promise<void> {
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
      } catch {
        // Gone or unreadable (for example still full of conflict markers): keep what is open.
      }
    }
  }

  showView(view: SidebarView): void {
    if (this.sidebarView() === view) {
      this.sidebarOpen.update((open) => !open);
    } else {
      this.sidebarView.set(view);
      this.sidebarOpen.set(true);
    }
  }

  setSidebarWidth(width: number): void {
    this.sidebarWidth.set(
      Math.round(Math.min(SIDEBAR_MAX_WIDTH, Math.max(SIDEBAR_MIN_WIDTH, width))),
    );
  }

  /** Opens an unsaved request that will be saved in `folder` (`''` is the workspace root). */
  newRequest(folder = ''): void {
    this.untitledCount++;
    const doc: RequestDocument = {
      id: `untitled-${this.untitledCount}`,
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
      dirty: true,
      saving: false,
      sendId: null,
      response: null,
      revision: 0,
    };
    this.documents.update((docs) => [...docs, doc]);
    this.activeId.set(doc.id);
    this.shown.update((n) => n + 1);
  }

  async openRequest(node: WorkspaceNode): Promise<void> {
    if (node.kind !== 'request') return;
    this.shown.update((n) => n + 1);
    if (this.documents().some((d) => d.id === node.path)) {
      this.activeId.set(node.path);
      return;
    }

    try {
      const content = fromRequestDto(await this.bridge.send('request.load', { path: node.path }));
      // Another click may have opened it while loading.
      if (!this.documents().some((d) => d.id === node.path)) {
        const doc: RequestDocument = {
          ...content,
          id: node.path,
          path: node.path,
          folder: parentFolder(node.path),
          dirty: false,
          saving: false,
          sendId: null,
          response: null,
          revision: 0,
        };
        this.documents.update((docs) => [...docs, doc]);
      }
      this.activeId.set(node.path);
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
      if (this.activeId() === id) this.activeId.set(saved.path);
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
      });
      response = { kind: 'response', response: result };
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

  /** Creates a folder inside `parent` (`''` is the root). Returns its path, or `null` on failure. */
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

  /** Moves a folder into `parent` (`''` is the root); open requests inside it follow. */
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

  /** Moves a request file into `folder` (`''` is the root); its open tab follows. */
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
    if (!confirm(question)) return;

    try {
      await this.bridge.send('folder.delete', { path });
      const closed = new Set(open.map((d) => d.id));
      const remaining = this.documents().filter((d) => !closed.has(d.id));
      this.documents.set(remaining);
      if (closed.has(this.activeId() ?? '')) this.activeId.set(remaining.at(-1)?.id ?? null);
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

  activate(id: string): void {
    this.activeId.set(id);
    this.shown.update((n) => n + 1);
  }

  close(id: string): void {
    const docs = this.documents();
    const index = docs.findIndex((d) => d.id === id);
    if (index < 0) return;
    if (docs[index].dirty && !confirm(`Discard unsaved changes to "${docs[index].name}"?`)) return;

    const sendId = docs[index].sendId;
    if (sendId) void this.bridge.send('request.cancel', { id: sendId }).catch(() => undefined);

    const remaining = docs.filter((d) => d.id !== id);
    this.documents.set(remaining);
    if (this.activeId() === id) {
      const next = remaining[Math.min(index, remaining.length - 1)];
      this.activeId.set(next?.id ?? null);
    }
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
    this.documents.update((docs) =>
      docs.map((d) => {
        if (!d.path) return { ...d, folder: move(d.folder) };
        const path = move(d.path);
        return { ...d, id: path, path, folder: parentFolder(path) };
      }),
    );
    const active = this.activeId();
    if (active) this.activeId.set(move(active));
    this.error.set(null);
    await this.refresh();
  }

  private applyWorkspace(workspace: WorkspaceDto): void {
    this.workspace.set({ name: workspace.name, root: workspace.root });
    this.tree.set(workspace.nodes.map(fromNodeDto));
  }

  private patchDocument(id: string, patch: Partial<RequestDocument>): void {
    this.documents.update((docs) => docs.map((d) => (d.id === id ? { ...d, ...patch } : d)));
  }

  private confirmDiscard(question: string): boolean {
    const dirty = this.documents().filter((d) => d.dirty).length;
    return dirty === 0 || confirm(`${question} ${dirty} request(s) have unsaved changes.`);
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
