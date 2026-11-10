import { Document, Pair, Scalar, YAMLMap, isMap, isScalar, isSeq, parseDocument } from 'yaml';

/** One `assert` entry of a plan, as the strings shown in the form. */
export interface PlanAssertion {
  /** A request path, or `*` for every request. */
  key: string;
  status: string;
  maxTime: string;
  bodyContains: string;
  bodyNotContains: string;
}

/** The fields of a run plan, as the strings shown in the visual editor. Empty means "not set". */
export interface PlanForm {
  name: string;
  requests: string[];
  /** `sequential` (or empty), `shuffle`, `random` or `roundRobin`. */
  order: string;
  /** The weight of each request path that has one (used with the `random` order). */
  weights: Record<string, string>;
  threads: string;
  iterations: string;
  duration: string;
  rampUp: string;
  thinkMin: string;
  thinkMax: string;
  maxRps: string;
  dataFile: string;
  dataMode: string;
  dataOnEof: string;
  assertions: PlanAssertion[];
  thresholds: Record<ThresholdKey, string>;
}

export const THRESHOLD_KEYS = [
  'p50',
  'p90',
  'p95',
  'p99',
  'average',
  'errorRate',
  'throughput',
] as const;
export type ThresholdKey = (typeof THRESHOLD_KEYS)[number];

export const ASSERT_ALL = '*';

/** The ways a virtual user can take the plan's requests in each iteration. */
export const REQUEST_ORDERS = ['sequential', 'shuffle', 'random', 'roundRobin'] as const;
export type RequestOrder = (typeof REQUEST_ORDERS)[number];

/** Whether an order sends one request per iteration rather than all of them. */
export function picksOneRequest(order: string): boolean {
  return order === 'random' || order === 'roundRobin';
}

type Path = (string | number)[];

const ORDER: Record<string, string[]> = {
  '': ['name', 'requests', 'order', 'weights', 'load', 'data', 'assert', 'thresholds'],
  load: ['threads', 'iterations', 'duration', 'rampUp', 'thinkTime', 'maxRps'],
  data: ['file', 'mode', 'onEof'],
  assert: [],
  thresholds: [...THRESHOLD_KEYS],
};
const ASSERT_ORDER = ['status', 'maxTime', 'bodyContains', 'bodyNotContains'];

const NUMBER = /^\d+(\.\d+)?$/;

/** Reads the plan's fields; `null` when the text is not a YAML mapping the form can show. */
export function readPlanForm(yaml: string): PlanForm | null {
  const doc = parseDocument(yaml);
  if (doc.errors.length > 0 || !isMap(doc.contents)) return null;

  const text = (path: Path): string => {
    const node = doc.getIn(path, true);
    return isScalar(node) && node.value !== null ? String(node.value) : '';
  };
  const think = doc.getIn(['load', 'thinkTime'], true);
  const requests = doc.getIn(['requests'], true);
  const assertNode = doc.getIn(['assert'], true);
  const weightsNode = doc.getIn(['weights'], true);

  const status = (path: Path): string => {
    const node = doc.getIn(path, true);
    return isSeq(node)
      ? node.items.map((item) => String(isScalar(item) ? item.value : item)).join(', ')
      : text(path);
  };

  return {
    name: text(['name']),
    requests: isSeq(requests)
      ? requests.items.map((item) => String(isScalar(item) ? item.value : ''))
      : [],
    order: text(['order']),
    weights: isMap(weightsNode)
      ? Object.fromEntries(
          weightsNode.items.map((pair) => {
            const key = String(isScalar(pair.key) ? pair.key.value : pair.key);
            return [key, text(['weights', key])];
          }),
        )
      : {},
    threads: text(['load', 'threads']),
    iterations: text(['load', 'iterations']),
    duration: text(['load', 'duration']),
    rampUp: text(['load', 'rampUp']),
    thinkMin: isMap(think) ? text(['load', 'thinkTime', 'min']) : text(['load', 'thinkTime']),
    thinkMax: isMap(think) ? text(['load', 'thinkTime', 'max']) : '',
    maxRps: text(['load', 'maxRps']),
    dataFile: text(['data', 'file']),
    dataMode: text(['data', 'mode']),
    dataOnEof: text(['data', 'onEof']),
    assertions: isMap(assertNode)
      ? assertNode.items.map((pair) => {
          const key = String(isScalar(pair.key) ? pair.key.value : pair.key);
          return {
            key,
            status: status(['assert', key, 'status']),
            maxTime: text(['assert', key, 'maxTime']),
            bodyContains: text(['assert', key, 'bodyContains']),
            bodyNotContains: text(['assert', key, 'bodyNotContains']),
          };
        })
      : [],
    thresholds: Object.fromEntries(
      THRESHOLD_KEYS.map((key) => [key, text(['thresholds', key])]),
    ) as Record<ThresholdKey, string>,
  };
}

/**
 * Edits plan text in place so the rest of the file (comments, unknown keys) survives: sets a
 * scalar (an empty value removes the key and any parent left empty).
 */
