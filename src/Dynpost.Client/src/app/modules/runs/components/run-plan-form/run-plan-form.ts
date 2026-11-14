import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList } from '@angular/cdk/drag-drop';
import { Component, computed, inject, input, output } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faArrowDown,
  faArrowUp,
  faClock,
  faFileLines,
  faFlagCheckered,
  faFloppyDisk,
  faGaugeHigh,
  faGripVertical,
  faListCheck,
  faListOl,
  faRepeat,
  faLayerGroup,
  faTable,
  faTrash,
  faTriangleExclamation,
} from '@fortawesome/free-solid-svg-icons';
import {
  ASSERT_ALL,
  PlanForm,
  REQUEST_ORDERS,
  RequestOrder,
  THRESHOLD_KEYS,
  addAssertion,
  addBody,
  picksOneRequest,
  readPlanForm,
  removeAssertion,
  removeBody,
  setAssertionStatus,
  setPlanEnvironmentOrder,
  setPlanEnvironments,
  setPlanOrder,
  setPlanRequestEnabled,
  setPlanRequests,
  setPlanSaveResponses,
  setPlanThinkTime,
  setPlanValue,
} from '../../../../models/run-plan-form';
import { environmentPath } from '../../../../models/messages';
import { METHOD_COLORS, WorkspaceNode } from '../../../../models/workspace';
import { EnvironmentStore } from '../../../../services/environment-store';
import { WorkspaceStore } from '../../../../services/workspace-store';
import { parseDuration } from '../../models/run-format';
import { LoadProfile } from '../load-profile/load-profile';

type LoadMode = 'iterations' | 'duration';

/** Each threshold's name, which side of the limit passes and an example value. */
const THRESHOLDS: Record<
  (typeof THRESHOLD_KEYS)[number],
  { label: string; bound: string; example: string }
> = {
  p50: { label: 'p50', bound: 'at most', example: '500ms' },
  p90: { label: 'p90', bound: 'at most', example: '500ms' },
  p95: { label: 'p95', bound: 'at most', example: '500ms' },
  p99: { label: 'p99', bound: 'at most', example: '500ms' },
  average: { label: 'Average', bound: 'at most', example: '500ms' },
  errorRate: { label: 'Error rate', bound: 'at most', example: '1%' },
  throughput: { label: 'Req/s', bound: 'at least', example: '50' },
};

/** How each request order is offered in the form. */
const ORDERS: Record<RequestOrder, { label: string; hint: string }> = {
  sequential: {
    label: 'In order',
    hint: 'Each iteration, every user sends them all in this order',
  },
  shuffle: {
    label: 'Shuffled',
    hint: 'Each iteration, every user sends them all in a new random order',
  },
  random: {
    label: 'Random',
    hint: 'Each iteration, every user sends one request, picked at random by weight',
  },
  roundRobin: {
    label: 'Round robin',
    hint: 'Each iteration, every user sends the next request; users start at different ones',
  },
};

/** How each environment order is offered in the form. */
const ENVIRONMENT_ORDERS: Record<RequestOrder, { label: string; hint: string }> = {
  sequential: {
    label: 'In order',
    hint: 'Each iteration, every user sends the requests with each environment in this order',
  },
  shuffle: {
    label: 'Shuffled',
    hint: 'Each iteration, every user sends the requests with each environment, in a new random order',
  },
  random: {
    label: 'Random',
    hint: 'Each iteration, every user sends the requests with one environment, picked at random by weight',
  },
  roundRobin: {
    label: 'Round robin',
    hint: 'Each iteration, every user sends the requests with the next environment; users start at different ones',
  },
};

/** Each item's share of the picks with the `random` order, as whole percentages. */
function sharesOf(weights: (string | undefined)[]): number[] {
  const values = weights.map((weight) => {
    const value = weight === undefined || weight === '' ? 1 : Number(weight);
    return Number.isFinite(value) && value > 0 ? value : 0;
  });
  const total = values.reduce((sum, value) => sum + value, 0);
  return values.map((value) => (total > 0 ? Math.round((value / total) * 100) : 0));
}

/** The most users the slider goes up to; the number field next to it takes more. */
const SLIDER_MAX = 200;

/** Edits a run plan with form fields; every change is written back into the plan's YAML text. */
@Component({
  selector: 'app-run-plan-form',
  imports: [CdkDrag, CdkDragHandle, CdkDropList, FaIconComponent, LoadProfile],
  host: { class: 'block' },
  templateUrl: './run-plan-form.html',
})
export class RunPlanForm {
  private readonly workspace = inject(WorkspaceStore);
  private readonly environmentStore = inject(EnvironmentStore);

  readonly yaml = input.required<string>();
  readonly yamlChange = output<string>();

