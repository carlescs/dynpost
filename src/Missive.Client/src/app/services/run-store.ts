import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { Bridge, BridgeError } from './bridge';
import { ERROR_CODES, RunPlanFileDto, RunResultDto, RunSummaryDto } from '../models/messages';
import { WorkspaceStore } from './workspace-store';

/** A run plan open in the editor. */
export interface RunPlanDraft {
  path: string;
  yaml: string;
  /** The text on disk, to tell whether there are unsaved changes. */
  savedYaml: string;
  /** Why the plan can't be run, or `null`. */
  error: string | null;
}

const POLL_MS = 500;
const NEW_PLAN = (name: string) =>
  [
    `name: ${name}`,
    'requests:',
    '  - path/to/request.yaml',
    'load:',
    '  threads: 5',
    '  iterations: 10',
    '',
  ].join('\n');

/**
 * Load run plans (`*.run.yaml` files), the plan open in the editor and the run being followed.
 * A running plan is polled through `run.progress` until it ends; the host keeps going on its own.
 */
@Injectable({ providedIn: 'root' })
export class RunStore {
  private readonly bridge = inject(Bridge);
  private readonly workspace = inject(WorkspaceStore);
  private readonly root = computed(() => this.workspace.workspace()?.root ?? null);

  readonly plans = signal<RunPlanFileDto[]>([]);
  readonly draft = signal<RunPlanDraft | null>(null);
  /** Whether the plan editor is shown in the editor area (opening a request tab hides it). */
  readonly showing = signal(false);
  readonly run = signal<RunResultDto | null>(null);
  readonly history = signal<RunSummaryDto[]>([]);
  readonly saving = signal(false);
  readonly starting = signal(false);
  /** Set when a very large run needs the user's go-ahead. */
  readonly confirmation = signal<string | null>(null);
  readonly error = signal<string | null>(null);

  readonly dirty = computed(() => {
    const draft = this.draft();
    return !!draft && draft.yaml !== draft.savedYaml;
  });
  readonly running = computed(() => this.run()?.state === 'running');

  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private generation = 0;

  constructor() {
    effect(() => {
      const root = this.root();
      untracked(() => {
        this.stopPolling();
        this.draft.set(null);
        this.showing.set(false);
        this.run.set(null);
        this.history.set([]);
        this.plans.set([]);
        if (root) void this.refresh();
      });
    });
    effect(() => {
      this.workspace.shown();
      untracked(() => this.showing.set(false));
    });
  }

  async refresh(): Promise<void> {
    try {
      this.plans.set(await this.bridge.send('runPlan.list'));
    } catch (error) {
      this.report(error);
    }
  }

  async open(path: string): Promise<void> {
    const current = this.draft();
    if (current?.path === path) {
      this.showing.set(true);
      return;
    }
    if (this.running()) {
      this.error.set('Stop the running plan before opening another plan.');
      return;
    }
    if (this.dirty() && !confirm('Discard unsaved changes?')) return;
    try {
      const document = await this.bridge.send('runPlan.open', { path });
      this.stopPolling();
      this.run.set(null);
      this.confirmation.set(null);
      this.draft.set({ ...document, savedYaml: document.yaml });
      this.showing.set(true);
      await this.loadHistory();
    } catch (error) {
      this.report(error);
    }
  }

  /** Creates a plan file with a starting template and opens it. */
  async create(): Promise<void> {
    const taken = new Set(this.plans().map((plan) => plan.path));
    let index = 1;
    while (taken.has(`plan-${index}.run.yaml`)) index++;
    const path = `plan-${index}.run.yaml`;
    try {
      await this.bridge.send('runPlan.save', { path, yaml: NEW_PLAN(`Plan ${index}`) });
      await this.refresh();
      await this.open(path);
    } catch (error) {
      this.report(error);
    }
  }

  edit(yaml: string): void {
    this.draft.update((draft) => (draft ? { ...draft, yaml } : draft));
  }

  /** Writes the text to the file; a plan that can't be run yet is saved too. */
  async save(): Promise<boolean> {
    const draft = this.draft();
    if (!draft) return false;
    this.saving.set(true);
    try {
      const saved = await this.bridge.send('runPlan.save', { path: draft.path, yaml: draft.yaml });
      // Edits typed while saving stay as they are, and stay unsaved. Renaming the plan may have
      // renamed its file, so the draft follows the saved path.
      this.draft.update((current) =>
        current?.path === draft.path
          ? {
              ...current,
              path: saved.path,
              yaml: current.yaml === draft.yaml ? saved.yaml : current.yaml,
              savedYaml: saved.yaml,
              error: saved.error,
            }
          : current,
      );
      await this.refresh();
      return true;
    } catch (error) {
      this.report(error);
      return false;
    } finally {
      this.saving.set(false);
    }
  }

  /** Saves pending edits, then starts the plan; `confirmLarge` goes ahead with a very large run. */
  async start(confirmLarge = false): Promise<void> {
    const draft = this.draft();
    if (!draft || this.starting() || this.running()) return;
    this.starting.set(true);
    this.confirmation.set(null);
    try {
      if (this.dirty() && !(await this.save())) return;
      if (this.draft()?.error) {
        this.error.set(this.draft()!.error);
        return;
      }
      const { runId } = await this.bridge.send('run.start', { path: draft.path, confirmLarge });
      this.error.set(null);
      await this.follow(runId);
    } catch (error) {
      if (error instanceof BridgeError && error.code === ERROR_CODES.confirmationRequired) {
        this.confirmation.set(error.message);
      } else {
        this.report(error);
      }
    } finally {
      this.starting.set(false);
    }
  }

  async cancel(): Promise<void> {
    const run = this.run();
    if (!run || run.state !== 'running') return;
    try {
      await this.bridge.send('run.cancel', { runId: run.runId });
    } catch (error) {
      this.report(error);
    }
  }

  /** Shows an earlier run of the open plan. */
  async show(runId: string): Promise<void> {
    if (this.running()) {
      this.error.set('Stop the running plan before viewing history.');
      return;
    }
    this.stopPolling();
    try {
      this.run.set(await this.bridge.send('run.progress', { runId }));
    } catch (error) {
      this.report(error);
    }
  }

  close(): void {
    if (this.running()) {
      this.error.set('Stop the running plan before closing it.');
      return;
    }
    if (this.dirty() && !confirm('Discard unsaved changes?')) return;
    this.showing.set(false);
    this.draft.set(null);
  }

  dismissError(): void {
    this.error.set(null);
  }

  private async follow(runId: string): Promise<void> {
    this.stopPolling();
    const generation = ++this.generation;
    const poll = async (): Promise<void> => {
      try {
        const result = await this.bridge.send('run.progress', { runId });
        if (generation !== this.generation) return;
        this.run.set(result);
        if (result.state === 'running') {
          this.pollTimer = setTimeout(() => void poll(), POLL_MS);
        } else {
          await this.loadHistory();
        }
      } catch (error) {
        if (generation === this.generation) this.report(error);
      }
    };
    await poll();
  }

  private async loadHistory(): Promise<void> {
    const path = this.draft()?.path;
    if (!path) return;
    try {
      const history = await this.bridge.send('run.list', { path });
      if (this.draft()?.path === path) this.history.set(history);
    } catch (error) {
      this.report(error);
    }
  }

  private stopPolling(): void {
    this.generation++;
    if (this.pollTimer) clearTimeout(this.pollTimer);
    this.pollTimer = null;
  }

  private report(error: unknown): void {
    this.error.set(error instanceof Error ? error.message : String(error));
  }
}
