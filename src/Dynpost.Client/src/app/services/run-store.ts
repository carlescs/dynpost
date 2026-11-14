import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { Bridge, BridgeError } from './bridge';
import { ERROR_CODES, RunPlanFileDto, RunResultDto, RunSummaryDto } from '../models/messages';
import { PLANS_FOLDER } from '../models/workspace';
import { WorkspaceStore } from './workspace-store';
import { DISCARD, Dialogs } from './dialogs';
import { joinPath } from './tree-state';
import { DateFormatStore } from './date-format-store';

/** A run plan open in an editor tab, with the run it shows. */
export interface RunPlanDraft {
  /** The draft's tab key (see `WorkspaceStore.tabs`); unlike `path`, never changes. */
  key: string;
  path: string;
  yaml: string;
  /** The text on disk, to tell whether there are unsaved changes. */
  savedYaml: string;
  /** Why the plan can't be run, or `null`. */
  error: string | null;
  /** The run shown in the dashboard: the one being followed, or an earlier one. */
  run: RunResultDto | null;
  /** The plan's earlier runs. */
  history: RunSummaryDto[];
  saving: boolean;
  starting: boolean;
  /** Set when a very large run needs the user's go-ahead. */
  confirmation: string | null;
  /** The last error of this plan's actions, shown in its tab, or `null`. */
  failure: string | null;
}

/** Whether a draft has edits that are not saved to its file. */
export const isRunPlanDirty = (draft: RunPlanDraft): boolean => draft.yaml !== draft.savedYaml;

/** Whether a draft's run is still going. */
export const isRunPlanRunning = (draft: RunPlanDraft): boolean => draft.run?.state === 'running';

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
 * Load run plans (`*.run.yaml` files) and the plans open in editor tabs, next to the request and
 * environment tabs. Each open plan follows its own run: a running plan is polled through
 * `run.progress` until it ends; the host keeps going on its own.
 */
@Injectable({ providedIn: 'root' })
export class RunStore {
  private readonly dates = inject(DateFormatStore);
  private readonly bridge = inject(Bridge);
  private readonly dialogs = inject(Dialogs);
  private readonly workspace = inject(WorkspaceStore);
  private readonly root = computed(() => this.workspace.workspace()?.root ?? null);

  readonly plans = signal<RunPlanFileDto[]>([]);
  /** The folders of the Load runs view (see `runFolder.list`), parents first. */
  readonly folders = signal<string[]>([]);
  /** The plans open in editor tabs. */
  readonly drafts = signal<RunPlanDraft[]>([]);
  /** The plan in the active tab, if a run plan tab is active. */
  readonly draft = computed(
    () => this.drafts().find((d) => d.key === this.workspace.activeTab()) ?? null,
  );

  /** Polling of each draft's run, by draft key; the generation tells stale polls apart. */
  private readonly polls = new Map<
    string,
    { generation: number; timer: ReturnType<typeof setTimeout> | null }
  >();
  private generation = 0;

  constructor() {
    effect(() => {
      const root = this.root();
      untracked(() => {
        for (const key of [...this.polls.keys()]) this.stopPolling(key);
        this.workspace.removeTabs(this.drafts().map((d) => d.key));
        this.drafts.set([]);
        this.plans.set([]);
        this.folders.set([]);
        if (root) void this.refresh();
      });
    });
    // Folders made, renamed or deleted in the explorer show here too.
    let first = true;
    effect(() => {
      this.workspace.tree();
      if (first) {
        first = false;
        return;
      }
      untracked(() => {
        if (this.root()) void this.refresh();
      });
    });
  }

  async refresh(): Promise<void> {
    const root = this.root();
    try {
      const [plans, folders] = await Promise.all([
        this.bridge.send('runPlan.list'),
        this.bridge.send('runFolder.list'),
      ]);
      if (this.root() !== root) return;
      this.plans.set(plans);
      this.folders.set(folders);
    } catch (error) {
      this.report(error);
    }
  }

  /** The plan's display name: its `name`, or its path when it has none. */
  nameOf(path: string): string {
    return this.plans().find((plan) => plan.path === path)?.name ?? path;
  }

