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
import { GitRemote } from '../../../../models/messages';
import { GitStore } from '../../../../services/git-store';

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
  templateUrl: './git-remotes.html',
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
