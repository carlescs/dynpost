import { DecimalPipe, KeyValuePipe } from '@angular/common';
import { Component, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faPlay, faStop, faXmark } from '@fortawesome/free-solid-svg-icons';
import { CodeEditor } from '../../../../components/code-editor/code-editor';
import { RunStore } from '../../../../services/run-store';
import { RunChart } from '../run-chart/run-chart';

/** Edits a run plan as YAML and shows the live statistics of its run. */
@Component({
  selector: 'app-run-editor',
  imports: [CodeEditor, DecimalPipe, FaIconComponent, KeyValuePipe, RunChart],
  host: { class: 'flex min-h-0 flex-1 flex-col overflow-hidden' },
  templateUrl: './run-editor.html',
})
export class RunEditor {
  protected readonly store = inject(RunStore);
  protected readonly faPlay = faPlay;
  protected readonly faStop = faStop;
  protected readonly faXmark = faXmark;

  protected readonly button =
    'flex items-center gap-2 rounded px-3 py-1 text-sm disabled:opacity-50';

  protected seconds(ms: number): string {
    return (ms / 1000).toFixed(1) + ' s';
  }
}
