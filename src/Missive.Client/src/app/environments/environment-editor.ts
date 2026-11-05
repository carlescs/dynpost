import { Component, computed, inject, input, linkedSignal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faCheck,
  faEye,
  faEyeSlash,
  faFloppyDisk,
  faLock,
  faSpinner,
  faTrash,
} from '@fortawesome/free-solid-svg-icons';
import { EnvironmentVariableDto } from '../bridge/messages';
import { EnvironmentDraft, EnvironmentStore } from './environment-store';

const BLANK: EnvironmentVariableDto = { name: '', value: '', secret: false };

/** Edits an environment open in a tab: its name and variables. */
@Component({
  selector: 'app-environment-editor',
  imports: [FaIconComponent],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    @if (draft(); as draft) {
      <div class="flex items-center gap-2 border-b border-slate-200 px-4 py-3">
        <span class="text-xs font-semibold tracking-wide text-slate-500 uppercase">
          Environment
        </span>
        <input
          class="min-w-0 flex-1 rounded px-1 text-lg font-semibold hover:bg-slate-100 focus:bg-white focus:ring-1 focus:ring-sky-500 focus:outline-none"
          aria-label="Environment name"
          [value]="draft.name"
          (input)="store.update(draft.key, { name: $any($event.target).value })"
        />
        <span class="truncate text-xs text-slate-400">{{
          draft.id ? 'environments/' + draft.id : 'New environment — not saved yet'
        }}</span>
        @if (draft.id) {
          <button
            type="button"
            [class]="buttonClass"
            [disabled]="isActive()"
            [title]="
              isActive()
                ? 'Requests are sent with this environment'
                : 'Send requests with this environment'
            "
            (click)="store.select(draft.id)"
          >
            <fa-icon [icon]="faCheck" /> {{ isActive() ? 'Active' : 'Use' }}
          </button>
        }
        <button
          type="button"
          [class]="buttonClass"
          title="Save environment (Ctrl+S)"
          [disabled]="!draft.dirty || draft.saving"
          (click)="store.save(draft.key)"
        >
          <fa-icon
            [icon]="draft.saving ? faSpinner : faFloppyDisk"
            [animation]="draft.saving ? 'spin' : undefined"
          />
          Save
        </button>
        <button
          type="button"
          [class]="buttonClass"
          title="Delete environment"
          (click)="store.deleteDraft(draft.key)"
        >
          <fa-icon [icon]="faTrash" />
        </button>
      </div>

      <section class="min-h-0 flex-1 overflow-y-auto px-4 py-3">
        <table class="w-full table-fixed border-collapse text-sm">
          <thead>
            <tr class="text-left text-xs text-slate-500">
              <th class="w-1/3 px-2 py-1 font-medium">Variable</th>
              <th class="px-2 py-1 font-medium">Value</th>
              <th class="w-20 px-2 py-1 text-center font-medium">Secret</th>
              <th class="w-8"></th>
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track $index; let i = $index; let last = $last) {
              <tr class="border-t border-slate-200">
                <td>
                  <input
                    class="w-full px-2 py-1 font-mono focus:bg-sky-50 focus:outline-none"
                    placeholder="Variable"
                    spellcheck="false"
                    [attr.aria-label]="'Variable name ' + (i + 1)"
                    [class.text-rose-700]="!validName(row.name)"
                    [value]="row.name"
                    (input)="patch(i, { name: $any($event.target).value })"
                  />
                </td>
                <td>
                  <input
                    class="w-full px-2 py-1 font-mono focus:bg-sky-50 focus:outline-none"
                    placeholder="Value"
                    spellcheck="false"
                    autocomplete="off"
                    [attr.aria-label]="'Value of ' + (row.name || 'variable ' + (i + 1))"
                    [type]="row.secret && !showSecrets() ? 'password' : 'text'"
                    [value]="row.value"
                    (input)="patch(i, { value: $any($event.target).value })"
                  />
                </td>
                <td class="text-center">
                  <input
                    type="checkbox"
                    [attr.aria-label]="'Keep ' + (row.name || 'this variable') + ' secret'"
                    [checked]="row.secret"
                    (change)="patch(i, { secret: $any($event.target).checked })"
                  />
                </td>
                <td class="text-center">
                  @if (!last) {
                    <button
                      type="button"
                      class="text-slate-400 hover:text-rose-600"
                      aria-label="Remove variable"
                      (click)="remove(i)"
                    >
                      <fa-icon [icon]="faTrash" />
                    </button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>

        <div class="mt-3 flex flex-col gap-2 text-xs text-slate-500">
          @if (hasSecrets()) {
            <button
              type="button"
              class="flex items-center gap-1.5 self-start text-sky-700 hover:underline"
              (click)="showSecrets.set(!showSecrets())"
            >
              <fa-icon [icon]="showSecrets() ? faEyeSlash : faEye" />
              {{ showSecrets() ? 'Hide secret values' : 'Show secret values' }}
            </button>
          }
          <p>
            Use a variable as <code>{{ example }}</code> in a request's URL, parameters, headers,
            auth or body. Values can use other variables too.
          </p>
          <p class="flex items-start gap-1.5">
            <fa-icon class="mt-0.5" [icon]="faLock" />
            <span>
              Secret values stay on this computer, in <code>.missive/secrets.yaml</code>, and are
              never committed: the environment file only lists their names. Teammates enter their
              own values after pulling.
            </span>
          </p>
        </div>
      </section>
    }
  `,
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
  protected readonly isActive = computed(() => {
    const id = this.draft().id;
    return !!id && id === this.store.activeId();
  });

  protected readonly example = '{{baseUrl}}';
  protected readonly buttonClass =
    'flex items-center gap-2 rounded border border-slate-300 px-3 py-1 text-sm hover:bg-slate-100 disabled:opacity-50 disabled:hover:bg-transparent';
  protected readonly faCheck = faCheck;
  protected readonly faEye = faEye;
  protected readonly faEyeSlash = faEyeSlash;
  protected readonly faFloppyDisk = faFloppyDisk;
  protected readonly faLock = faLock;
  protected readonly faSpinner = faSpinner;
  protected readonly faTrash = faTrash;

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
