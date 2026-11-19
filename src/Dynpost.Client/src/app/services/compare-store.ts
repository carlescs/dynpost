import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { Bridge } from './bridge';
import { RunStore, isRunPlanDirty } from './run-store';
import { WorkspaceStore } from './workspace-store';
import { RequestContent } from '../models/workspace';
import { fromRequestDto, toRequestDto } from '../models/request-mapping';
import type { PlanForm } from '../models/run-plan-form';
import { touches } from '../models/disk-changes';

/** What can be compared: requests with requests, run plans with run plans. */
export type CompareKind = 'request' | 'plan';

/** A request or run plan picked for a comparison. */
export interface CompareItem {
  kind: CompareKind;
  /** Workspace-relative path of its file. */
  path: string;
  /** The name shown for it. */
  name: string;
}

/** One side of a comparison, read from its open tab when it has unsaved edits, else from disk. */
export interface CompareSide {
  path: string;
  name: string;
  /** The YAML of the file (as saving would write it, for unsaved edits). */
  text: string;
  /** For a request, its content. */
  request: RequestContent | null;
  /** For a run plan, its fields; `null` when the text can't be read as a plan. */
  plan: PlanForm | null;
  /** Whether the side shows edits that are not saved yet. */
  unsaved: boolean;
}

/** Two requests, or two run plans, compared in the main area. */
export interface Comparison {
  kind: CompareKind;
  left: CompareSide;
  right: CompareSide;
}

/**
 * Compares two requests or two run plans: the user selects one for comparing in its tree, then
 * compares another of the same kind with it. The comparison is shown in the main area, instead of
 * the editor tabs, until it is closed or a tab is opened.
 */
@Injectable({ providedIn: 'root' })
export class CompareStore {
  private readonly bridge = inject(Bridge);
  private readonly workspace = inject(WorkspaceStore);
  private readonly runs = inject(RunStore);

  /** The item selected for comparing, the left side of the next comparison. */
  readonly selected = signal<CompareItem | null>(null);
  /** The comparison shown in the main area, if any. */
  readonly comparison = signal<Comparison | null>(null);
  /** Whether the sides of a comparison are being read. */
  readonly loading = signal(false);
  /** The items being compared, the left one first; kept to read them again. */
  private items: [CompareItem, CompareItem] | null = null;
  /** Identifies the latest read; the answer of a stale or abandoned one is ignored. */
  private token = 0;

  /** Whether a comparison is shown in the main area, or being read to be shown there. */
  readonly open = computed(() => this.comparison() !== null || this.loading());

  constructor() {
    effect(() => {
      this.workspace.workspace();
      untracked(() => {
        this.selected.set(null);
        this.close();
      });
    });
    // Opening or selecting a tab (even the one shown) shows it instead.
    effect(() => {
      this.workspace.shown();
      untracked(() => this.close());
    });
  }

  /** Selects an item to compare others with. */
  select(item: CompareItem): void {
    this.selected.set(item);
  }

  /** Whether `item` can be compared with the selected item: same kind, another file. */
  canCompareWith(item: CompareItem): boolean {
    const selected = this.selected();
    return !!selected && selected.kind === item.kind && selected.path !== item.path;
  }

  /** Compares the selected item (left) with `item` (right). */
  async compareWithSelected(item: CompareItem): Promise<void> {
    const selected = this.selected();
    if (selected && this.canCompareWith(item)) await this.compare(selected, item);
  }

  /** Compares two items of the same kind, `left` as the original side. */
  async compare(left: CompareItem, right: CompareItem): Promise<void> {
    if (left.kind !== right.kind) return;
    this.items = [left, right];
    await this.load(true);
  }

  /** Swaps the sides of the comparison. */
  swap(): void {
    if (this.items) this.items = [this.items[1], this.items[0]];
    this.comparison.update((c) => c && { ...c, left: c.right, right: c.left });
  }

  /** Reads both sides again, when `paths` (anything when missing) touch either of them. */
  async reload(paths?: readonly string[]): Promise<void> {
    if (!this.items || !this.comparison() || this.loading()) return;
    if (!this.items.some((item) => touches(paths, item.path))) return;
    await this.load(false);
  }

  close(): void {
    this.token++;
    this.items = null;
    this.comparison.set(null);
    this.loading.set(false);
  }

  private async load(report: boolean): Promise<void> {
    const items = this.items;
    if (!items) return;
    const token = ++this.token;
    this.loading.set(true);
    try {
      const [left, right] = await Promise.all(items.map((item) => this.read(item)));
      if (token === this.token) this.comparison.set({ kind: items[0].kind, left, right });
    } catch (error) {
      if (token !== this.token) return;
      // A file that can't be read any more (deleted, renamed) ends the comparison.
      this.close();
      if (report) this.workspace.error.set(error instanceof Error ? error.message : String(error));
    } finally {
      if (token === this.token) this.loading.set(false);
    }
  }

  private async read(item: CompareItem): Promise<CompareSide> {
    return item.kind === 'request' ? this.readRequest(item) : this.readPlan(item);
  }

  private async readRequest(item: CompareItem): Promise<CompareSide> {
    const open = this.workspace.documents().find((d) => d.path === item.path && d.dirty);
    if (open) {
      const text = await this.bridge.send('request.viewFile', { request: toRequestDto(open) });
      return { path: item.path, name: open.name, text, request: open, plan: null, unsaved: true };
    }
    const dto = await this.bridge.send('request.load', { path: item.path });
    const text = await this.bridge.send('request.viewFile', { request: dto, path: item.path });
    const request = fromRequestDto(dto);
    return { path: item.path, name: request.name, text, request, plan: null, unsaved: false };
  }

  private async readPlan(item: CompareItem): Promise<CompareSide> {
    const open = this.runs.drafts().find((d) => d.path === item.path);
    const text = open
      ? open.yaml
      : (await this.bridge.send('runPlan.open', { path: item.path })).yaml;
    // The YAML parser is a lazy chunk, kept out of the initial bundle.
    const { readPlanForm } = await import('../models/run-plan-form');
    const plan = readPlanForm(text);
    return {
      path: item.path,
      name: plan?.name || item.name,
      text,
      request: null,
      plan,
      unsaved: !!open && isRunPlanDirty(open),
    };
  }
}