  protected readonly form = computed<PlanForm | null>(() => readPlanForm(this.yaml()));
  protected readonly thresholdKeys = THRESHOLD_KEYS;
  protected readonly thresholds = THRESHOLDS;
  protected readonly orders = REQUEST_ORDERS;
  protected readonly orderInfo = ORDERS;
  protected readonly environmentOrderInfo = ENVIRONMENT_ORDERS;
  protected readonly allRequests = ASSERT_ALL;
  protected readonly sliderMax = SLIDER_MAX;
  protected readonly methodColors = METHOD_COLORS;
  protected readonly faTrash = faTrash;
  protected readonly faArrowUp = faArrowUp;
  protected readonly faArrowDown = faArrowDown;
  protected readonly faGripVertical = faGripVertical;
  protected readonly faTriangleExclamation = faTriangleExclamation;
  protected readonly faListOl = faListOl;
  protected readonly faLayerGroup = faLayerGroup;
  protected readonly faGaugeHigh = faGaugeHigh;
  protected readonly faTable = faTable;
  protected readonly faListCheck = faListCheck;
  protected readonly faFlagCheckered = faFlagCheckered;
  protected readonly faRepeat = faRepeat;
  protected readonly faClock = faClock;
  protected readonly faFileLines = faFileLines;
  protected readonly faFloppyDisk = faFloppyDisk;

  protected readonly field =
    'w-full rounded border border-slate-300 bg-white px-2 py-1 text-sm focus:border-sky-500 focus:outline-none';
  protected readonly label = 'flex flex-col gap-1 text-xs text-slate-600';
  protected readonly iconButton =
    'flex size-6 shrink-0 items-center justify-center rounded text-slate-500 hover:bg-slate-200 disabled:opacity-30';
  protected readonly card = 'flex flex-col gap-3 rounded-lg border border-slate-200 bg-white p-3';
  protected readonly cardIcon =
    'flex size-6 shrink-0 items-center justify-center rounded-md bg-sky-50 text-xs text-sky-700';
  protected readonly chip = 'rounded-md bg-slate-100 px-2 py-0.5 font-semibold text-slate-800';

  /** Every request in the workspace, by path. */
  private readonly workspaceRequests = computed(() => {
    const requests = new Map<string, WorkspaceNode>();
    const walk = (nodes: WorkspaceNode[]) => {
      for (const node of nodes) {
        if (node.kind === 'request') requests.set(node.path, node);
        else if (node.children) walk(node.children);
      }
    };
    walk(this.workspace.tree());
    return requests;
  });

  /** The plan's requests in sending order, with what the workspace knows about each. */
  protected readonly steps = computed(() => {
    const known = this.workspaceRequests();
    const disabled = new Set(this.form()?.disabled ?? []);
    return (this.form()?.requests ?? []).map((path) => {
      const node = known.get(path);
      return {
        path,
        name: node?.name ?? path,
        method: node?.method,
        missing: !node,
        enabled: !disabled.has(path),
      };
    });
  });

  /** How many of the plan's requests are sent. */
  protected readonly enabledCount = computed(() => {
    const form = this.form();
    if (!form) return 0;
    const disabled = new Set(form.disabled);
    return form.requests.filter((path) => !disabled.has(path)).length;
  });

  /** The workspace's environments, by workspace path. */
  private readonly workspaceEnvironments = computed(
    () =>
      new Map(
        this.environmentStore.environments().map((env) => [environmentPath(env.id), env.name]),
      ),
  );

  /** The environment selected in the app, which runs use when the plan lists none. */
  protected readonly selectedEnvironment = computed(
    () => this.environmentStore.active()?.name ?? null,
  );

  /** The plan's environments in order, with their names, flagging those not in the workspace. */
  protected readonly environmentSteps = computed(() => {
    const known = this.workspaceEnvironments();
    return (this.form()?.environments ?? []).map((path) => ({
      path,
      name: known.get(path) ?? path,
      missing: !known.has(path),
    }));
  });

  /** Environments that can still be added to the plan. */
  protected readonly availableEnvironments = computed(() => {
    const used = new Set(this.form()?.environments ?? []);
    return [...this.workspaceEnvironments()]
      .filter(([path]) => !used.has(path))
      .map(([path, name]) => ({ path, name }));
  });

  /** Each environment's share of the picks with the `random` environment order. */
  protected readonly environmentShares = computed(() => {
    const form = this.form();
    return form ? sharesOf(form.environments.map((path) => form.environmentWeights[path])) : [];
  });

  /** Requests that can still be added to the plan. */
  protected readonly availableRequests = computed(() => {
    const used = new Set(this.form()?.requests ?? []);
    return [...this.workspaceRequests().keys()].filter((path) => !used.has(path));
  });

