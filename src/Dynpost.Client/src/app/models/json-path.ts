/**
 * A JSONPath subset for filtering JSON response bodies in the viewer: `$`, `.name`, `['name']`,
 * `[0]` (negative counts from the end), `[*]` and `.*`, `[0,2]`, slices `[1:3]`, recursive
 * descent `..name` / `..*`, and filters `[?(@.price < 10)]` comparing a child (or `@`) with a
 * number, string, `true`, `false` or `null` (`==`, `!=`, `<`, `<=`, `>`, `>=`), or `[?(@.name)]`
 * for children that have it.
 */

export type JsonValue =
  null | boolean | number | string | JsonValue[] | { [key: string]: JsonValue };

export type JsonPathResult = { ok: true; matches: JsonValue[] } | { ok: false; error: string };

type Segment =
  | { kind: 'child'; names: (string | number)[]; recursive: boolean }
  | { kind: 'wildcard'; recursive: boolean }
  | { kind: 'slice'; start: number | null; end: number | null; step: number; recursive: boolean }
  | { kind: 'filter'; filter: Filter; recursive: boolean };

interface Filter {
  path: (string | number)[];
  op: '==' | '!=' | '<' | '<=' | '>' | '>=' | 'exists';
  value?: JsonValue;
}

/** Runs `path` on `value`; the matches are in document order. */
export function queryJsonPath(value: JsonValue, path: string): JsonPathResult {
  let segments: Segment[];
  try {
    segments = parse(path.trim());
  } catch (error) {
    return { ok: false, error: error instanceof Error ? error.message : String(error) };
  }
  let nodes: JsonValue[] = [value];
  for (const segment of segments) {
    const next: JsonValue[] = [];
    for (const node of nodes) {
      for (const target of segment.recursive ? descendants(node) : [node])
        apply(segment, target, next);
    }
    nodes = next;
  }
  return { ok: true, matches: nodes };
}

/** Pretty JSON of what `path` matches: the value itself for a single match on a definite path, else an array. */
export function filterJson(
  text: string,
  path: string,
): { text: string; count: number } | { error: string } {
  let value: JsonValue;
  try {
    value = JSON.parse(text);
  } catch {
    return { error: 'The body is not valid JSON.' };
  }
  const result = queryJsonPath(value, path);
  if (!result.ok) return { error: result.error };
  const definite = isDefinite(path);
  const shown = definite && result.matches.length === 1 ? result.matches[0] : result.matches;
  return { text: JSON.stringify(shown, null, 2) ?? '', count: result.matches.length };
}

