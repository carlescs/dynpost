import { Component, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faArrowsRotate,
  faCircleCheck,
  faCircleExclamation,
  faPen,
  faPlus,
} from '@fortawesome/free-solid-svg-icons';
import { EnvironmentStore } from '../../../../services/environment-store';
import { WorkspaceStore } from '../../../../services/workspace-store';

/** Lists the workspace's environments: pick the one requests are sent with, or open one to edit. */
@Component({
  selector: 'app-environments-view',
  imports: [FaIconComponent],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  templateUrl: './environments-view.html',
})
export class EnvironmentsView {
  protected readonly store = inject(EnvironmentStore);
  protected readonly workspace = inject(WorkspaceStore);
  protected readonly faPen = faPen;
  protected readonly faPlus = faPlus;
  protected readonly faArrowsRotate = faArrowsRotate;
  protected readonly faCircleCheck = faCircleCheck;
  protected readonly faCircleExclamation = faCircleExclamation;
  protected readonly example = '{{variable}}';

  protected readonly headerButton =
    'flex size-6 items-center justify-center rounded text-slate-500 hover:bg-slate-200 hover:text-slate-800';
  /** An empty radio mark, the size of the check icon. */
  protected readonly ring = 'inline-block size-[1em] rounded-full border-2 border-current';
}
