/** A piece of text shown either plain or highlighted as part of a match. */
export interface TextSegment {
  text: string;
  match: boolean;
}

/** Splits `text` into runs of matched and unmatched characters, from the indexes of matched ones. */
export function highlightPositions(text: string, positions: readonly number[]): TextSegment[] {
  const matched = new Set(positions);
  return highlight(text, (i) => matched.has(i));
}

/** Splits `text` into the parts the ranges (start and length) cover and the parts between them. */
export function highlightRanges(
  text: string,
  ranges: readonly { start: number; length: number }[],
): TextSegment[] {
  return highlight(text, (i) => ranges.some((r) => i >= r.start && i < r.start + r.length));
}

function highlight(text: string, isMatch: (index: number) => boolean): TextSegment[] {
  const segments: TextSegment[] = [];
  for (let i = 0; i < text.length; i++) {
    const match = isMatch(i);
    const last = segments.at(-1);
    if (last && last.match === match) last.text += text[i];
    else segments.push({ text: text[i], match });
  }
  return segments;
}
