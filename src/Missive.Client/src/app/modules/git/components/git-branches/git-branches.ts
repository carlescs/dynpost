import { Component, computed, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faChevronDown,
  faChevronRight,
  faCircleExclamation,
  faCodeBranch,
  faCodeMerge,
  faDownload,
  faPlus,
  faRightLeft,
  faSpinner,
  faTrash,
  faXmark,
} from '@fortawesome/free-solid-svg-icons';
import { GitBranch } from '../../../../models/messages';
import { GitStore } from '../../../../services/git-store';
import { Dialogs } from '../../../../services/dialogs';
import { DateFormatStore } from '../../../../services/date-format-store';

/** The new-branch form's values. */
interface BranchForm {
  name: string;
  switchTo: boolean;
}

/**
 * The repository's branches, in a collapsible section of the Source control view: switch to a
 * local branch, create one from the current commit, check out a remote branch as a local
 * tracking branch, merge a local or remote branch into the current one, or delete a local
 * branch or a branch on its remote.
 */
@Component({
  selector: 'app-git-branches',
  imports: [FaIconComponent],
  host: { class: 'block shrink-0 border-t border-slate-200' },
  templateUrl: './git-branches.html',
})
export class GitBranches {
  protected readonly dates = inject(DateFormatStore);
  protected readonly git = inject(GitStore);
  private readonly dialogs = inject(Dialogs);
  protected readonly expanded = signal(false);
  protected readonly form = signal<BranchForm | null>(null);
  /** The branch an operation is running for, to show its spinner. */
  protected readonly pending = signal<string | null>(null);

  protected readonly local = computed(() => (this.git.branches() ?? []).filter((b) => !b.remote));
  protected readonly remote = computed(() => (this.git.branches() ?? []).filter((b) => b.remote));

  /** Why nothing can be merged into the current branch right now, or `null` when it can. */
  protected readonly mergeBlocker = computed(() => {
    const status = this.git.status();
    if (!status) return 'The git status is not loaded yet';
    if (!status.branch) return 'Check out a branch to merge into';
    if (status.unborn) return 'The current branch has no commits to merge into yet';
    if (status.merging) return 'Complete or abort the merge in progress first';
    if (status.changes.length) return 'Commit or discard your changes before merging';
    return null;
  });

  protected readonly faChevronDown = faChevronDown;
  protected readonly faChevronRight = faChevronRight;
  protected readonly faCircleExclamation = faCircleExclamation;
  protected readonly faCodeBranch = faCodeBranch;
  protected readonly faCodeMerge = faCodeMerge;
  protected readonly faDownload = faDownload;
  protected readonly faPlus = faPlus;
  protected readonly faRightLeft = faRightLeft;
  protected readonly faSpinner = faSpinner;
  protected readonly faTrash = faTrash;
  protected readonly faXmark = faXmark;

  protected toggle(): void {
    this.expanded.update((open) => !open);
    if (this.expanded()) void this.git.loadBranches();
  }

  protected startCreate(): void {
    if (!this.expanded()) this.toggle();
    this.form.set({ name: '', switchTo: true });
  }

  protected patch(patch: Partial<BranchForm>): void {
    this.form.update((f) => (f ? { ...f, ...patch } : f));
  }

  protected cancel(): void {
    this.form.set(null);
    if (this.git.failedOperation() === 'branch') this.git.dismissError();
  }

  protected async create(): Promise<void> {
    const f = this.form();
    if (!f || !f.name.trim()) return;
    if (await this.git.createBranch(f.name, f.switchTo)) this.form.set(null);
  }

  protected async switchTo(name: string): Promise<void> {
    await this.track(name, () => this.git.switchBranch(name));
  }

  protected async checkout(branch: GitBranch): Promise<void> {
    await this.track(branch.name, () => this.git.checkoutRemoteBranch(branch));
  }

  protected mergeTitle(branch: GitBranch): string {
    return (
      this.mergeBlocker() ??
      `Merge ${branch.name} into ${this.git.status()?.branch ?? 'the current branch'}`
    );
  }

  protected async merge(branch: GitBranch): Promise<void> {
    const into = this.git.status()?.branch;
    const ok = await this.dialogs.confirm(
      `Merge '${branch.name}' into the current branch '${into}'?`,
      { title: 'Merge branch?', confirmLabel: 'Merge' },
    );
    if (!ok) return;
    await this.track(branch.name, () => this.git.mergeBranch(branch));
  }

  protected async remove(branch: GitBranch): Promise<void> {
    const ok = await this.dialogs.confirm(
      `Delete the local branch '${branch.name}'? Nothing is deleted on the remote.`,
      { title: 'Delete branch?', confirmLabel: 'Delete', danger: true },
    );
    if (!ok) return;
    await this.track(branch.name, async () => {
      if (await this.git.deleteBranch(branch.name)) return true;
      if (this.git.error()?.code !== 'git.branch_not_merged') return false;
      // Git kept it because its commits would be lost: ask again, spelling that out.
      if (
        !(await this.dialogs.confirm(
          `'${branch.name}' has commits that are not merged into any other branch or its ` +
            'upstream. Deleting it anyway loses those commits. Delete it anyway?',
          { title: 'Branch not merged', confirmLabel: 'Delete anyway', danger: true },
        ))
      ) {
        this.git.dismissError();
        return false;
      }
      return this.git.deleteBranch(branch.name, true);
    });
  }

  protected async removeRemote(branch: GitBranch): Promise<void> {
    const local = branch.localName ?? branch.name;
    const tracked = branch.trackedBy
      ? ` The local branch '${branch.trackedBy}' is kept, but its upstream will be gone.`
      : '';
    if (
      !(await this.dialogs.confirm(
        `Delete the branch '${local}' on the remote '${branch.remote}'? It is deleted for ` +
          `everyone who uses the remote, and can't be undone from Missive.${tracked}`,
        { title: 'Delete remote branch?', confirmLabel: 'Delete', danger: true },
      ))
    ) {
      return;
    }
    await this.track(branch.name, () => this.git.deleteRemoteBranch(branch.name));
  }

  private async track(name: string, action: () => Promise<boolean>): Promise<void> {
    this.pending.set(name);
    try {
      await action();
    } finally {
      this.pending.set(null);
    }
  }
}