function isDefinite(path: string): boolean {
  return !/\*|\.\.|\[\?|:|,/.test(path);
}

function descendants(node: JsonValue): JsonValue[] {
  const all: JsonValue[] = [node];
  const visit = (value: JsonValue) => {
    if (value === null || typeof value !== 'object') return;
    for (const child of Array.isArray(value) ? value : Object.values(value)) {
      all.push(child);
      visit(child);
    }
  };
  visit(node);
  return all;
}

function apply(segment: Segment, node: JsonValue, out: JsonValue[]): void {
  if (node === null || typeof node !== 'object') return;
  switch (segment.kind) {
    case 'child':
      for (const name of segment.names) {
        if (Array.isArray(node)) {
          if (typeof name !== 'number') continue;
          const index = name < 0 ? node.length + name : name;
          if (index >= 0 && index < node.length) out.push(node[index]);
        } else if (typeof name === 'string' && Object.hasOwn(node, name)) {
          out.push(node[name]);
        }
      }
      break;
    case 'wildcard':
      out.push(...(Array.isArray(node) ? node : Object.values(node)));
      break;
    case 'slice': {
      if (!Array.isArray(node)) return;
      const length = node.length;
      const norm = (i: number) => (i < 0 ? Math.max(length + i, 0) : Math.min(i, length));
      const start = segment.start === null ? 0 : norm(segment.start);
      const end = segment.end === null ? length : norm(segment.end);
      for (let i = start; i < end; i += segment.step) out.push(node[i]);
      break;
    }
    case 'filter':
      for (const child of Array.isArray(node) ? node : Object.values(node)) {
        if (matches(segment.filter, child)) out.push(child);
      }
      break;
  }
}

function matches(filter: Filter, node: JsonValue): boolean {
  let value: JsonValue | undefined = node;
  for (const name of filter.path) {
    if (value === null || typeof value !== 'object') return false;
    if (Array.isArray(value)) value = typeof name === 'number' ? value[name] : undefined;
    else value = typeof name === 'string' && Object.hasOwn(value, name) ? value[name] : undefined;
    if (value === undefined) return false;
  }
  if (filter.op === 'exists') return true;
  const other = filter.value;
  switch (filter.op) {
    case '==':
      return value === other;
    case '!=':
      return value !== other;
    default: {
      if (typeof value !== typeof other || (typeof value !== 'number' && typeof value !== 'string'))
        return false;
      const a = value as number | string;
      const b = other as number | string;
      return filter.op === '<'
        ? a < b
        : filter.op === '<='
          ? a <= b
          : filter.op === '>'
            ? a > b
            : a >= b;
    }
  }
}

function parse(path: string): Segment[] {
  if (!path.startsWith('$')) throw new Error('A JSONPath starts with $.');
  const segments: Segment[] = [];
  let i = 1;
  while (i < path.length) {
    let recursive = false;
    if (path.startsWith('..', i)) {
      recursive = true;
      i += 2;
      if (path[i] !== '[') {
        const name = readName(path, i);
        i += name.length;
        segments.push(
          name === '*'
            ? { kind: 'wildcard', recursive }
            : { kind: 'child', names: [name], recursive },
        );
        continue;
      }
    } else if (path[i] === '.') {
      i++;
      const name = readName(path, i);
      if (!name) throw new Error(`Expected a name after '.' at ${i}.`);
      i += name.length;
      segments.push(
        name === '*'
          ? { kind: 'wildcard', recursive }
          : { kind: 'child', names: [name], recursive },
      );
      continue;
    }
    if (path[i] !== '[') throw new Error(`Unexpected '${path[i]}' at ${i}.`);
    const close = findClose(path, i);
    const inner = path.slice(i + 1, close).trim();
    i = close + 1;
    segments.push(parseBracket(inner, recursive));
  }
  return segments;
}

function readName(path: string, start: number): string {
  const match = /^(\*|[^.[\]\s]+)/.exec(path.slice(start));
  return match ? match[1] : '';
}

function findClose(path: string, open: number): number {
  let quote: string | null = null;
  let depth = 0;
  for (let i = open; i < path.length; i++) {
    const c = path[i];
    if (quote) {
      if (c === '\\') i++;
      else if (c === quote) quote = null;
    } else if (c === "'" || c === '"') quote = c;
    else if (c === '[' || c === '(') depth++;
    else if (c === ']' || c === ')') {
      depth--;
      if (depth === 0 && c === ']') return i;
    }
  }
  throw new Error(`A '[' at ${open} isn't closed.`);
}

function parseBracket(inner: string, recursive: boolean): Segment {
  if (inner === '*') return { kind: 'wildcard', recursive };
  if (inner.startsWith('?'))
    return { kind: 'filter', filter: parseFilter(inner.slice(1).trim()), recursive };
  if (/^-?\d*:-?\d*(:\d+)?$/.test(inner)) {
    const [start, end, step] = inner.split(':');
    return {
      kind: 'slice',
      start: start ? Number(start) : null,
      end: end ? Number(end) : null,
      step: step ? Math.max(Number(step), 1) : 1,
      recursive,
    };
  }
  const names = splitTopLevel(inner).map((part) => {
    const text = part.trim();
    if (/^-?\d+$/.test(text)) return Number(text);
    if (/^'.*'$|^".*"$/s.test(text)) return unquote(text);
    throw new Error(`'${text}' is not an index or a quoted name.`);
  });
  return { kind: 'child', names, recursive };
}

function parseFilter(text: string): Filter {
  const match =
    /^\(\s*@((?:\.[^.\s=!<>()[\]]+|\[[^\]]+\])*)\s*(?:(==|!=|<=|>=|<|>)\s*(.+?))?\s*\)$/s.exec(
      text,
    );
  if (!match) throw new Error(`'${text}' is not a supported filter, such as ?(@.price < 10).`);
  const path = [...match[1].matchAll(/\.([^.[\]]+)|\[([^\]]+)\]/g)].map((m) =>
    m[1] !== undefined ? m[1] : /^-?\d+$/.test(m[2]) ? Number(m[2]) : unquote(m[2].trim()),
  );
  if (!match[2]) return { path, op: 'exists' };
  const raw = match[3].trim();
  let value: JsonValue;
  if (/^'.*'$/s.test(raw)) value = unquote(raw);
  else {
    try {
      value = JSON.parse(raw);
    } catch {
      throw new Error(`'${raw}' is not a number, a quoted string, true, false or null.`);
    }
  }
  return { path, op: match[2] as Filter['op'], value };
}

