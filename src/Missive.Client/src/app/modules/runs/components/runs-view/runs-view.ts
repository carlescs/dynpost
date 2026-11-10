import { Component, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faArrowsRotate,
  faCircleExclamation,
  faPlus,
  faTrash,
} from '@fortawesome/free-solid-svg-icons';
import { RunStore } from '../../../../services/run-store';
import { WorkspaceStore } from '../../../../services/workspace-store';

/** Lists the workspace's run plans (`*.run.yaml`); click one to edit and run it. */
@Component({
  selector: 'app-runs-view',
  imports: [FaIconComponent],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  templateUrl: './runs-view.html',
})
export class RunsView {
  protected readonly store = inject(RunStore);
  protected readonly workspace = inject(WorkspaceStore);
  protected readonly faPlus = faPlus;
  protected readonly faArrowsRotate = faArrowsRotate;
  protected readonly faCircleExclamation = faCircleExclamation;
  protected readonly faTrash = faTrash;
  protected readonly headerButton =
    'flex size-6 items-center justify-center rounded text-slate-500 hover:bg-slate-200 hover:text-slate-800';
}
