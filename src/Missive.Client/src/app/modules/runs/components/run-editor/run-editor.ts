import { Component, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faChartColumn,
  faCode,
  faCompress,
  faExpand,
  faPlay,
  faSliders,
  faStop,
  faTableList,
  faTrash,
  faXmark,
} from '@fortawesome/free-solid-svg-icons';
import { CodeEditor } from '../../../../components/code-editor/code-editor';
import { RunStore } from '../../../../services/run-store';
import { RunHistory } from '../run-history/run-history';
import { RunPlanForm } from '../run-plan-form/run-plan-form';
import { RunResults } from '../run-results/run-results';
import { RunSamples } from '../run-samples/run-samples';

/** The two zones of the editor: the plan, and the results of its run. */
export type RunEditorZone = 'plan' | 'results';

/**
 * Edits a run plan (as a form or as YAML) next to the results of its run: a dashboard, or a table
 * of every request sent with its response. Either zone can be maximized to the full width.
 */
@Component({
  selector: 'app-run-editor',
  imports: [CodeEditor, FaIconComponent, RunHistory, RunPlanForm, RunResults, RunSamples],
  host: { class: 'flex min-h-0 flex-1 flex-col overflow-hidden' },
  templateUrl: './run-editor.html',
})
export class RunEditor {
  protected readonly store = inject(RunStore);
  protected readonly faPlay = faPlay;
  protected readonly faStop = faStop;
  protected readonly faXmark = faXmark;
  protected readonly faTrash = faTrash;
  protected readonly faSliders = faSliders;
  protected readonly faCode = faCode;
  protected readonly faChartColumn = faChartColumn;
  protected readonly faTableList = faTableList;
  protected readonly faExpand = faExpand;
  protected readonly faCompress = faCompress;

  /** Whether the plan is edited with the form or as YAML text. */
  protected readonly mode = signal<'visual' | 'yaml'>('visual');
  /** Whether the results show the dashboard or the table of requests and responses. */
  protected readonly view = signal<'dashboard' | 'requests'>('dashboard');
  /** The zone that takes the full width, or `null` when both are shown side by side. */
  protected readonly maximized = signal<RunEditorZone | null>(null);

  protected toggleMaximized(zone: RunEditorZone): void {
    this.maximized.update((current) => (current === zone ? null : zone));
  }

  /** Shows the table of requests and responses. */
  protected showRequests(): void {
    this.view.set('requests');
  }

  protected readonly iconButton =
    'flex size-6 items-center justify-center rounded text-xs text-slate-500 hover:bg-slate-200';

  protected readonly button =
    'flex items-center gap-2 rounded px-3 py-1 text-sm disabled:opacity-50';
}