function splitTopLevel(text: string): string[] {
  const parts: string[] = [];
  let quote: string | null = null;
  let current = '';
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (quote) {
      current += c;
      if (c === '\\') current += text[++i] ?? '';
      else if (c === quote) quote = null;
    } else if (c === "'" || c === '"') {
      quote = c;
      current += c;
    } else if (c === ',') {
      parts.push(current);
      current = '';
    } else current += c;
  }
  parts.push(current);
  return parts;
}

function unquote(text: string): string {
  return text.slice(1, -1).replace(/\\(.)/g, '$1');
}

/**
 * The JSONPath of the value at `offset` in the JSON `text` (a key or a value), such as
 * `$.items[2].id`; `$` at the top, `null` when the text isn't JSON up to there.
 */
export function jsonPathAt(text: string, offset: number): string | null {
  type Frame = { array: boolean; index: number; key: string | null };
  const stack: Frame[] = [];
  let pendingKey: string | null = null;
  let i = 0;
  const skip = () => {
    while (i < text.length && /\s/.test(text[i])) i++;
  };
  const path = () =>
    '$' +
    stack
      .map((f) =>
        f.array
          ? `[${f.index}]`
          : f.key === null
            ? ''
            : /^[A-Za-z_$][\w$]*$/.test(f.key)
              ? `.${f.key}`
              : `['${f.key.replace(/\\/g, '\\\\').replace(/'/g, "\\'")}']`,
      )
      .join('');
  while (i < text.length) {
    skip();
    if (i >= text.length) break;
    const c = text[i];
    const top = stack.at(-1);
    if (c === '{' || c === '[') {
      if (i >= offset) return path();
      stack.push({ array: c === '[', index: 0, key: null });
      i++;
    } else if (c === '}' || c === ']') {
      if (i >= offset) return path();
      stack.pop();
      i++;
    } else if (c === ',') {
      if (top?.array) top.index++;
      else if (top) top.key = null;
      i++;
    } else if (c === ':') {
      if (top) top.key = pendingKey;
      i++;
    } else if (c === '"') {
      const start = i;
      i++;
      while (i < text.length && text[i] !== '"') i += text[i] === '\\' ? 2 : 1;
      i++;
      let raw: string;
      try {
        raw = JSON.parse(text.slice(start, i));
      } catch {
        return null;
      }
      skip();
      const isKey = !!top && !top.array && text[i] === ':';
      if (isKey) {
        pendingKey = raw;
        if (offset >= start && offset < i) {
          top.key = raw;
          return path();
        }
      } else if (offset >= start && offset < i) return path();
    } else {
      const start = i;
      while (i < text.length && !/[\s,\]}]/.test(text[i])) i++;
      if (offset >= start && offset <= i) return path();
    }
    if (i > offset && stack.length === 0) return '$';
  }
  return stack.length === 0 ? '$' : null;
}