  /** The numbers behind the load summary and its sketch. */
  protected readonly load = computed(() => {
    const form = this.form();
    if (!form) return null;
    const threads = Number(form.threads) || 1;
    const iterations = Number(form.iterations) || 1;
    const timed = this.loadMode(form) === 'duration';
    const requests = picksOneRequest(form.order)
      ? Math.min(1, this.enabledCount())
      : this.enabledCount();
    const environments =
      form.environments.length === 0 || picksOneRequest(this.environmentOrder(form))
        ? 1
        : form.environments.length;
    return {
      threads,
      iterations,
      timed,
      oneEach: picksOneRequest(form.order),
      requests,
      environments,
      total: threads * iterations * requests * environments,
      rampUpMs: parseDuration(form.rampUp) ?? 0,
      durationMs: timed ? parseDuration(form.duration) : null,
    };
  });

  /** Assertion targets that don't have a block yet. */
  protected readonly availableTargets = computed(() => {
    const form = this.form();
    if (!form) return [];
    const used = new Set(form.assertions.map((a) => a.key));
    return [ASSERT_ALL, ...form.requests].filter((key) => !used.has(key));
  });

  /** Requests that don't take their body from a folder yet. */
  protected readonly availableBodyTargets = computed(() => {
    const form = this.form();
    if (!form) return [];
    const used = new Set(form.bodies.map((body) => body.key));
    return [...new Set(form.requests)].filter((key) => !used.has(key));
  });

  /** The plan's order, `sequential` when it isn't set or isn't one the form knows. */
  protected order(form: PlanForm): RequestOrder {
    return (REQUEST_ORDERS as readonly string[]).includes(form.order)
      ? (form.order as RequestOrder)
      : 'sequential';
  }

  /**
   * Each request's share of the picks with the `random` order, as a whole percentage; disabled
   * requests are never picked.
   */
  protected readonly shares = computed(() => {
    const form = this.form();
    if (!form) return [];
    return sharesOf(
      form.requests.map((path) => (form.disabled.includes(path) ? '0' : form.weights[path])),
    );
  });

  /** The plan's environment order, `sequential` when it isn't set or isn't one the form knows. */
  protected environmentOrder(form: PlanForm): RequestOrder {
    return (REQUEST_ORDERS as readonly string[]).includes(form.environmentOrder)
      ? (form.environmentOrder as RequestOrder)
      : 'sequential';
  }

  protected setEnvironmentOrder(event: Event): void {
    this.emit(setPlanEnvironmentOrder(this.yaml(), (event.target as HTMLSelectElement).value));
  }

  protected setEnvironmentWeight(path: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value.trim();
    this.emit(setPlanValue(this.yaml(), ['environmentWeights', path], value === '1' ? '' : value));
  }

  protected addEnvironment(select: HTMLSelectElement, form: PlanForm): void {
    if (!select.value) return;
    this.emit(setPlanEnvironments(this.yaml(), [...form.environments, select.value]));
    select.value = '';
  }

  protected removeEnvironment(form: PlanForm, index: number): void {
    this.emit(
      setPlanEnvironments(
        this.yaml(),
        form.environments.filter((_, i) => i !== index),
      ),
    );
  }

  protected moveEnvironment(form: PlanForm, index: number, by: -1 | 1): void {
    const environments = [...form.environments];
    [environments[index], environments[index + by]] = [
      environments[index + by],
      environments[index],
    ];
    this.emit(setPlanEnvironments(this.yaml(), environments));
  }

  protected dropEnvironment(form: PlanForm, event: CdkDragDrop<unknown>): void {
    const environments = [...form.environments];
    environments.splice(event.currentIndex, 0, ...environments.splice(event.previousIndex, 1));
    this.emit(setPlanEnvironments(this.yaml(), environments));
  }

  protected setOrder(event: Event): void {
    this.emit(setPlanOrder(this.yaml(), (event.target as HTMLSelectElement).value));
  }

  protected setSaveResponses(event: Event): void {
    this.emit(setPlanSaveResponses(this.yaml(), (event.target as HTMLInputElement).checked));
  }

  protected setWeight(path: string, event: Event): void {
    const value = (event.target as HTMLInputElement).value.trim();
    this.emit(setPlanValue(this.yaml(), ['weights', path], value === '1' ? '' : value));
  }

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

  protected setEnabled(path: string, event: Event): void {
    this.emit(setPlanRequestEnabled(this.yaml(), path, (event.target as HTMLInputElement).checked));
  }

  protected moveRequest(form: PlanForm, index: number, by: -1 | 1): void {
    const requests = [...form.requests];
    [requests[index], requests[index + by]] = [requests[index + by], requests[index]];
    this.emit(setPlanRequests(this.yaml(), requests));
  }

  protected dropRequest(form: PlanForm, event: CdkDragDrop<unknown>): void {
    const requests = [...form.requests];
    requests.splice(event.currentIndex, 0, ...requests.splice(event.previousIndex, 1));
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

  protected addBody(select: HTMLSelectElement): void {
    if (!select.value) return;
    this.emit(addBody(this.yaml(), select.value));
    select.value = '';
  }

  protected removeBody(key: string): void {
    this.emit(removeBody(this.yaml(), key));
  }

  private emit(yaml: string): void {
    if (yaml !== this.yaml()) this.yamlChange.emit(yaml);
  }
}
