import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { Bridge, BridgeError } from '../bridge/bridge';
import { EnvironmentSummaryDto, EnvironmentVariableDto } from '../bridge/messages';
import { VariableLookup } from '../editor/variables';
import { WorkspaceStore } from '../workspace/workspace-store';

/** An environment open in the environment editor. */
export interface EnvironmentDraft {
  /** The environment's file name; `null` for a new environment that has not been saved yet. */
  id: string | null;
  name: string;
  variables: EnvironmentVariableDto[];
  dirty: boolean;
  saving: boolean;
  /** Incremented on every edit, so a save can tell whether the draft changed meanwhile. */
  revision: number;
}

/**
 * The open workspace's environments: the list, the selected one (whose variables requests are
 * sent with) and the environment being edited. Files are read and written by the .NET host.
 */
@Injectable({ providedIn: 'root' })
export class EnvironmentStore {
  private readonly bridge = inject(Bridge);
  private readonly workspace = inject(WorkspaceStore);

  readonly environments = signal<EnvironmentSummaryDto[]>([]);
  readonly activeId = signal<string | null>(null);
  readonly active = computed(
    () => this.environments().find((e) => e.id === this.activeId()) ?? null,
  );
  /** The selected environment's variables, for highlighting and hovers in the editors. */
  readonly variables = signal<VariableLookup>(new Map());
  /** The environment shown in the main area for editing, if any. */
  readonly draft = signal<EnvironmentDraft | null>(null);
  /**
   * Incremented whenever the user asks to see an environment (even the one already open), so
   * views shown instead of the editor can make way for it.
   */
  readonly shown = signal(0);

  private readonly root = computed(() => this.workspace.workspace()?.root ?? null);
  private loadCount = 0;

  constructor() {
    effect(() => {
      const root = this.root();
      untracked(() => {
        this.environments.set([]);
        this.activeId.set(null);
        this.variables.set(new Map());
        this.draft.set(null);
        if (root) void this.refresh();
      });
    });
  }

  /** Re-reads the list of environments and the selected environment's variables. */
  async refresh(): Promise<void> {
    const root = this.root();
    if (!root) return;
    const load = ++this.loadCount;
    try {
      const list = await this.bridge.send('environment.list');
      if (load !== this.loadCount || this.root() !== root) return;
      this.environments.set(list.environments);
      this.activeId.set(list.active);
      await this.loadVariables(load);
    } catch (error) {
      this.report(error);
    }
  }

  /** Selects the environment requests are sent with, or none. */
  async select(id: string | null): Promise<void> {
    if (id === this.activeId()) return;
    try {
      await this.bridge.send('environment.select', { id });
      this.activeId.set(id);
      await this.loadVariables(++this.loadCount);
    } catch (error) {
      this.report(error);
      await this.refresh();
    }
  }

  /** Opens an environment in the editor. */
  async edit(id: string): Promise<void> {
    if (this.draft()?.id === id) {
      this.shown.update((n) => n + 1);
      return;
    }
    if (!this.confirmDiscard()) return;
    try {
      const environment = await this.bridge.send('environment.load', { id });
      this.draft.set({ ...environment, id, dirty: false, saving: false, revision: 0 });
      this.shown.update((n) => n + 1);
    } catch (error) {
      this.report(error);
    }
  }

  /** Opens a new, unsaved environment in the editor. */
  create(): void {
    if (!this.confirmDiscard()) return;
    this.draft.set({
      id: null,
      name: 'New environment',
      variables: [],
      dirty: true,
      saving: false,
      revision: 0,
    });
    this.shown.update((n) => n + 1);
  }

  update(patch: Partial<Pick<EnvironmentDraft, 'name' | 'variables'>>): void {
    this.draft.update((d) => d && { ...d, ...patch, dirty: true, revision: d.revision + 1 });
  }

  /** Writes the draft to its file (or creates the file); a new environment is selected if none is. */
  async save(): Promise<void> {
    const draft = this.draft();
    if (!draft || draft.saving) return;
    this.draft.set({ ...draft, saving: true });
    try {
      const environment = { name: draft.name, variables: draft.variables };
      const saved = draft.id
        ? await this.bridge.send('environment.save', { id: draft.id, environment })
        : await this.bridge.send('environment.create', { environment });

      this.draft.update((d) => {
        if (!d) return d;
        const base = { ...d, id: saved.id, saving: false };
        // Keep edits typed while the save was in flight; they still need saving.
        return d.revision === draft.revision
          ? { ...base, ...saved.environment, dirty: false }
          : base;
      });
      this.workspace.dismissError();
      if (!draft.id && !this.activeId()) {
        await this.select(saved.id);
      }
      await this.refresh();
    } catch (error) {
      this.draft.update((d) => d && { ...d, saving: false });
      this.report(error);
    }
  }

  /** Deletes the environment in the editor, after asking the user. */
  async deleteDraft(): Promise<void> {
    const draft = this.draft();
    if (!draft) return;
    if (!draft.id) {
      this.draft.set(null);
      return;
    }
    if (!confirm(`Delete the environment "${draft.name}" and the secret values stored for it?`)) {
      return;
    }
    try {
      await this.bridge.send('environment.delete', { id: draft.id });
      this.draft.set(null);
      await this.refresh();
    } catch (error) {
      this.report(error);
    }
  }

  /** Closes the editor, after asking if it has unsaved changes. */
  close(): void {
    if (this.confirmDiscard()) this.draft.set(null);
  }

  private async loadVariables(load: number): Promise<void> {
    const id = this.activeId();
    let variables: VariableLookup = new Map();
    if (id) {
      try {
        const environment = await this.bridge.send('environment.load', { id });
        variables = new Map(
          environment.variables
            .filter((v) => !v.secret || v.value)
            .map((v) => [v.name, { value: v.value, secret: v.secret }]),
        );
      } catch {
        // An unreadable environment defines nothing; sending explains why.
      }
    }
    if (load === this.loadCount) this.variables.set(variables);
  }

  private confirmDiscard(): boolean {
    const draft = this.draft();
    return (
      !draft?.dirty ||
      confirm(`Discard unsaved changes to the environment "${draft.name || 'Untitled'}"?`)
    );
  }

  private report(error: unknown): void {
    this.workspace.error.set(
      error instanceof BridgeError || error instanceof Error ? error.message : String(error),
    );
  }
}