/** A suggestion for the JSONPath being typed: what the list shows and the path it completes to. */
export interface JsonPathCompletion {
  label: string;
  /** The text before the caret once the suggestion is taken. */
  text: string;
  /** Whether `label` is a key (otherwise an array selector such as `[*]`). */
  kind: 'key' | 'index';
}

const IDENTIFIER = /^[A-Za-z_$][\w$]*$/;

/**
 * Suggestions for the JSONPath `text` (what is before the caret) typed against `value`: the keys of
 * what the path before the last `.`, `..`, `[` or `['` matches that start with (or else contain)
 * what follows it, ignoring case, and `[*]` for arrays. Empty when that path isn't valid.
 */
export function jsonPathCompletions(
  value: JsonValue,
  text: string,
  limit = 50,
): JsonPathCompletion[] {
  const split = splitPartial(text);
  if (!split) return [];
  const { base, sep, partial, quote } = split;
  const result = queryJsonPath(value, base || '$');
  if (!result.ok || !base.startsWith('$')) return [];
  const nodes = sep === '..' ? result.matches.flatMap(descendants) : result.matches;

  const keys = new Set<string>();
  let arrays = false;
  let maxLength = 0;
  for (const node of nodes) {
    if (Array.isArray(node)) {
      arrays = true;
      maxLength = Math.max(maxLength, node.length);
    } else if (node !== null && typeof node === 'object') {
      for (const key of Object.keys(node)) keys.add(key);
    }
  }

  const needle = partial.toLowerCase();
  const ranked = [...keys]
    .map((key) => ({ key, at: key.toLowerCase().indexOf(needle) }))
    .filter((k) => k.at >= 0)
    .sort((a, b) => Number(a.at !== 0) - Number(b.at !== 0));
  const dots = sep === '..' ? '..' : '.';
  const completions: JsonPathCompletion[] = ranked.map(({ key }) => ({
    label: key,
    kind: 'key',
    text:
      sep === '['
        ? `${base}[${quote ?? "'"}${escapeName(key, quote ?? "'")}${quote ?? "'"}]`
        : IDENTIFIER.test(key)
          ? `${base}${dots}${key}`
          : `${base}${sep === '..' ? '..' : ''}['${escapeName(key, "'")}']`,
  }));
  if (arrays && quote === null && (partial === '' || partial === '*')) {
    const prefix = sep === '..' ? `${base}..` : base;
    completions.unshift({ label: '[*]', kind: 'index', text: `${prefix}[*]` });
    if (sep !== '..') {
      for (let i = 0; i < Math.min(maxLength, 3); i++)
        completions.push({ label: `[${i}]`, kind: 'index', text: `${base}[${i}]` });
    }
  }
  return completions.filter((c) => c.text !== text).slice(0, limit);
}

/** Splits the typed path into the part already complete and the name being typed after it. */
function splitPartial(
  text: string,
): { base: string; sep: '.' | '..' | '['; partial: string; quote: string | null } | null {
  if (/^\s*\$?$/.test(text)) return { base: '$', sep: '.', partial: '', quote: null };
  // Inside an open bracket: `[` or `['par` / `["par`.
  const bracket = /^(.*)\[\s*(?:(['"])((?:[^\\]|\\.)*))?$/s.exec(text);
  if (bracket && !(bracket[2] && bracket[3].includes(bracket[2]))) {
    if (bracket[2] === undefined && !/^\s*$/.test(text.slice(bracket[1].length + 1))) return null;
    return {
      base: bracket[1],
      sep: '[',
      partial: (bracket[3] ?? '').replace(/\\(.)/g, '$1'),
      quote: bracket[2] ?? null,
    };
  }
  const dot = /^(.*?)(\.\.|\.)([^.[\]\s'"]*)$/s.exec(text);
  if (dot) return { base: dot[1], sep: dot[2] as '.' | '..', partial: dot[3], quote: null };
  return null;
}

function escapeName(name: string, quote: string): string {
  return name.replace(/\\/g, '\\\\').replaceAll(quote, `\\${quote}`);
}
