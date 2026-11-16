import { Component, computed, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faChevronDown,
  faChevronRight,
  faCircleExclamation,
  faFolderOpen,
  faFolderTree,
  faPlus,
  faSpinner,
  faTrash,
  faXmark,
} from '@fortawesome/free-solid-svg-icons';
import { GitWorktree } from '../../../../models/messages';
import { GitStore } from '../../../../services/git-store';
import { Dialogs } from '../../../../services/dialogs';

/** The new-worktree form's values. */
interface WorktreeForm {
  /** Whether to create a new branch rather than check out an existing one. */
  createBranch: boolean;
  branch: string;
  parentFolder: string;
  name: string;
  /** Whether the user typed the folder name, so it no longer follows the branch. */
  nameEdited: boolean;
}

/**
 * The repository's worktrees, in a collapsible section of the Source control view: check out a
 * branch in a folder of its own to work on it next to this one, open a worktree as the workspace,
 * or remove one.
 */
@Component({
  selector: 'app-git-worktrees',
  imports: [FaIconComponent],
  host: { class: 'block shrink-0 border-t border-slate-200' },
  templateUrl: './git-worktrees.html',
})
export class GitWorktrees {
  protected readonly git = inject(GitStore);
  private readonly dialogs = inject(Dialogs);
  protected readonly expanded = signal(false);
  protected readonly form = signal<WorktreeForm | null>(null);
  /** The worktree an operation is running for, to show its spinner. */
  protected readonly pending = signal<string | null>(null);

  /** Local branches that no worktree has checked out, so a new one can. */
  protected readonly freeBranches = computed(() => {
    const taken = new Set((this.git.worktrees() ?? []).map((w) => w.branch));
    return (this.git.branches() ?? [])
      .filter((b) => !b.remote && !b.current && !taken.has(b.name))
      .map((b) => b.name);
  });

  protected readonly faChevronDown = faChevronDown;
  protected readonly faChevronRight = faChevronRight;
  protected readonly faCircleExclamation = faCircleExclamation;
  protected readonly faFolderOpen = faFolderOpen;
  protected readonly faFolderTree = faFolderTree;
  protected readonly faPlus = faPlus;
  protected readonly faSpinner = faSpinner;
  protected readonly faTrash = faTrash;
  protected readonly faXmark = faXmark;

  protected toggle(): void {
    this.expanded.update((open) => !open);
    if (this.expanded()) void this.git.loadWorktrees();
  }

  protected async startCreate(): Promise<void> {
    if (!this.expanded()) this.toggle();
    // The branch list fills the form's choices.
    if (this.git.branches() === null) await this.git.loadBranches();
    if (this.git.worktrees() === null) await this.git.loadWorktrees();
    const branch = this.freeBranches()[0] ?? '';
    this.form.set({
      createBranch: !branch,
      branch,
      parentFolder: parentOf(this.mainWorktree()?.path ?? ''),
      name: this.suggestedName(branch),
      nameEdited: false,
    });
  }

  /** Updates the form; the folder name follows the branch until the user edits it. */
  protected patch(patch: Partial<WorktreeForm>): void {
    this.form.update((f) => {
      if (!f) return f;
      const next = { ...f, ...patch };
      if (!next.nameEdited) next.name = this.suggestedName(next.branch);
      return next;
    });
  }

  protected setMode(createBranch: boolean): void {
    this.patch({ createBranch, branch: createBranch ? '' : (this.freeBranches()[0] ?? '') });
  }

  protected async browse(): Promise<void> {
    const folder = await this.git.pickWorktreeFolder();
    if (folder) this.patch({ parentFolder: folder });
  }

  protected cancel(): void {
    this.form.set(null);
    if (this.git.failedOperation() === 'worktree') this.git.dismissError();
  }

  protected canCreate(f: WorktreeForm): boolean {
    return !!f.branch.trim() && !!f.parentFolder.trim() && !!f.name.trim();
  }

  protected async create(): Promise<void> {
    const f = this.form();
    if (!f || !this.canCreate(f)) return;
    const created = await this.git.addWorktree(f.parentFolder, f.name, f.branch, f.createBranch);
    if (!created) return;
    this.form.set(null);
    const open = await this.dialogs.confirm(
      `Open the new worktree for '${created.branch}' now? It replaces this workspace in the window.`,
      { title: 'Worktree created', confirmLabel: 'Open it', cancelLabel: 'Not now' },
    );
    if (open) await this.git.openWorktree(created);
  }

  protected name(worktree: GitWorktree): string {
    return lastSegment(worktree.path);
  }

  protected async open(worktree: GitWorktree): Promise<void> {
    await this.track(worktree.path, async () => {
      await this.git.openWorktree(worktree);
      return true;
    });
  }

  protected async remove(worktree: GitWorktree): Promise<void> {
    const question = worktree.prunable
      ? `Forget the worktree at '${worktree.path}'? Its folder is already gone.`
      : `Remove the worktree at '${worktree.path}'? Its folder is deleted` +
        (worktree.branch
          ? `; the branch '${worktree.branch}' and its commits stay in the repository.`
          : '.');
    const ok = await this.dialogs.confirm(question, {
      title: 'Remove worktree?',
      confirmLabel: worktree.prunable ? 'Forget' : 'Remove',
      danger: true,
    });
    if (!ok) return;
    await this.track(worktree.path, async () => {
      if (await this.git.removeWorktree(worktree)) return true;
      if (this.git.error()?.code !== 'git.worktree_has_changes') return false;
      // Git kept it because files would be lost: ask again, spelling that out.
      if (
        !(await this.dialogs.confirm(
          `The worktree at '${worktree.path}' has uncommitted changes or new files. Removing it ` +
            'anyway deletes them. Remove it anyway?',
          { title: 'Worktree has changes', confirmLabel: 'Remove anyway', danger: true },
        ))
      ) {
        this.git.dismissError();
        return false;
      }
      return this.git.removeWorktree(worktree, true);
    });
  }

  private mainWorktree(): GitWorktree | undefined {
    const worktrees = this.git.worktrees() ?? [];
    return worktrees.find((w) => w.main) ?? worktrees[0];
  }

  /** `api-feature-login` for the branch `feature/login` of the repository folder `api`. */
  private suggestedName(branch: string): string {
    const repository = lastSegment(this.mainWorktree()?.path ?? '') || 'worktree';
    const slug = branch
      .trim()
      .replace(/[^A-Za-z0-9._-]+/g, '-')
      .replace(/^[-.]+|-+$/g, '');
    return slug ? `${repository}-${slug}` : '';
  }

  private async track(path: string, action: () => Promise<boolean>): Promise<void> {
    this.pending.set(path);
    try {
      await action();
    } finally {
      this.pending.set(null);
    }
  }
}

function lastSegment(path: string): string {
  return (
    path
      .replace(/[\\/]+$/, '')
      .split(/[\\/]/)
      .pop() ?? ''
  );
}

/** The folder that contains `path`, keeping its separators (`/` or Windows `\`). */
function parentOf(path: string): string {
  const trimmed = path.replace(/[\\/]+$/, '');
  const cut = Math.max(trimmed.lastIndexOf('/'), trimmed.lastIndexOf('\\'));
  if (cut < 0) return '';
  // Keep the root of `/repo` or `C:\repo`.
  return cut === 0 || /^[A-Za-z]:$/.test(trimmed.slice(0, cut))
    ? trimmed.slice(0, cut + 1)
    : trimmed.slice(0, cut);
}
