import { DialogRef } from '@angular/cdk/dialog';
import { Component, computed, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faCodeBranch, faFolderOpen, faXmark } from '@fortawesome/free-solid-svg-icons';
import { repositoryName } from '../../models/repository-name';
import { WorkspaceStore } from '../../services/workspace-store';

/**
 * Asks for a git repository's URL and the folder to clone it into, clones it and opens the clone
 * as the workspace. Opened with `openCloneDialog`; closes with `true` once the
 * clone is open, and stays open showing the reason when cloning fails.
 */
@Component({
  selector: 'app-clone-dialog',
  imports: [FaIconComponent],
  templateUrl: './clone-dialog.html',
})
export class CloneDialog {
  private readonly store = inject(WorkspaceStore);

  protected readonly ref = inject(DialogRef<boolean>);
  protected readonly url = signal('');
  protected readonly parentFolder = signal('');
  /** The folder name the user typed; `null` while it follows the URL. */
  private readonly typedName = signal<string | null>(null);
  protected readonly name = computed(() => this.typedName() ?? repositoryName(this.url()));
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly canClone = computed(
    () => !!this.url().trim() && !!this.parentFolder() && !!this.name().trim() && !this.busy(),
  );
  /** Where the clone goes, for the user to check before cloning. */
  protected readonly destination = computed(() => {
    const parent = this.parentFolder();
    const name = this.name().trim();
    if (!parent || !name) return '';
    const separator = parent.includes('\\') && !parent.includes('/') ? '\\' : '/';
    return parent.endsWith(separator) ? parent + name : parent + separator + name;
  });
  protected readonly faXmark = faXmark;
  protected readonly faFolderOpen = faFolderOpen;
  protected readonly faCodeBranch = faCodeBranch;

  protected setName(name: string): void {
    this.typedName.set(name);
  }

  protected async browse(): Promise<void> {
    const folder = await this.store.pickCloneFolder();
    if (folder) this.parentFolder.set(folder);
  }

  protected async clone(): Promise<void> {
    if (!this.canClone()) return;
    this.busy.set(true);
    this.error.set(null);
    const result = await this.store.cloneRepository(
      this.url().trim(),
      this.parentFolder(),
      this.name().trim(),
    );
    this.busy.set(false);
    if (result === null) this.ref.close(true);
    else if (result !== false) this.error.set(result);
  }

  protected close(): void {
    if (!this.busy()) this.ref.close();
  }
}
