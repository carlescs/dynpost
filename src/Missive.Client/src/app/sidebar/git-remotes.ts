import { NgTemplateOutlet } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faChevronDown,
  faChevronRight,
  faCircleExclamation,
  faPen,
  faPlus,
  faSpinner,
  faTrash,
  faXmark,
} from '@fortawesome/free-solid-svg-icons';
import { GitRemote } from '../bridge/messages';
import { GitStore } from '../git/git-store';

/** The remote being edited (`null` name: a new one) and the form's values. */
interface RemoteForm {
  original: string | null;
  name: string;
  url: string;
}

/**
 * The repository's remotes, in a collapsible section of the Source control view: add one,
 * rename it or change its URL, or remove it. Nothing is fetched or pushed here.
 */
@Component({
  selector: 'app-git-remotes',
  imports: [NgTemplateOutlet, FaIconComponent],
  host: { class: 'block shrink-0 border-t border-slate-200' },
  template: `
    <div class="flex h-8 items-center gap-1 px-3">
      <button
        type="button"
        class="flex min-w-0 flex-1 items-center gap-1.5 text-left text-xs font-semibold tracking-wide text-slate-500 uppercase hover:text-slate-800"
        [attr.aria-expanded]="expanded()"
        (click)="expanded.set(!expanded())"
      >
        <fa-icon class="w-3" [icon]="expanded() ? faChevronDown : faChevronRight" />
        Remotes ({{ git.remotes()?.length ?? 0 }})
      </button>
      <button
        type="button"
        class="flex size-6 items-center justify-center rounded text-slate-500 enabled:hover:bg-slate-200 enabled:hover:text-slate-800 disabled:opacity-50"
        title="Add a remote"
        aria-label="Add a remote"
        [disabled]="git.busy() !== null"
        (click)="startAdd()"
      >
        <fa-icon [icon]="faPlus" />
      </button>
    </div>

    @if (expanded()) {
      <div class="max-h-72 overflow-y-auto pb-2 text-sm">
        @if (git.failedOperation() === 'remote' && git.error(); as error) {
          <div
            class="mx-3 mb-2 flex items-start gap-2 rounded border border-rose-200 bg-rose-50 px-2 py-1.5 text-xs text-rose-800"
            role="alert"
          >
            <fa-icon class="mt-0.5" [icon]="faCircleExclamation" />
            <p class="min-w-0 flex-1 break-words whitespace-pre-line">{{ error.message }}</p>
            <button
              type="button"
              class="shrink-0 text-rose-500 hover:text-rose-800"
              title="Dismiss"
              aria-label="Dismiss"
              (click)="git.dismissError()"
            >
              <fa-icon [icon]="faXmark" />
            </button>
          </div>
        }

        <ul>
          @for (remote of git.remotes() ?? []; track remote.name) {
            @if (form()?.original === remote.name) {
              <li class="px-3 py-1">
                <ng-container *ngTemplateOutlet="editor" />
              </li>
            } @else {
              <li class="group flex items-start gap-2 px-3 py-1 hover:bg-slate-200">
                <div class="min-w-0 flex-1">
                  <p class="truncate font-medium">{{ remote.name }}</p>
                  <p class="truncate font-mono text-xs text-slate-500" [title]="remote.fetchUrl">
                    {{ remote.fetchUrl }}
                  </p>
                  @if (remote.pushUrl !== remote.fetchUrl) {
                    <p
                      class="truncate font-mono text-xs text-slate-500"
                      [title]="'Push URL: ' + remote.pushUrl"
                    >
                      push: {{ remote.pushUrl }}
                    </p>
                  }
                </div>
                <button
                  type="button"
                  class="flex size-6 shrink-0 items-center justify-center rounded text-slate-500 enabled:hover:bg-slate-300 enabled:hover:text-slate-800 disabled:opacity-50"
                  [title]="'Edit ' + remote.name"
                  [attr.aria-label]="'Edit ' + remote.name"
                  [disabled]="git.busy() !== null"
                  (click)="startEdit(remote)"
                >
                  <fa-icon [icon]="faPen" />
                </button>
                <button
                  type="button"
                  class="flex size-6 shrink-0 items-center justify-center rounded text-slate-500 enabled:hover:bg-rose-100 enabled:hover:text-rose-700 disabled:opacity-50"
                  [title]="'Remove ' + remote.name"
                  [attr.aria-label]="'Remove ' + remote.name"
                  [disabled]="git.busy() !== null"
                  (click)="remove(remote)"
                >
                  <fa-icon
                    [icon]="removing() === remote.name ? faSpinner : faTrash"
                    [animation]="removing() === remote.name ? 'spin' : undefined"
                  />
                </button>
              </li>
            }
          } @empty {
            @if (!form()) {
              <li class="px-3 py-1 text-slate-500">
                No remotes. Add one to pull and push the workspace.
              </li>
            }
          }
          @if (form()?.original === null) {
            <li class="px-3 py-1">
              <ng-container *ngTemplateOutlet="editor" />
            </li>
          }
        </ul>
      </div>
    }

    <ng-template #editor>
      @if (form(); as f) {
        <form class="space-y-1.5" (submit)="$event.preventDefault(); save()">
          <input
            class="block w-full rounded border border-slate-300 bg-white px-2 py-1 text-sm focus:border-sky-500 focus:outline-none"
            placeholder="Name, e.g. origin"
            aria-label="Remote name"
            spellcheck="false"
            autocomplete="off"
            [value]="f.name"
            (input)="patch({ name: $any($event.target).value })"
            (keydown.escape)="cancel()"
          />
          <input
            class="block w-full rounded border border-slate-300 bg-white px-2 py-1 font-mono text-xs focus:border-sky-500 focus:outline-none"
            placeholder="https://github.com/you/repo.git"
            aria-label="Remote URL"
            spellcheck="false"
            autocomplete="off"
            [value]="f.url"
            (input)="patch({ url: $any($event.target).value })"
            (keydown.escape)="cancel()"
          />
          <div class="flex gap-2">
            <button
              type="submit"
              class="flex flex-1 items-center justify-center gap-1.5 rounded bg-sky-600 px-2 py-0.5 text-sm text-white enabled:hover:bg-sky-700 disabled:cursor-not-allowed disabled:opacity-50"
              [disabled]="git.busy() !== null || !f.name.trim() || !f.url.trim()"
            >
              @if (git.busy() === 'remote') {
                <fa-icon [icon]="faSpinner" animation="spin" />
              }
              {{ f.original === null ? 'Add remote' : 'Save' }}
            </button>
            <button
              type="button"
              class="flex-1 rounded border border-slate-300 bg-white px-2 py-0.5 text-sm enabled:hover:bg-slate-100"
              (click)="cancel()"
            >
              Cancel
            </button>
          </div>
        </form>
      }
    </ng-template>
  `,
})
export class GitRemotes {
  protected readonly git = inject(GitStore);
  protected readonly expanded = signal(false);
  protected readonly form = signal<RemoteForm | null>(null);
  /** The remote being removed, to show its spinner. */
  protected readonly removing = signal<string | null>(null);

