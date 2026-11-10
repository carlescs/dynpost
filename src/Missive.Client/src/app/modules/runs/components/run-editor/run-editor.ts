import { Component, inject, input, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faChartColumn,
  faCode,
  faPlay,
  faSliders,
  faStop,
  faTrash,
} from '@fortawesome/free-solid-svg-icons';
import { CodeEditor } from '../../../../components/code-editor/code-editor';
import {
  RunPlanDraft,
  RunStore,
  isRunPlanDirty,
  isRunPlanRunning,
} from '../../../../services/run-store';
import { RunHistory } from '../run-history/run-history';
import { RunPlanForm } from '../run-plan-form/run-plan-form';
import { RunResults } from '../run-results/run-results';

/** Edits a run plan open in a tab (as a form or as YAML) next to the dashboard of its run. */
@Component({
  selector: 'app-run-editor',
  imports: [CodeEditor, FaIconComponent, RunHistory, RunPlanForm, RunResults],
  host: { class: 'flex min-h-0 flex-1 flex-col overflow-hidden' },
  templateUrl: './run-editor.html',
})
export class RunEditor {
  readonly draft = input.required<RunPlanDraft>();

  protected readonly store = inject(RunStore);
  protected readonly isDirty = isRunPlanDirty;
  protected readonly isRunning = isRunPlanRunning;
  protected readonly faPlay = faPlay;
  protected readonly faStop = faStop;
  protected readonly faTrash = faTrash;
  protected readonly faSliders = faSliders;
  protected readonly faCode = faCode;
  protected readonly faChartColumn = faChartColumn;

  /** Whether the plan is edited with the form or as YAML text. */
  protected readonly mode = signal<'visual' | 'yaml'>('visual');

  protected readonly button =
    'flex items-center gap-2 rounded px-3 py-1 text-sm disabled:opacity-50';
}