  /** Keeps a plan on this computer only, or shares it through git again. */
  async toggleLocalOnly(plan: RunPlanFileDto): Promise<void> {
    if (await this.workspace.setLocalOnly(plan.path, plan.name ?? plan.path, !plan.localOnly))
      await this.refresh();
  }

  /** Opens a plan in a tab, or shows its tab if it is already open. */
  async open(path: string): Promise<void> {
    if (this.showOpen(path)) return;
    const root = this.root();
    try {
      const document = await this.bridge.send('runPlan.open', { path });
      // Another click may have opened it while loading, or another folder been opened.
      if (this.showOpen(path) || this.root() !== root) return;
      const key = this.workspace.newTabKey();
      this.drafts.update((drafts) => [
        ...drafts,
        {
          ...document,
          key,
          savedYaml: document.yaml,
          run: null,
          history: [],
          saving: false,
          starting: false,
          confirmation: null,
          failure: null,
        },
      ]);
      this.workspace.openTab(key);
      await this.loadHistory(key);
    } catch (error) {
      this.report(error);
    }
  }

  /**
   * Re-reads open plans changed outside the UI (by an AI assistant): those at `paths` without
   * unsaved edits, closing those whose file is gone, and the earlier runs of every open plan,
   * which runs deleted outside the UI change.
   */
  async reloadFromDisk(paths: string[]): Promise<void> {
    for (const draft of this.drafts()) {
      if (paths.includes(draft.path) && !isRunPlanDirty(draft)) {
        const { key, path } = draft;
        try {
          const document = await this.bridge.send('runPlan.open', { path });
          this.patch(key, (d) =>
            d.path === path && !isRunPlanDirty(d)
              ? { ...d, yaml: document.yaml, savedYaml: document.yaml, error: document.error }
              : d,
          );
        } catch (error) {
          if (error instanceof BridgeError && error.code === ERROR_CODES.notFound) {
            const current = this.find(key);
            if (current?.path === path && !isRunPlanRunning(current)) this.remove(key);
          } else {
            this.report(error, key);
          }
          continue;
        }
      }
      if (this.find(draft.key) && !isRunPlanRunning(this.find(draft.key)!)) {
        await this.loadHistory(draft.key);
      }
    }
  }

  /**
   * Shows a run started outside the UI (by an AI assistant): opens its plan's tab, unless open
   * already, and follows the run there, unless the tab is following another run.
   */
  async followStarted(path: string, runId: string): Promise<void> {
    if (!this.drafts().some((d) => d.path === path)) await this.open(path);
    const draft = this.drafts().find((d) => d.path === path);
    if (!draft || isRunPlanRunning(draft)) return;
    this.workspace.activateTab(draft.key);
    await this.follow(draft.key, runId);
  }

  /** Creates a plan file with a starting template in `folder` (by default the top of `plans`) and opens it. */
  async create(folder = PLANS_FOLDER): Promise<void> {
    const taken = new Set(this.plans().map((plan) => plan.path.toLowerCase()));
    let index = 1;
    while (taken.has(joinPath(folder, `plan-${index}.run.yaml`).toLowerCase())) index++;
    const path = joinPath(folder, `plan-${index}.run.yaml`);
    try {
      await this.bridge.send('runPlan.save', { path, yaml: NEW_PLAN(`Plan ${index}`) });
      await this.refresh();
      await this.open(path);
    } catch (error) {
      this.report(error);
    }
  }

  /** Deletes a plan file and its saved runs after asking; closes its tab if it is open. */
  async delete(path: string): Promise<void> {
    const open = this.drafts().find((d) => d.path === path);
    if (open && isRunPlanRunning(open)) {
      this.report('Stop the running plan before deleting it.', open.key);
      return;
    }
    const ok = await this.dialogs.confirm(
      `Delete the run plan '${this.nameOf(path)}' (${path}) and its saved runs?`,
      { title: 'Delete run plan?', confirmLabel: 'Delete', danger: true },
    );
    if (!ok) return;
    try {
      await this.bridge.send('runPlan.delete', { path });
      const draft = this.drafts().find((d) => d.path === path);
      if (draft) this.remove(draft.key);
    } catch (error) {
      this.report(error, open?.key);
    }
    await this.refresh();
  }

