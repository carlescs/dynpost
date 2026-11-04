import { Injectable, computed, inject, signal } from '@angular/core';
import { Bridge, BridgeError } from '../bridge/bridge';
import { WorkspaceDto } from '../bridge/messages';
import { GitState, RequestContent, RequestDocument, WorkspaceInfo, WorkspaceNode } from './models';
import { EMPTY_AUTH, fromNodeDto, fromRequestDto, toRequestDto } from './request-mapping';

export type SidebarView = 'explorer' | 'git';

export const SIDEBAR_MIN_WIDTH = 180;
export const SIDEBAR_MAX_WIDTH = 600;
export const SIDEBAR_DEFAULT_WIDTH = 256;

const LAST_WORKSPACE_KEY = 'missive.lastWorkspace';

/**
 * UI state for the open workspace: the file tree, git status and the documents open in the
 * editor. All file work happens in the .NET host through the {@link Bridge}; this store only
 * mirrors what is on disk.
 *
 * Git status is still placeholder sample data until git support exists in the host.
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

  readonly git = signal<GitState>({
    branch: 'main',
    ahead: 1,
    behind: 0,
    changes: [
      { path: 'users/create-user.yaml', status: 'modified' },
      { path: 'orders/list-orders.yaml', status: 'added' },
    ],
  });

  readonly sidebarView = signal<SidebarView>('explorer');
  readonly sidebarOpen = signal(true);
  /** Sidebar width in pixels; kept here so it survives closing and reopening the sidebar. */
  readonly sidebarWidth = signal(SIDEBAR_DEFAULT_WIDTH);

  readonly documents = signal<RequestDocument[]>([]);
  readonly activeId = signal<string | null>(null);
  readonly activeDocument = computed(
    () => this.documents().find((d) => d.id === this.activeId()) ?? null,
  );

  private untitledCount = 0;

  /** Reopens the workspace from the previous session, if it still exists. */
  async restore(): Promise<void> {
    const root = readLastWorkspace();
    if (!root || this.workspace()) return;
    try {
      this.applyWorkspace(await this.bridge.send('workspace.open', { path: root }));
    } catch {
      // The folder may have been moved or deleted; start without a workspace.
      writeLastWorkspace(null);
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
      revision: 0,
    };
    this.documents.update((docs) => [...docs, doc]);
    this.activeId.set(doc.id);
  }

  async openRequest(node: WorkspaceNode): Promise<void> {
    if (node.kind !== 'request') return;
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

  async saveActive(): Promise<void> {
    const id = this.activeId();
    if (id) await this.save(id);
  }

  activate(id: string): void {
    this.activeId.set(id);
  }

  close(id: string): void {
    const docs = this.documents();
    const index = docs.findIndex((d) => d.id === id);
    if (index < 0) return;
    if (docs[index].dirty && !confirm(`Discard unsaved changes to "${docs[index].name}"?`)) return;

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

  private applyWorkspace(workspace: WorkspaceDto): void {
    this.workspace.set({ name: workspace.name, root: workspace.root });
    this.tree.set(workspace.nodes.map(fromNodeDto));
    writeLastWorkspace(workspace.root);
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

function parentFolder(path: string): string {
  const index = path.lastIndexOf('/');
  return index < 0 ? '' : path.slice(0, index);
}

function readLastWorkspace(): string | null {
  try {
    return localStorage.getItem(LAST_WORKSPACE_KEY);
  } catch {
    return null;
  }
}

function writeLastWorkspace(root: string | null): void {
  try {
    if (root) localStorage.setItem(LAST_WORKSPACE_KEY, root);
    else localStorage.removeItem(LAST_WORKSPACE_KEY);
  } catch {
    // Storage can be unavailable; remembering the workspace is only a convenience.
  }
}
