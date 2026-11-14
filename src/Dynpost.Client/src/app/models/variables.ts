/**
 * A `{{variable}}` reference, as the host resolves it (spaces inside the braces are allowed).
 * Keep in sync with `VariableTemplate` in `src/Dynpost.Domain/Requests/VariableTemplate.cs`.
 * Global: reset `lastIndex` or use `matchAll`.
 */
export const VARIABLE_PATTERN = /\{\{\s*([^{}\s]+)\s*\}\}/g;

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
  return variables && !variables.has(name) ? 'variable-undefined' : 'variable-highlight';
}

/** A one-line description of a `{{name}}` reference, for hovers. */
export function describeVariable(name: string, variables: VariableLookup | null): string {
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