export function setPlanValue(yaml: string, path: Path, value: string): string {
  return edit(yaml, (doc) => assign(doc, path, value));
}

/** Replaces the request list, dropping the weights of requests that are no longer in it. */
export function setPlanRequests(yaml: string, requests: string[]): string {
  return edit(yaml, (doc) => {
    if (requests.length === 0) {
      doc.deleteIn(['requests']);
    } else {
      doc.setIn(['requests'], doc.createNode(requests));
    }
    const weights = doc.getIn(['weights'], true);
    if (isMap(weights)) {
      for (const pair of [...weights.items]) {
        const key = String(isScalar(pair.key) ? pair.key.value : pair.key);
        if (!requests.includes(key)) prune(doc, ['weights', key]);
      }
    }
  });
}

/** Sets how users take the requests; the default (`sequential`) is left out of the file. */
export function setPlanOrder(yaml: string, order: string): string {
  return setPlanValue(yaml, ['order'], order === 'sequential' ? '' : order);
}

/** Sets the think time: a single value, or a `min`/`max` range when both differ. */
export function setPlanThinkTime(yaml: string, min: string, max: string): string {
  return edit(yaml, (doc) => {
    min = min.trim();
    max = max.trim();
    if (max === '' || max === min) {
      assign(doc, ['load', 'thinkTime'], min);
      return;
    }
    if (!isMap(doc.getIn(['load', 'thinkTime'], true))) doc.deleteIn(['load', 'thinkTime']);
    assign(doc, ['load', 'thinkTime', 'min'], min);
    assign(doc, ['load', 'thinkTime', 'max'], max);
  });
}

/** Sets the status checks of an assertion from a comma-separated list. */
export function setAssertionStatus(yaml: string, key: string, value: string): string {
  return edit(yaml, (doc) => {
    const codes = value
      .split(',')
      .map((code) => code.trim())
      .filter(Boolean);
    if (codes.length <= 1) {
      assign(doc, ['assert', key, 'status'], codes[0] ?? '');
    } else {
      doc.setIn(['assert', key, 'status'], doc.createNode(codes));
    }
  });
}

/** Adds an empty assertion block for a request path (or `*`). */
export function addAssertion(yaml: string, key: string): string {
  return edit(yaml, (doc) => {
    if (!doc.hasIn(['assert', key])) doc.setIn(['assert', key], doc.createNode({ status: '2xx' }));
  });
}

export function removeAssertion(yaml: string, key: string): string {
  return edit(yaml, (doc) => prune(doc, ['assert', key]));
}

/** Applies a change to the parsed text; text that isn't a YAML mapping is returned untouched. */
function edit(yaml: string, change: (doc: Document) => void): string {
  const doc = parseDocument(yaml);
  if (doc.errors.length > 0) return yaml;
  if (!doc.contents) doc.contents = new YAMLMap() as unknown as Document.Parsed['contents'];
  if (!isMap(doc.contents)) return yaml;
  change(doc);
  sortKeys(doc);
  const result = doc.toString({ indent: 2, lineWidth: 0 });
  return result === yaml ? yaml : result;
}

function assign(doc: Document, path: Path, raw: string): void {
  const value = raw.trim();
  if (value === '') {
    prune(doc, path);
    return;
  }
  const scalar = new Scalar(NUMBER.test(value) ? Number(value) : value);
  if (!isMap(doc.getIn(path.slice(0, -1), true))) {
    // A parent that is missing (or not a mapping) is replaced by a mapping.
    doc.setIn(path.slice(0, -1), new YAMLMap());
  }
  doc.setIn(path, scalar);
}

/** Deletes the value and then any ancestor mapping (below the root) that became empty. */
function prune(doc: Document, path: Path): void {
  doc.deleteIn(path);
  for (let length = path.length - 1; length >= 1; length--) {
    const parent = doc.getIn(path.slice(0, length), true);
    if (isMap(parent) && parent.items.length === 0) doc.deleteIn(path.slice(0, length));
    else break;
  }
}

function sortKeys(doc: Document): void {
  const sort = (map: unknown, order: string[]): void => {
    if (!isMap(map)) return;
    const rank = (pair: Pair): number => {
      const index = order.indexOf(String(isScalar(pair.key) ? pair.key.value : pair.key));
      return index < 0 ? order.length : index;
    };
    const sorted = map.items
      .map((pair, index) => ({ pair, index }))
      .sort((a, b) => rank(a.pair) - rank(b.pair) || a.index - b.index);
    map.items = sorted.map((entry) => entry.pair);
  };
  sort(doc.contents, ORDER['']);
  for (const section of ['load', 'data', 'thresholds'])
    sort(doc.getIn([section], true), ORDER[section]);
  const assertNode = doc.getIn(['assert'], true);
  if (isMap(assertNode)) {
    for (const pair of assertNode.items) sort(pair.value, ASSERT_ORDER);
  }
}
