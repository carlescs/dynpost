import { Component, inject } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faGlobe,
  faArrowsRotate,
  faCircleCheck,
  faCircleExclamation,
  faClone,
  faPen,
  faPlus,
} from '@fortawesome/free-solid-svg-icons';
import { SharingToggle } from '../../../../components/sharing-toggle/sharing-toggle';
import { EnvironmentStore, GLOBALS_ID } from '../../../../services/environment-store';
import { WorkspaceStore } from '../../../../services/workspace-store';
import { environmentColor } from '../../../../models/environment-colors';

/** Lists the workspace's environments: click one to send requests with it, or its pen icon to edit it. */
@Component({
  selector: 'app-environments-view',
  imports: [FaIconComponent, SharingToggle],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  templateUrl: './environments-view.html',
})
export class EnvironmentsView {
  protected readonly store = inject(EnvironmentStore);
  protected readonly workspace = inject(WorkspaceStore);
  protected readonly faPlus = faPlus;
  protected readonly faPen = faPen;
  protected readonly faClone = faClone;
  protected readonly faGlobe = faGlobe;
  protected readonly globalsId = GLOBALS_ID;
  protected readonly faArrowsRotate = faArrowsRotate;
  protected readonly faCircleCheck = faCircleCheck;
  protected readonly faCircleExclamation = faCircleExclamation;
  protected readonly example = '{{variable}}';
  protected readonly colorOf = environmentColor;

  protected readonly headerButton =
    'flex size-6 items-center justify-center rounded text-slate-500 hover:bg-slate-200 hover:text-slate-800';
  /** An empty radio mark, the size of the check icon. */
  protected readonly ring = 'inline-block size-[1em] rounded-full border-2 border-current';
}
