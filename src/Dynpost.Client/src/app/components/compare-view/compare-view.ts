import { Component, computed, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faCircleInfo,
  faCodeCompare,
  faFileCode,
  faFileLines,
  faRightLeft,
  faSpinner,
  faXmark,
} from '@fortawesome/free-solid-svg-icons';
import { CompareStore } from '../../services/compare-store';
import { CodeDiffEditor } from '../code-diff-editor/code-diff-editor';
import { PlanDiff } from '../plan-diff/plan-diff';
import { RequestDiff } from '../request-diff/request-diff';

type CompareMode = 'visual' | 'text';

/**
 * Two requests, or two run plans, side by side: laid out like their editors with what differs
 * marked (visual), or as the text of their files.
 */
@Component({
  selector: 'app-compare-view',
  imports: [FaIconComponent, CodeDiffEditor, PlanDiff, RequestDiff],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  templateUrl: './compare-view.html',
})
export class CompareView {
  protected readonly store = inject(CompareStore);
  protected readonly faCircleInfo = faCircleInfo;
  protected readonly faCodeCompare = faCodeCompare;
  protected readonly faFileCode = faFileCode;
  protected readonly faFileLines = faFileLines;
  protected readonly faRightLeft = faRightLeft;
  protected readonly faSpinner = faSpinner;
  protected readonly faXmark = faXmark;

  /** The mode the user picked; kept between comparisons. */
  protected readonly mode = signal<CompareMode>('visual');
  protected readonly sideBySide = signal(true);

  /** Whether both sides could be read as what they are (a plan's text may not be). */
  protected readonly canShowVisual = computed(() => {
    const c = this.store.comparison();
    if (!c) return false;
    return c.kind === 'request'
      ? !!c.left.request && !!c.right.request
      : !!c.left.plan && !!c.right.plan;
  });

  protected readonly effectiveMode = computed<CompareMode>(() =>
    this.canShowVisual() ? this.mode() : 'text',
  );

  protected readonly same = computed(() => {
    const c = this.store.comparison();
    return !!c && c.left.text === c.right.text;
  });
}
