import { Component, computed, inject, input, output } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faArrowDown, faArrowUp, faPlus, faTrash } from '@fortawesome/free-solid-svg-icons';
import {
  ASSERT_ALL,
  PlanForm,
  THRESHOLD_KEYS,
  addAssertion,
  readPlanForm,
  removeAssertion,
  setAssertionStatus,
  setPlanRequests,
  setPlanThinkTime,
  setPlanValue,
} from '../../../../models/run-plan-form';
import { WorkspaceNode } from '../../../../models/workspace';
import { WorkspaceStore } from '../../../../services/workspace-store';

type LoadMode = 'iterations' | 'duration';

const THRESHOLD_LABELS: Record<(typeof THRESHOLD_KEYS)[number], string> = {
  p50: 'p50 (max)',
  p90: 'p90 (max)',
  p95: 'p95 (max)',
  p99: 'p99 (max)',
  average: 'Average (max)',
  errorRate: 'Error rate (max)',
  throughput: 'Throughput (min, req/s)',
};

/** Edits a run plan with form fields; every change is written back into the plan's YAML text. */
@Component({
  selector: 'app-run-plan-form',
  imports: [FaIconComponent],
  host: { class: 'block' },
  templateUrl: './run-plan-form.html',
})
export class RunPlanForm {
  private readonly workspace = inject(WorkspaceStore);

  readonly yaml = input.required<string>();
  readonly yamlChange = output<string>();

  protected readonly form = computed<PlanForm | null>(() => readPlanForm(this.yaml()));
  protected readonly thresholdKeys = THRESHOLD_KEYS;
  protected readonly thresholdLabels = THRESHOLD_LABELS;
  protected readonly allRequests = ASSERT_ALL;
  protected readonly faPlus = faPlus;
  protected readonly faTrash = faTrash;
  protected readonly faArrowUp = faArrowUp;
  protected readonly faArrowDown = faArrowDown;

  protected readonly field =
    'w-full rounded border border-slate-300 bg-white px-2 py-1 text-sm focus:border-sky-500 focus:outline-none';
  protected readonly label = 'flex flex-col gap-1 text-xs text-slate-600';
  protected readonly iconButton =
    'flex size-6 shrink-0 items-center justify-center rounded text-slate-500 hover:bg-slate-200 disabled:opacity-30';

  /** Every request in the workspace, as paths. */
  private readonly workspaceRequests = computed(() => {
    const paths: string[] = [];
    const walk = (nodes: WorkspaceNode[]) => {
      for (const node of nodes) {
        if (node.kind === 'request') paths.push(node.path);
        else if (node.children) walk(node.children);
      }
    };
    walk(this.workspace.tree());
    return paths;
  });

  /** Requests that can still be added to the plan. */
  protected readonly availableRequests = computed(() => {
    const used = new Set(this.form()?.requests ?? []);
    return this.workspaceRequests().filter((path) => !used.has(path));
  });

  /** Assertion targets that don't have a block yet. */
  protected readonly availableTargets = computed(() => {
    const form = this.form();
    if (!form) return [];
    const used = new Set(form.assertions.map((a) => a.key));
    return [ASSERT_ALL, ...form.requests].filter((key) => !used.has(key));
  });

  protected loadMode(form: PlanForm): LoadMode {
    return form.duration !== '' && form.iterations === '' ? 'duration' : 'iterations';
  }

  protected set(path: (string | number)[], event: Event): void {
    this.emit(setPlanValue(this.yaml(), path, (event.target as HTMLInputElement).value));
  }

  protected setLoadMode(form: PlanForm, mode: LoadMode): void {
    const other = mode === 'duration' ? 'iterations' : 'duration';
    const yaml = setPlanValue(this.yaml(), ['load', other], '');
    this.emit(
      mode === 'duration'
        ? setPlanValue(yaml, ['load', 'duration'], form.duration || '1m')
        : setPlanValue(yaml, ['load', 'iterations'], form.iterations || '10'),
    );
  }

  protected setThink(form: PlanForm, part: 'min' | 'max', event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.emit(
      part === 'min'
        ? setPlanThinkTime(this.yaml(), value, form.thinkMax)
        : setPlanThinkTime(this.yaml(), form.thinkMin, value),
    );
  }

  protected setAssertionStatus(key: string, event: Event): void {
    this.emit(setAssertionStatus(this.yaml(), key, (event.target as HTMLInputElement).value));
  }

  protected addRequest(select: HTMLSelectElement, form: PlanForm): void {
    if (!select.value) return;
    this.emit(setPlanRequests(this.yaml(), [...form.requests, select.value]));
    select.value = '';
  }

  protected removeRequest(form: PlanForm, index: number): void {
    this.emit(
      setPlanRequests(
        this.yaml(),
        form.requests.filter((_, i) => i !== index),
      ),
    );
  }

  protected moveRequest(form: PlanForm, index: number, by: -1 | 1): void {
    const requests = [...form.requests];
    [requests[index], requests[index + by]] = [requests[index + by], requests[index]];
    this.emit(setPlanRequests(this.yaml(), requests));
  }

  protected addAssertion(select: HTMLSelectElement): void {
    if (!select.value) return;
    this.emit(addAssertion(this.yaml(), select.value));
    select.value = '';
  }

  protected removeAssertion(key: string): void {
    this.emit(removeAssertion(this.yaml(), key));
  }

  private emit(yaml: string): void {
    if (yaml !== this.yaml()) this.yamlChange.emit(yaml);
  }
}