  /** Moves a plan file into `folder` (`plans` for the top level); its open tab follows. */
  async movePlan(path: string, folder: string): Promise<boolean> {
    const open = this.drafts().find((d) => d.path === path);
    if (open && isRunPlanRunning(open)) {
      this.report('Stop the running plan before moving it.', open.key);
      return false;
    }
    return this.change(async () => {
      const moved = await this.bridge.send('runPlan.move', { path, folder });
      this.relocate(path, moved.path);
    });
  }

  /** Creates a folder inside `parent` (`plans` for the top level); resolves to whether it worked. */
  createFolder(parent: string, name: string): Promise<boolean> {
    return this.change(() => this.bridge.send('folder.create', { parent, name }));
  }

  /** Renames a folder of plans; open plans inside it follow. */
  renameFolder(path: string, name: string): Promise<boolean> {
    if (this.refuseRunningIn(path, 'renaming')) return Promise.resolve(false);
    return this.change(async () => {
      const renamed = await this.bridge.send('runFolder.rename', { path, name });
      this.relocate(path, renamed.path);
    });
  }

  /** Moves a folder of plans into `parent` (`plans` for the top level); open plans inside it follow. */
  moveFolder(path: string, parent: string): Promise<boolean> {
    if (this.refuseRunningIn(path, 'moving')) return Promise.resolve(false);
    return this.change(async () => {
      const moved = await this.bridge.send('runFolder.move', { path, parent });
      this.relocate(path, moved.path);
    });
  }

  /** Copies a folder of plans next to it as "… copy". */
  duplicateFolder(path: string): Promise<boolean> {
    return this.change(() => this.bridge.send('runFolder.duplicate', { path }));
  }

  /**
   * Deletes a folder with its plans and their saved runs, after asking; the tabs of its plans
   * close. A folder with a running plan can't be deleted.
   */
  async deleteFolder(path: string): Promise<boolean> {
    if (this.refuseRunningIn(path, 'deleting')) return false;
    const plans = this.plans().filter((plan) => isWithin(plan.path, path)).length;
    const open = this.drafts().filter((d) => isWithin(d.path, path));
    const dirty = open.filter(isRunPlanDirty).length;
    const ok = await this.dialogs.confirm(
      `Delete the folder "${path}"` +
        (plans ? `, the ${plans} run plan(s) in it and their saved runs?` : '?') +
        (dirty ? ` ${dirty} open plan(s) with unsaved changes will be closed.` : ''),
      { title: 'Delete folder?', confirmLabel: 'Delete', danger: true },
    );
    if (!ok) return false;
    return this.change(async () => {
      await this.bridge.send('runFolder.delete', { path });
      for (const draft of open) this.remove(draft.key);
    });
  }

  edit(key: string, yaml: string): void {
    this.patch(key, (draft) => ({ ...draft, yaml }));
  }

  /** Writes the text to the file; a plan that can't be run yet is saved too. */
  async save(key: string): Promise<boolean> {
    const draft = this.find(key);
    if (!draft || draft.saving) return false;
    this.patch(key, (d) => ({ ...d, saving: true }));
    try {
      const saved = await this.bridge.send('runPlan.save', { path: draft.path, yaml: draft.yaml });
      // Edits typed while saving stay as they are, and stay unsaved. Renaming the plan may have
      // renamed its file, so the draft follows the saved path.
      this.patch(key, (current) => ({
        ...current,
        path: saved.path,
        yaml: current.yaml === draft.yaml ? saved.yaml : current.yaml,
        savedYaml: saved.yaml,
        error: saved.error,
        saving: false,
        failure: null,
      }));
      await this.refresh();
      return true;
    } catch (error) {
      this.patch(key, (d) => ({ ...d, saving: false }));
      this.report(error, key);
      return false;
    }
  }

