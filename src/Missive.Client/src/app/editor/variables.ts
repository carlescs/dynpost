/**
 * A `{{variable}}` reference, as the host resolves it (spaces inside the braces are allowed).
 * Keep in sync with `VariableTemplate` in `src/Missive.Domain/Requests/VariableTemplate.cs`.
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
