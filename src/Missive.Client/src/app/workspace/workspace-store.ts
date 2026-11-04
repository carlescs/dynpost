import { Injectable, computed, inject, signal } from '@angular/core';
import { Bridge } from '../bridge/bridge';
import { BridgeError, WorkspaceInfo } from '../bridge/messages';
import { RequestDocument, WorkspaceNode } from './models';

export type SidebarView = 'explorer' | 'git';

export const SIDEBAR_MIN_WIDTH = 180;
export const SIDEBAR_MAX_WIDTH = 600;
export const SIDEBAR_DEFAULT_WIDTH = 256;

/**
 * UI state for the open workspace: its folder, the file tree and the documents open in the editor.
 *
 * The workspace tree is placeholder sample data until requests are loaded from disk through the
 * bridge; all file work happens in the .NET backend.
 */
@Injectable({ providedIn: 'root' })
export class WorkspaceStore {
  private readonly bridge = inject(Bridge);

  /** Absolute path of the workspace folder, or `null` until the user opens one. */
  readonly workspacePath = signal<string | null>(null);
  readonly workspaceName = signal('my-api');
  readonly openError = signal<BridgeError | null>(null);
  /** Whether the workspace from the last session is still being reopened at startup. */
  readonly restoring = signal(false);

  readonly tree = signal<WorkspaceNode[]>([
    {
      path: 'users',
      name: 'users',
      kind: 'folder',
      children: [
        { path: 'users/get-user.yaml', name: 'Get user', kind: 'request', method: 'GET' },
        { path: 'users/create-user.yaml', name: 'Create user', kind: 'request', method: 'POST' },
        { path: 'users/delete-user.yaml', name: 'Delete user', kind: 'request', method: 'DELETE' },
      ],
    },
    {
      path: 'orders',
      name: 'orders',
      kind: 'folder',
      children: [
        { path: 'orders/list-orders.yaml', name: 'List orders', kind: 'request', method: 'GET' },
      ],
    },
    { path: 'health.yaml', name: 'Health check', kind: 'request', method: 'GET' },
  ]);

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

  constructor() {
    if (this.bridge.available) void this.restoreLastWorkspace();
  }

  /** Asks the host for a workspace folder and opens it; does nothing if the user cancels. */
  async openWorkspace(): Promise<void> {
    this.openError.set(null);
    const picked = await this.bridge.request('workspace.pickFolder', {});
    if (!picked.ok) {
      this.openError.set(picked.error);
      return;
    }
    if (!picked.value.path) return;

    const opened = await this.bridge.request('workspace.open', {
      workspacePath: picked.value.path,
    });
    if (opened.ok) this.setWorkspace(opened.value);
    else this.openError.set(opened.error);
  }

  /** Reopens the workspace from the last session, if its folder still exists. */
  private async restoreLastWorkspace(): Promise<void> {
    this.restoring.set(true);
    try {
      const last = await this.bridge.request('workspace.getLast', {});
      // Don't override a folder the user opened in the meantime.
      if (last.ok && last.value.workspace && !this.workspacePath()) {
        this.setWorkspace(last.value.workspace);
      }
    } finally {
      this.restoring.set(false);
    }
  }

  private setWorkspace(workspace: WorkspaceInfo): void {
    this.workspacePath.set(workspace.path);
    this.workspaceName.set(workspace.name);
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

  newRequest(): void {
    this.untitledCount++;
    const doc: RequestDocument = {
      id: `untitled-${this.untitledCount}`,
      path: null,
      name: `New request ${this.untitledCount}`,
      method: 'GET',
      url: '',
      params: [],
      headers: [],
      auth: 'none',
      bodyType: 'none',
      body: '',
      dirty: true,
    };
    this.documents.update((docs) => [...docs, doc]);
    this.activeId.set(doc.id);
  }

  openRequest(node: WorkspaceNode): void {
    if (node.kind !== 'request') return;
    if (!this.documents().some((d) => d.id === node.path)) {
      // Placeholder until requests are loaded from disk through the bridge.
      const doc: RequestDocument = {
        id: node.path,
        path: node.path,
        name: node.name,
        method: node.method ?? 'GET',
        url: `{{baseUrl}}/${node.path.replace(/\.yaml$/, '')}`,
        params: [],
        headers: [{ key: 'Accept', value: 'application/json', enabled: true }],
        auth: 'none',
        bodyType: 'none',
        body: '',
        dirty: false,
      };
      this.documents.update((docs) => [...docs, doc]);
    }
    this.activeId.set(node.path);
  }

  activate(id: string): void {
    this.activeId.set(id);
  }

  close(id: string): void {
    const docs = this.documents();
    const index = docs.findIndex((d) => d.id === id);
    if (index < 0) return;
    const remaining = docs.filter((d) => d.id !== id);
    this.documents.set(remaining);
    if (this.activeId() === id) {
      const next = remaining[Math.min(index, remaining.length - 1)];
      this.activeId.set(next?.id ?? null);
    }
  }

  update(id: string, patch: Partial<Omit<RequestDocument, 'id' | 'path' | 'dirty'>>): void {
    this.documents.update((docs) =>
      docs.map((d) => (d.id === id ? { ...d, ...patch, dirty: true } : d)),
    );
  }
}
