import { Component, input } from '@angular/core';
import { TextSegment } from '../../models/highlight';

/**
 * Text with the parts a search matched highlighted (see `models/highlight.ts`). Every part is its
 * own element, so the template's line breaks (whitespace-only, which Angular drops) never show as
 * spaces inside words.
 */
@Component({
  selector: 'app-highlighted-text',
  templateUrl: './highlighted-text.html',
})
export class HighlightedText {
  readonly segments = input.required<readonly TextSegment[]>();
  /** The classes of the highlighted parts. */
  readonly markClass = input('bg-transparent font-semibold text-sky-700');
}
