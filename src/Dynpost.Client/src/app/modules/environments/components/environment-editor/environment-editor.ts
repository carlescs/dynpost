import { Component, computed, inject, input, linkedSignal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faBan,
  faCheck,
  faEye,
  faEyeSlash,
  faFloppyDisk,
  faLock,
  faSpinner,
  faTrash,
  faClone,
} from '@fortawesome/free-solid-svg-icons';
import { EnvironmentVariableDto } from '../../../../models/messages';
import {
  isGlobals,
  EnvironmentDraft,
  EnvironmentStore,
} from '../../../../services/environment-store';
import { CookieJar } from '../cookie-jar/cookie-jar';
import { ENVIRONMENT_COLORS, ENVIRONMENT_COLOR_NAMES } from '../../../../models/environment-colors';
import { ClientCertificates } from '../client-certificates/client-certificates';
import { CapturedValues } from '../captured-values/captured-values';

const BLANK: EnvironmentVariableDto = { name: '', value: '', secret: false };

/** Edits an environment open in a tab: its name, variables and cookie jar, and shows its captured values. */
@Component({
  selector: 'app-environment-editor',
  imports: [FaIconComponent, CookieJar, CapturedValues, ClientCertificates],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  templateUrl: './environment-editor.html',
})
export class EnvironmentEditor {
  protected readonly store = inject(EnvironmentStore);
  readonly draft = input.required<EnvironmentDraft>();

  /** Secret values are hidden again when another environment's tab is shown. */
  protected readonly showSecrets = linkedSignal({
    source: () => this.draft().key,
    computation: () => false,
  });
  protected readonly rows = computed(() => [...this.draft().variables, BLANK]);
  protected readonly hasSecrets = computed(() => this.draft().variables.some((v) => v.secret));
  /** Whether the tab edits the workspace-global variables rather than an environment. */
  protected readonly globals = computed(() => isGlobals(this.draft()));
  protected readonly isActive = computed(() => {
    const id = this.draft().id;
    return !!id && id === this.store.activeId();
  });

  protected readonly example = '{{baseUrl}}';
  protected readonly buttonClass =
    'flex items-center gap-2 rounded border border-slate-300 px-3 py-1 text-sm hover:bg-slate-100 disabled:opacity-50 disabled:hover:bg-transparent';
  protected readonly colors = ENVIRONMENT_COLORS;
  protected readonly colorNames = ENVIRONMENT_COLOR_NAMES;
  /** A color choice: a round button, outlined when chosen. */
  protected readonly swatch =
    'flex size-5 items-center justify-center rounded-full border-2 outline-offset-2 outline-sky-500 hover:scale-110';
  protected readonly faBan = faBan;
  protected readonly faCheck = faCheck;
  protected readonly faEye = faEye;
  protected readonly faEyeSlash = faEyeSlash;
  protected readonly faFloppyDisk = faFloppyDisk;
  protected readonly faLock = faLock;
  protected readonly faSpinner = faSpinner;
  protected readonly faTrash = faTrash;
  protected readonly faClone = faClone;

  protected patch(index: number, patch: Partial<EnvironmentVariableDto>): void {
    const draft = this.draft();
    const variables = [...draft.variables];
    variables[index] = { ...(variables[index] ?? BLANK), ...patch };
    this.store.update(draft.key, { variables });
  }

  protected remove(index: number): void {
    const draft = this.draft();
    this.store.update(draft.key, {
      variables: draft.variables.filter((_, i) => i !== index),
    });
  }

  /** Whether `{{name}}` would be recognized as a reference; names can't have spaces or braces. */
  protected validName(name: string): boolean {
    return !/[{}\s]/.test(name.trim());
  }
}