  protected readonly faChevronDown = faChevronDown;
  protected readonly faChevronRight = faChevronRight;
  protected readonly faCircleExclamation = faCircleExclamation;
  protected readonly faPen = faPen;
  protected readonly faPlus = faPlus;
  protected readonly faSpinner = faSpinner;
  protected readonly faTrash = faTrash;
  protected readonly faXmark = faXmark;

  protected startAdd(): void {
    this.expanded.set(true);
    // Suggest origin for the first remote: it is what Publish pushes to.
    this.form.set({ original: null, name: this.git.remotes()?.length ? '' : 'origin', url: '' });
  }

  protected startEdit(remote: GitRemote): void {
    this.form.set({ original: remote.name, name: remote.name, url: remote.fetchUrl });
  }

  protected patch(patch: Partial<RemoteForm>): void {
    this.form.update((f) => (f ? { ...f, ...patch } : f));
  }

  protected cancel(): void {
    this.form.set(null);
    if (this.git.failedOperation() === 'remote') this.git.dismissError();
  }

  protected async save(): Promise<void> {
    const f = this.form();
    if (!f || !f.name.trim() || !f.url.trim()) return;
    const ok =
      f.original === null
        ? await this.git.addRemote(f.name, f.url)
        : await this.git.updateRemote(f.original, f.name, f.url);
    if (ok) this.form.set(null);
  }

  protected async remove(remote: GitRemote): Promise<void> {
    if (
      !confirm(
        `Remove the remote '${remote.name}'? Its remote-tracking branches are deleted and branches ` +
          'that track it lose their upstream. Nothing is deleted on the remote itself.',
      )
    ) {
      return;
    }
    this.removing.set(remote.name);
    try {
      await this.git.removeRemote(remote.name);
    } finally {
      this.removing.set(null);
    }
  }
}
