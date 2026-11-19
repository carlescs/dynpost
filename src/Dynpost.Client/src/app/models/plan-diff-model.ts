import { KeyValue } from './workspace';
import { RowChange, diffRows } from './request-diff-model';
import { ASSERT_ALL, PlanAssertion, PlanBody, PlanForm, ThresholdKey } from './run-plan-form';

/** One section of a run plan, compared row by row. */
export interface PlanSectionChanges {
  id: PlanSectionId;
  title: string;
  /** The heading of the key column. */
  label: string;
  /** The heading of the value column. */
  valueLabel: string;
  /** Whether the section's rows can be turned off (requests and environments). */
  toggles: boolean;
  rows: RowChange[];
  /** How many rows were added, removed or changed. */
  changes: number;
}

export type PlanSectionId =
  'requests' | 'environments' | 'load' | 'data' | 'bodies' | 'assertions' | 'thresholds';

/** A run plan with nothing in it: the side of a comparison that can't be read as a plan. */
export const EMPTY_PLAN: PlanForm = {
  name: '',
  requests: [],
  disabled: [],
  order: '',
  weights: {},
  environments: [],
  disabledEnvironments: [],
  environmentOrder: '',
  environmentWeights: {},
  threads: '',
  iterations: '',
  duration: '',
  rampUp: '',
  thinkMin: '',
  thinkMax: '',
  maxRps: '',
  dataFile: '',
  dataMode: '',
  dataOnEof: '',
  bodies: [],
  saveResponses: false,
  assertions: [],
  thresholds: {
    p50: '',
    p90: '',
    p95: '',
    p99: '',
    average: '',
    errorRate: '',
    throughput: '',
  },
};

const ORDER_LABELS: Record<string, string> = {
  sequential: 'In order',
  shuffle: 'Shuffled',
  random: 'Random',
  roundRobin: 'Round robin',
};

const THRESHOLD_LABELS: Record<ThresholdKey, string> = {
  p50: 'p50 at most',
  p90: 'p90 at most',
  p95: 'p95 at most',
  p99: 'p99 at most',
  average: 'Average at most',
  errorRate: 'Error rate at most',
  throughput: 'Req/s at least',
};

const row = (key: string, value: string, enabled = true): KeyValue => ({ key, value, enabled });

/** Rows for the values that are set, in the given order. */
const settings = (entries: [string, string][]): KeyValue[] =>
  entries.filter(([, value]) => value !== '').map(([key, value]) => row(key, value));

/** The plan's requests, with their weights and whether they are sent. */
export function planRequestRows(plan: PlanForm): KeyValue[] {
  return plan.requests.map((path) =>
    row(path, plan.weights[path] ?? '', !plan.disabled.includes(path)),
  );
}

/** The environments the plan's users take, with their weights and whether they are used. */
export function planEnvironmentRows(plan: PlanForm): KeyValue[] {
  return plan.environments.map((path) =>
    row(path, plan.environmentWeights[path] ?? '', !plan.disabledEnvironments.includes(path)),
  );
}

/** How the plan loads the server: users, length, pacing and orders. */
export function planLoadRows(plan: PlanForm): KeyValue[] {
  const think =
    plan.thinkMin && plan.thinkMax ? `${plan.thinkMin} – ${plan.thinkMax}` : plan.thinkMin;
  return settings([
    ['Virtual users (threads)', plan.threads],
    ['Iterations', plan.iterations],
    ['Duration', plan.duration],
    ['Ramp-up', plan.rampUp],
    ['Think time', think],
    ['Most requests per second', plan.maxRps],
    ['Request order', ORDER_LABELS[plan.order] ?? plan.order],
    ['Environment order', ORDER_LABELS[plan.environmentOrder] ?? plan.environmentOrder],
  ]);
}

/** The plan's data file and what it keeps of the responses. */
export function planDataRows(plan: PlanForm): KeyValue[] {
  return settings([
    ['Data file', plan.dataFile],
    ['Data mode', plan.dataMode],
    ['At the end of the data', plan.dataOnEof],
    ['Save responses', plan.saveResponses ? 'yes' : ''],
  ]);
}

const describeBody = (body: PlanBody): string =>
  [
    body.folder,
    body.pattern && `files ${body.pattern}`,
    body.mode && `mode ${body.mode}`,
    body.onEof && `at the end ${body.onEof}`,
  ]
    .filter(Boolean)
    .join(', ');

/** The requests whose body is taken from the files of a folder, and how. */
export function planBodyRows(plan: PlanForm): KeyValue[] {
  return plan.bodies.map((body) => row(body.key, describeBody(body)));
}

const describeAssertion = (a: PlanAssertion): string =>
  [
    a.status && `status ${a.status}`,
    a.maxTime && `time at most ${a.maxTime}`,
    a.bodyContains && `body contains "${a.bodyContains}"`,
    a.bodyNotContains && `body doesn't contain "${a.bodyNotContains}"`,
  ]
    .filter(Boolean)
    .join(', ');

/** What the plan checks in each response, by request (`*` for every request). */
export function planAssertionRows(plan: PlanForm): KeyValue[] {
  return plan.assertions.map((a) =>
    row(a.key === ASSERT_ALL ? 'Every request' : a.key, describeAssertion(a)),
  );
}

/** The limits a run must keep to pass. */
export function planThresholdRows(plan: PlanForm): KeyValue[] {
  return settings(
    (Object.keys(THRESHOLD_LABELS) as ThresholdKey[]).map((key) => [
      THRESHOLD_LABELS[key],
      plan.thresholds[key] ?? '',
    ]),
  );
}

const SECTIONS: {
  id: PlanSectionId;
  title: string;
  label: string;
  valueLabel: string;
  toggles: boolean;
  rows: (plan: PlanForm) => KeyValue[];
}[] = [
  {
    id: 'requests',
    title: 'Requests',
    label: 'Request',
    valueLabel: 'Weight',
    toggles: true,
    rows: planRequestRows,
  },
  {
    id: 'environments',
    title: 'Environments',
    label: 'Environment',
    valueLabel: 'Weight',
    toggles: true,
    rows: planEnvironmentRows,
  },
  {
    id: 'load',
    title: 'Load',
    label: 'Setting',
    valueLabel: 'Value',
    toggles: false,
    rows: planLoadRows,
  },
  {
    id: 'data',
    title: 'Data',
    label: 'Setting',
    valueLabel: 'Value',
    toggles: false,
    rows: planDataRows,
  },
  {
    id: 'bodies',
    title: 'Bodies',
    label: 'Request',
    valueLabel: 'Files',
    toggles: false,
    rows: planBodyRows,
  },
  {
    id: 'assertions',
    title: 'Assertions',
    label: 'Request',
    valueLabel: 'Checks',
    toggles: false,
    rows: planAssertionRows,
  },
  {
    id: 'thresholds',
    title: 'Thresholds',
    label: 'Metric',
    valueLabel: 'Limit',
    toggles: false,
    rows: planThresholdRows,
  },
];

/** Compares two run plans section by section, laid out like the plan form. */
export function planChanges(before: PlanForm, after: PlanForm): PlanSectionChanges[] {
  return SECTIONS.map(({ rows, ...section }) => {
    const changes = diffRows(rows(before), rows(after));
    return { ...section, rows: changes, changes: changes.filter((c) => c.kind !== 'same').length };
  });
}
