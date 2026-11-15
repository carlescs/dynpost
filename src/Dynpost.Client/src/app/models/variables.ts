/**
 * A `{{variable}}` reference, as the host resolves it (spaces inside the braces are allowed, and a
 * built-in `{{$name arg …}}` may have arguments).
 * Keep in sync with `VariableTemplate` in `src/Dynpost.Domain/Requests/VariableTemplate.cs`.
 * Global: reset `lastIndex` or use `matchAll`.
 */
export const VARIABLE_PATTERN = /\{\{\s*(\$[^{}\s]+(?:[ \t]+[^{}\s]+)*|[^{}\s]+)\s*\}\}/g;

/**
 * The built-in variables the host generates when sending, with what they hold. Keep in sync with
 * `DynamicVariables` in `src/Dynpost.Domain/Requests/DynamicVariables.cs`.
 */
export const DYNAMIC_VARIABLES: Readonly<Record<string, string>> = {
  $uuid: 'a random UUID (version 4)',
  $timestamp: 'the current time in Unix seconds',
  $isoTimestamp: 'the current time in ISO 8601 (UTC)',
  $randomInt:
    'a random whole number from 0 to 1000, or from min to max with {{$randomInt min max}}',
};

/** Whether `name` (as written between the braces) refers to a built-in variable. */
export function isDynamicVariable(name: string): boolean {
  return name.length > 1 && name.startsWith('$');
}

/** The description of a supported built-in reference, or `null` when it isn't one the host knows. */
function dynamicDescription(name: string): string | null {
  const [bare, ...args] = name.split(/[ \t]+/);
  const description = Object.hasOwn(DYNAMIC_VARIABLES, bare) ? DYNAMIC_VARIABLES[bare] : null;
  if (!description) return null;
  if (args.length === 0) return description;
  if (bare !== '$randomInt' || args.length !== 2) return null;
  const [min, max] = args.map(Number);
  return Number.isInteger(min) && Number.isInteger(max) && min <= max
    ? `a random whole number from ${min} to ${max}`
    : null;
}

export interface TextSegment {
  text: string;
  /** The variable name when this segment is a `{{variable}}` reference. */
  variable?: string;
}

/** Splits text into plain runs and `{{variable}}` references. */
export function splitVariables(text: string): TextSegment[] {
  const segments: TextSegment[] = [];
  let last = 0;
  for (const match of text.matchAll(VARIABLE_PATTERN)) {
    if (match.index > last) segments.push({ text: text.slice(last, match.index) });
    segments.push({ text: match[0], variable: match[1] });
    last = match.index + match[0].length;
  }
  if (last < text.length) segments.push({ text: text.slice(last) });
  return segments;
}

/** What the editors know about a variable of the selected environment or of a folder. */
export interface VariableInfo {
  value: string;
  secret: boolean;
  /** Whether the value was captured from a response (and overrides the environment's). */
  captured?: boolean;
  /** The folder that sets it, for a folder variable (its value as written, not resolved). */
  folder?: string;
}

/** The variables a request can use by name; secrets without a local value are left out. */
export type VariableLookup = ReadonlyMap<string, VariableInfo>;

/** A variable a folder sets, as `InheritedVariableDto` carries it. */
export interface FolderVariable {
  name: string;
  value: string;
  /** The folder that sets it. */
  folder: string;
}

/**
 * The variables a request in a folder can use: the selected environment's, plus the folders'
 * variables it doesn't define (the environment's win, as when the host sends).
 */
export function withFolderVariables(
  environment: VariableLookup,
  folders: readonly FolderVariable[],
): VariableLookup {
  const missing = folders.filter((v) => !environment.has(v.name));
  if (!missing.length) return environment;
  const combined = new Map(environment);
  for (const v of missing)
    combined.set(v.name, { value: v.value, secret: false, folder: v.folder });
  return combined;
}

/** The CSS class for a `{{name}}` reference: undefined ones stand out when `variables` is known. */
export function variableClass(name: string, variables: VariableLookup | null): string {
  if (isDynamicVariable(name) && !variables?.has(name))
    return dynamicDescription(name) ? 'variable-builtin' : 'variable-undefined';
  return variables && !variables.has(name) ? 'variable-undefined' : 'variable-highlight';
}

/** A one-line description of a `{{name}}` reference, for hovers. */
export function describeVariable(name: string, variables: VariableLookup | null): string {
  if (isDynamicVariable(name) && !variables?.has(name)) {
    const description = dynamicDescription(name);
    return description
      ? `${name}: built-in, ${description}, generated when sent`
      : `${name} is not a built-in variable (${Object.keys(DYNAMIC_VARIABLES).join(', ')})`;
  }
  if (!variables) return `Variable ${name}`;
  const variable = variables.get(name);
  if (!variable) return `${name} is not defined in the selected environment`;
  const source = variable.captured
    ? ' (captured)'
    : variable.folder
      ? ` (set by the folder ${variable.folder})`
      : '';
  return variable.secret
    ? `${name}: secret value${source}`
    : `${name} = ${variable.value}${source}`;
}