  /** Saves pending edits, then starts the plan; `confirmLarge` goes ahead with a very large run. */
  async start(key: string, confirmLarge = false): Promise<void> {
    const draft = this.find(key);
    if (!draft || draft.starting || isRunPlanRunning(draft)) return;
    this.patch(key, (d) => ({ ...d, starting: true, confirmation: null }));
    try {
      if (isRunPlanDirty(draft) && !(await this.save(key))) return;
      const current = this.find(key);
      if (!current || current.error) return;
      const { runId } = await this.bridge.send('run.start', { path: current.path, confirmLarge });
      this.patch(key, (d) => ({ ...d, failure: null }));
      await this.follow(key, runId);
    } catch (error) {
      if (error instanceof BridgeError && error.code === ERROR_CODES.confirmationRequired) {
        this.patch(key, (d) => ({ ...d, confirmation: error.message }));
      } else {
        this.report(error, key);
      }
    } finally {
      this.patch(key, (d) => ({ ...d, starting: false }));
    }
  }

  async cancel(key: string): Promise<void> {
    const run = this.find(key)?.run;
    if (!run || run.state !== 'running') return;
    try {
      await this.bridge.send('run.cancel', { runId: run.runId });
    } catch (error) {
      this.report(error, key);
    }
  }

  /** Shows an earlier run of a plan. */
  async show(key: string, runId: string): Promise<void> {
    const draft = this.find(key);
    if (!draft) return;
    if (isRunPlanRunning(draft)) {
      this.report('Stop the running plan before viewing history.', key);
      return;
    }
    this.stopPolling(key);
    try {
      const run = await this.bridge.send('run.progress', { runId });
      this.patch(key, (d) => ({ ...d, run }));
    } catch (error) {
      this.report(error, key);
    }
  }

  /** Deletes earlier runs of a plan after asking; the dashboard empties if it showed one of them. */
  async deleteRuns(key: string, runIds: string[]): Promise<void> {
    const draft = this.find(key);
    if (!draft || !runIds.length) return;
    if (draft.run && runIds.includes(draft.run.runId) && isRunPlanRunning(draft)) {
      this.report('Stop the run before deleting it.', key);
      return;
    }
    const name = this.nameOf(draft.path);
    let message: string;
    if (runIds.length === 1) {
      const run = draft.history.find((summary) => summary.runId === runIds[0]);
      const when = run ? ` from ${this.dates.dateTime(run.startedAt)}` : '';
      message = `Delete the run${when} of '${name}' and its request log?`;
    } else {
      message = `Delete ${runIds.length} runs of '${name}' and their request logs?`;
    }
    const ok = await this.dialogs.confirm(message, {
      title: runIds.length === 1 ? 'Delete run?' : 'Delete runs?',
      confirmLabel: 'Delete',
      danger: true,
    });
    if (!ok) return;
    const deleted = new Set(runIds);
    try {
      await this.bridge.send('run.delete', { runIds });
      this.patch(key, (d) => ({
        ...d,
        run: d.run && deleted.has(d.run.runId) ? null : d.run,
        history: d.history.filter((summary) => !deleted.has(summary.runId)),
      }));
    } catch (error) {
      this.report(error, key);
    }
    await this.loadHistory(key);
  }

  /** Deletes all the earlier runs of a plan after asking; the plan itself stays. */
  async clearHistory(key: string): Promise<void> {
    const draft = this.find(key);
    if (!draft) return;
    if (isRunPlanRunning(draft)) {
      this.report('Stop the running plan before clearing its history.', key);
      return;
    }
    const ok = await this.dialogs.confirm(
      `Delete all ${draft.history.length} saved runs of '${this.nameOf(draft.path)}'? The plan itself is kept.`,
      { title: 'Clear run history?', confirmLabel: 'Delete all', danger: true },
    );
    if (!ok) return;
    try {
      await this.bridge.send('run.clear', { path: draft.path });
      this.patch(key, (d) => ({ ...d, run: null, history: [] }));
    } catch (error) {
      this.report(error, key);
    }
    await this.loadHistory(key);
  }

  /** Closes a plan's tab, after asking (unless `ask` is false) if it has unsaved changes; a running plan stays open. */
  async close(key: string, ask = true): Promise<void> {
    const draft = this.find(key);
    if (!draft) return;
    if (isRunPlanRunning(draft)) {
      this.workspace.activateTab(key);
      this.report('Stop the running plan before closing it.', key);
      return;
    }
    if (
      ask &&
      isRunPlanDirty(draft) &&
      !(await this.dialogs.confirm(
        `Discard unsaved changes to the run plan "${this.nameOf(draft.path)}"?`,
        DISCARD,
      ))
    ) {
      return;
    }
    this.remove(key);
  }

  dismissError(key: string): void {
    this.patch(key, (d) => ({ ...d, failure: null }));
  }

  /**
   * Runs a change to the files of the view, then reloads the plans and folders (and the explorer,
   * which shows the same folders). Resolves to whether the change worked; errors go to the error bar.
   */
  private async change(action: () => Promise<unknown>): Promise<boolean> {
    try {
      await action();
      this.workspace.error.set(null);
      return true;
    } catch (error) {
      this.report(error);
      return false;
    } finally {
      await this.refresh();
      await this.workspace.refresh();
    }
  }

  /** Points the open plans at or inside `from` to their paths under `to`. */
  private relocate(from: string, to: string): void {
    if (from === to) return;
    this.drafts.update((drafts) =>
      drafts.map((d) =>
        isWithin(d.path, from) ? { ...d, path: to + d.path.slice(from.length) } : d,
      ),
    );
  }

  /** Reports and returns `true` when a plan in `folder` is running. */
  private refuseRunningIn(folder: string, action: string): boolean {
    const running = this.drafts().find((d) => isWithin(d.path, folder) && isRunPlanRunning(d));
    if (running) this.report(`Stop the running plan before ${action} its folder.`, running.key);
    return !!running;
  }

  /** Shows the tab of the plan at `path` if it is open; returns whether it was. */
  private showOpen(path: string): boolean {
    const open = this.drafts().find((d) => d.path === path);
    if (open) this.workspace.activateTab(open.key);
    return !!open;
  }

  private remove(key: string): void {
    this.stopPolling(key);
    this.drafts.update((drafts) => drafts.filter((d) => d.key !== key));
    this.workspace.removeTabs([key]);
  }

  private find(key: string): RunPlanDraft | undefined {
    return this.drafts().find((d) => d.key === key);
  }

  private patch(key: string, change: (draft: RunPlanDraft) => RunPlanDraft): void {
    this.drafts.update((drafts) => drafts.map((d) => (d.key === key ? change(d) : d)));
  }

  private async follow(key: string, runId: string): Promise<void> {
    this.stopPolling(key);
    const generation = ++this.generation;
    this.polls.set(key, { generation, timer: null });
    const current = () => this.polls.get(key)?.generation === generation;
    const poll = async (): Promise<void> => {
      try {
        const result = await this.bridge.send('run.progress', { runId });
        if (!current()) return;
        this.patch(key, (d) => ({ ...d, run: result }));
        if (result.state === 'running') {
          this.polls.set(key, { generation, timer: setTimeout(() => void poll(), POLL_MS) });
        } else {
          this.polls.delete(key);
          await this.loadHistory(key);
        }
      } catch (error) {
        if (current()) this.report(error, key);
      }
    };
    await poll();
  }

  private async loadHistory(key: string): Promise<void> {
    const path = this.find(key)?.path;
    if (!path) return;
    try {
      const history = await this.bridge.send('run.list', { path });
      this.patch(key, (d) => (d.path === path ? { ...d, history } : d));
    } catch (error) {
      this.report(error, key);
    }
  }

  private stopPolling(key: string): void {
    const poll = this.polls.get(key);
    if (poll?.timer) clearTimeout(poll.timer);
    this.polls.delete(key);
  }

  /** Shows an error in the plan's tab, or in the app's error bar when it isn't about one plan. */
  private report(error: unknown, key?: string): void {
    const message = error instanceof Error ? error.message : String(error);
    if (key && this.find(key)) this.patch(key, (d) => ({ ...d, failure: message }));
    else this.workspace.error.set(message);
  }
}

/** Whether `path` is `folder` itself or inside it. */
function isWithin(path: string, folder: string): boolean {
  return path === folder || path.startsWith(`${folder}/`);
}
