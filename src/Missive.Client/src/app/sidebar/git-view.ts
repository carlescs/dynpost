import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faArrowDown,
  faArrowUp,
  faBan,
  faCheck,
  faCircleExclamation,
  faChevronDown,
  faChevronRight,
  faCodeBranch,
  faFolder,
  faFolderOpen,
  faListUl,
  faRotateLeft,
  faSitemap,
  faRotate,
  faSpinner,
  faTriangleExclamation,
  faXmark,
} from '@fortawesome/free-solid-svg-icons';
import { GitChange } from '../bridge/messages';
import { STATUS_BADGES, fileName, folderOf } from '../git/git-format';
import { GitHistoryStore } from '../git/git-history-store';
import { GitOperation, GitStore } from '../git/git-store';
import { WorkspaceStore } from '../workspace/workspace-store';
import { GitHistoryList } from './git-history-list';
import { GitRemotes } from './git-remotes';

@Component({
  selector: 'app-git-view',
  imports: [FaIconComponent, GitHistoryList, GitRemotes],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  template: `
    <header class="flex h-9 shrink-0 items-center justify-between px-3">
      <h2 class="text-xs font-semibold tracking-wide text-slate-500 uppercase">Source control</h2>
      @if (workspace.workspace()) {
        <button
          type="button"
          class="flex size-6 items-center justify-center rounded text-slate-500 enabled:hover:bg-slate-200 enabled:hover:text-slate-800 disabled:opacity-50"
          title="Refresh"
          aria-label="Refresh"
          [disabled]="git.busy() !== null"
          (click)="git.refresh()"
        >
          <fa-icon [icon]="faRotate" [animation]="git.busy() === 'refresh' ? 'spin' : undefined" />
        </button>
      }
    </header>

    @if (!git.available) {
      <p class="px-3 text-sm text-slate-500">
        Source control is available in the Missive desktop app.
      </p>
    } @else if (!workspace.workspace()) {
      <div class="space-y-2 px-3 text-sm text-slate-500">
        <p>Open a workspace folder to see its changes and sync it with git.</p>
        <button
          type="button"
          class="flex w-full items-center justify-center gap-2 rounded bg-sky-600 px-3 py-1 text-sm text-white enabled:hover:bg-sky-700 disabled:cursor-not-allowed disabled:opacity-50"
          [disabled]="workspace.opening()"
          (click)="workspace.openFolder()"
        >
          <fa-icon [icon]="faFolderOpen" /> Open folder
        </button>
      </div>
    } @else if (git.notARepository()) {
      <div class="space-y-2 px-3 text-sm text-slate-500">
        <p>This workspace folder is not a git repository yet.</p>
        <p>Initialize one to track changes and sync them with a remote.</p>
        <button
          type="button"
          class="flex w-full items-center justify-center gap-2 rounded bg-sky-600 px-3 py-1 text-sm text-white enabled:hover:bg-sky-700 disabled:cursor-not-allowed disabled:opacity-50"
          [disabled]="git.busy() !== null"
          (click)="git.init()"
        >
          <fa-icon [icon]="busyIcon('init', faCodeBranch)" [animation]="spin('init')" />
          Initialize repository
        </button>
      </div>
    } @else {
      @if (!git.conflict() && git.failedOperation() !== 'remote' && git.error(); as error) {
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

      @if (git.status(); as status) {
        <div class="flex items-center gap-2 px-3 pb-2 text-sm text-slate-700">
          <fa-icon class="text-slate-500" [icon]="faCodeBranch" />
          <span class="truncate font-medium" [title]="status.upstream ?? 'No upstream branch'">{{
            status.branch ?? 'Detached HEAD'
          }}</span>
          @if (status.upstream) {
            <span class="ml-auto flex shrink-0 gap-2 text-xs text-slate-500">
              <span title="Commits to pull"
                ><fa-icon [icon]="faArrowDown" /> {{ status.behind }}</span
              >
              <span title="Commits to push"><fa-icon [icon]="faArrowUp" /> {{ status.ahead }}</span>
            </span>
          } @else {
            <span class="ml-auto shrink-0 text-xs text-slate-400">Not published</span>
          }
        </div>

        @if (status.merging) {
          <div
            class="mx-3 mb-2 space-y-1.5 rounded border border-amber-200 bg-amber-50 px-2 py-1.5 text-xs text-amber-800"
          >
            <p class="flex items-start gap-2">
              <fa-icon class="mt-0.5" [icon]="faTriangleExclamation" />
              @if (git.conflicts().length) {
                <span>
                  Merge in progress: {{ git.conflicts().length }} conflicted file(s). Open each one
                  below to keep a version or mark your own edit as resolved, then commit.
                </span>
              } @else {
                <span>Merge in progress. All conflicts are resolved; commit to complete it.</span>
              }
            </p>
            <button
              type="button"
              class="rounded border border-amber-300 bg-white px-2 py-0.5 text-amber-800 enabled:hover:bg-amber-100 disabled:opacity-50"
              [disabled]="git.busy() !== null"
              (click)="abortMerge()"
            >
              <fa-icon [icon]="busyIcon('abort', faBan)" [animation]="spin('abort')" /> Abort merge
            </button>
          </div>
        }

        <div class="space-y-2 px-3 pb-3">
          <textarea
            class="block w-full resize-none rounded border border-slate-300 bg-white px-2 py-1 text-sm focus:border-sky-500 focus:outline-none"
            rows="3"
            placeholder="Commit message (Ctrl+Enter to commit)"
            aria-label="Commit message"
            [value]="message()"
            (input)="message.set($any($event.target).value)"
            (keydown.control.enter)="commit()"
            (keydown.meta.enter)="commit()"
          ></textarea>
          <button
            type="button"
            class="flex items-center justify-center gap-2 rounded px-3 py-1 text-sm disabled:cursor-not-allowed disabled:opacity-50 w-full bg-sky-600 text-white enabled:hover:bg-sky-700"
            [disabled]="!canCommit()"
            (click)="commit()"
          >
            <fa-icon [icon]="busyIcon('commit', faCheck)" [animation]="spin('commit')" />
            {{ selected().size ? 'Commit selected (' + selected().size + ')' : 'Commit all' }}
          </button>
          <div class="flex gap-2">
            <button
              type="button"
              class="flex items-center justify-center gap-2 rounded px-3 py-1 text-sm disabled:cursor-not-allowed disabled:opacity-50 flex-1 border border-slate-300 bg-white enabled:hover:bg-slate-100"
              [disabled]="git.busy() !== null || !status.upstream"
              [title]="
                status.upstream ? 'Pull from ' + status.upstream : 'The branch has no upstream yet'
              "
              (click)="git.pull()"
            >
              <fa-icon [icon]="busyIcon('pull', faArrowDown)" [animation]="spin('pull')" /> Pull
            </button>
            <button
              type="button"
              class="flex items-center justify-center gap-2 rounded px-3 py-1 text-sm disabled:cursor-not-allowed disabled:opacity-50 flex-1 border border-slate-300 bg-white enabled:hover:bg-slate-100"
              [disabled]="git.busy() !== null || status.branch === undefined"
              [title]="status.upstream ? 'Push to ' + status.upstream : 'Publish the branch'"
              (click)="git.push()"
            >
              <fa-icon [icon]="busyIcon('push', faArrowUp)" [animation]="spin('push')" />
              {{ status.upstream ? 'Push' : 'Publish' }}
            </button>
          </div>
          @if (git.notice(); as notice) {
            <p
              class="text-xs"
              [class]="notice.kind === 'warning' ? 'text-amber-700' : 'text-slate-500'"
              role="status"
            >
              {{ notice.text }}
            </p>
          }
        </div>

        <div class="flex shrink-0 gap-3 border-b border-slate-200 px-3" role="tablist">
          <button
            type="button"
            role="tab"
            class="-mb-px border-b-2 pb-1 text-xs font-semibold tracking-wide uppercase"
            [class]="tabClass('changes')"
            [attr.aria-selected]="tab() === 'changes'"
            (click)="tab.set('changes')"
          >
            Changes ({{ status.changes.length }})
          </button>
          <button
            type="button"
            role="tab"
            class="-mb-px border-b-2 pb-1 text-xs font-semibold tracking-wide uppercase"
            [class]="tabClass('history')"
            [attr.aria-selected]="tab() === 'history'"
            (click)="showHistory()"
          >
            History
          </button>
        </div>
        @if (tab() === 'history') {
          <app-git-history class="min-h-0 flex-1" />
        } @else {
          <div class="flex shrink-0 items-center justify-end gap-1 px-3 pt-1 text-slate-500">
            @if (status.changes.length && !status.merging) {
              <button
                type="button"
                class="mr-auto flex items-center gap-1 rounded px-1.5 py-0.5 text-xs enabled:hover:bg-slate-200 enabled:hover:text-slate-800 disabled:opacity-50"
                [disabled]="git.busy() !== null"
                [title]="selected().size ? 'Discard the selected changes' : 'Discard all changes'"
                (click)="discard(selectedPaths().length ? selectedPaths() : allPaths())"
              >
                <fa-icon [icon]="busyIcon('discard', faRotateLeft)" [animation]="spin('discard')" />
                {{ selectedPaths().length ? 'Discard selected' : 'Discard all' }}
              </button>
            }
            <button
              type="button"
              class="flex size-6 items-center justify-center rounded hover:bg-slate-200"
              [class.bg-slate-200]="viewMode() === 'list'"
              title="View as list"
              aria-label="View as list"
              [attr.aria-pressed]="viewMode() === 'list'"
              (click)="viewMode.set('list')"
            >
              <fa-icon [icon]="faListUl" />
            </button>
            <button
              type="button"
              class="flex size-6 items-center justify-center rounded hover:bg-slate-200"
              [class.bg-slate-200]="viewMode() === 'tree'"
              title="View as tree"
              aria-label="View as tree"
              [attr.aria-pressed]="viewMode() === 'tree'"
              (click)="viewMode.set('tree')"
            >
              <fa-icon [icon]="faSitemap" />
            </button>
          </div>
          <ul class="min-h-0 flex-1 overflow-y-auto py-1 text-sm">
            @for (row of rows(); track row.key) {
              @if (row.change; as change) {
                <li
                  class="group flex items-center gap-2 py-0.5 pr-3 hover:bg-slate-200"
                  [style.padding-left.px]="12 + row.depth * 14"
                  [class.bg-amber-50]="change.status === 'conflicted'"
                  [class.bg-sky-100]="
                    git.conflict()?.path === change.path || git.diff()?.path === change.path
                  "
                  [title]="describe(change)"
                >
                  <input
                    type="checkbox"
                    class="shrink-0 accent-sky-600"
                    [attr.aria-label]="'Select ' + change.path"
                    [checked]="selected().has(change.path)"
                    [disabled]="status.merging || change.status === 'conflicted'"
                    (change)="toggle(change.path)"
                  />
                  @if (change.status === 'conflicted') {
                    <button
                      type="button"
                      class="flex min-w-0 items-center gap-2 text-left hover:underline"
                      [attr.aria-label]="'Resolve the conflict in ' + change.path"
                      (click)="git.openConflict(change.path)"
                    >
                      <span class="truncate">{{ fileName(change.path) }}</span>
                      @if (viewMode() === 'list') {
                        <span class="truncate text-xs text-slate-400">{{
                          folder(change.path)
                        }}</span>
                      }
                    </button>
                    <button
                      type="button"
                      class="ml-auto shrink-0 rounded border border-amber-300 bg-white px-1.5 text-xs text-amber-800 enabled:hover:bg-amber-100 disabled:opacity-50"
                      [disabled]="git.busy() !== null"
                      (click)="git.openConflict(change.path)"
                    >
                      Resolve
                    </button>
                  } @else {
                    <button
                      type="button"
                      class="flex min-w-0 items-center gap-2 text-left hover:underline"
                      [attr.aria-label]="'Show the changes in ' + change.path"
                      (click)="git.openDiff(change.path)"
                    >
                      <span class="truncate">{{ fileName(change.path) }}</span>
                      @if (viewMode() === 'list') {
                        <span class="truncate text-xs text-slate-400">{{
                          folder(change.path)
                        }}</span>
                      }
                    </button>
                    @if (git.diffLoading() === change.path) {
                      <fa-icon
                        class="shrink-0 text-xs text-slate-400"
                        [icon]="faSpinner"
                        animation="spin"
                      />
                    }
                  }
                  @if (change.status !== 'conflicted' && !status.merging) {
                    <button
                      type="button"
                      class="ml-auto hidden size-5 shrink-0 items-center justify-center rounded text-slate-500 group-hover:flex enabled:hover:bg-slate-300 enabled:hover:text-slate-800 disabled:opacity-50"
                      [attr.aria-label]="'Discard changes in ' + change.path"
                      title="Discard changes"
                      [disabled]="git.busy() !== null"
                      (click)="discard([change.path])"
                    >
                      <fa-icon [icon]="faRotateLeft" />
                    </button>
                  }
                  <span
                    class="font-mono text-xs font-bold"
                    [class.ml-auto]="change.status === 'conflicted'"
                    [class]="badges[change.status].color"
                    [title]="change.status"
                    >{{ badges[change.status].letter }}</span
                  >
                </li>
              } @else {
                <li>
                  <button
                    type="button"
                    class="flex w-full items-center gap-1.5 py-0.5 pr-3 text-left text-slate-600 hover:bg-slate-200"
                    [style.padding-left.px]="12 + row.depth * 14"
                    [attr.aria-expanded]="!collapsed().has(row.key)"
                    (click)="toggleFolder(row.key)"
                  >
                    <fa-icon
                      class="w-3 text-xs text-slate-400"
                      [icon]="collapsed().has(row.key) ? faChevronRight : faChevronDown"
                    />
                    <fa-icon class="text-slate-400" [icon]="faFolder" />
                    <span class="truncate">{{ row.name }}</span>
                  </button>
                </li>
              }
            } @empty {
              <li class="px-3 py-1 text-slate-500">No changes.</li>
            }
          </ul>
        }
        <app-git-remotes />
      } @else if (git.busy() === 'refresh') {
        <p class="px-3 text-sm text-slate-500">
          <fa-icon [icon]="faSpinner" animation="spin" /> Reading git status…
        </p>
      } @else if (git.error()) {
        <div class="px-3">
          <button
            type="button"
            class="flex items-center justify-center gap-2 rounded px-3 py-1 text-sm disabled:cursor-not-allowed disabled:opacity-50 flex-1 border border-slate-300 bg-white enabled:hover:bg-slate-100 w-full"
            (click)="git.refresh()"
          >
            <fa-icon [icon]="faRotate" /> Retry
          </button>
        </div>
      }
    }
  `,
})
export class GitView {
  protected readonly git = inject(GitStore);
  protected readonly workspace = inject(WorkspaceStore);
  private readonly history = inject(GitHistoryStore);
  protected readonly badges = STATUS_BADGES;
  protected readonly message = signal('');
  /** Whether the list shows the working tree's changes or the commit history. */
  protected readonly tab = signal<'changes' | 'history'>('changes');
  /** Whether changes are listed flat, with their folder, or grouped under folders. */
  protected readonly viewMode = signal<'list' | 'tree'>('list');
  /** Tree folders the user collapsed, by path. */
  protected readonly collapsed = signal(new Set<string>());
  protected readonly fileName = fileName;
  protected readonly folder = folderOf;

  protected showHistory(): void {
    this.tab.set('history');
    if (this.history.commits() === null) void this.history.load();
  }

  protected tabClass(tab: 'changes' | 'history'): string {
    return this.tab() === tab
      ? 'border-sky-600 text-slate-800'
      : 'border-transparent text-slate-500 hover:text-slate-800';
  }

  constructor() {
    // Offer git's merge commit message once a merge starts, unless the user typed one already.
    effect(() => {
      const mergeMessage = this.git.status()?.mergeMessage;
      if (mergeMessage && !untracked(this.message).trim()) this.message.set(mergeMessage);
    });
  }

  protected abortMerge(): void {
    if (
      confirm(
        'Abort the merge? Your branch goes back to how it was before the pull, and any conflict resolutions so far are lost.',
      )
    ) {
      void this.git.abortMerge();
    }
  }
  /** Paths picked for a partial commit; empty commits everything. */
  protected readonly selected = signal(new Set<string>());

  /** Selected paths that still have changes. */
  protected readonly selectedPaths = computed(() => {
    const changed = new Set(this.git.status()?.changes.map((c) => c.path));
    return [...this.selected()].filter((p) => changed.has(p));
  });

  protected readonly canCommit = computed(
    () =>
      this.git.busy() === null &&
      this.message().trim().length > 0 &&
      this.git.changeCount() > 0 &&
      this.git.conflicts().length === 0,
  );

  protected readonly rows = computed<ChangeRow[]>(() => {
    const changes = this.git.status()?.changes ?? [];
    if (this.viewMode() === 'list') {
      return changes.map((change) => ({ key: change.path, depth: 0, name: '', change }));
    }
    return buildTree(changes, this.collapsed());
  });

  protected readonly allPaths = computed(() => this.git.status()?.changes.map((c) => c.path) ?? []);

  protected readonly faChevronDown = faChevronDown;
  protected readonly faChevronRight = faChevronRight;
  protected readonly faFolder = faFolder;
  protected readonly faListUl = faListUl;
  protected readonly faRotateLeft = faRotateLeft;
  protected readonly faSitemap = faSitemap;
  protected readonly faCodeBranch = faCodeBranch;
  protected readonly faArrowDown = faArrowDown;
  protected readonly faArrowUp = faArrowUp;
  protected readonly faBan = faBan;
  protected readonly faCheck = faCheck;
  protected readonly faRotate = faRotate;
  protected readonly faSpinner = faSpinner;
  protected readonly faFolderOpen = faFolderOpen;
  protected readonly faXmark = faXmark;
  protected readonly faCircleExclamation = faCircleExclamation;
  protected readonly faTriangleExclamation = faTriangleExclamation;

  protected async commit(): Promise<void> {
    if (!this.canCommit()) return;
    if (await this.git.commit(this.message(), this.selectedPaths())) {
      this.message.set('');
      this.selected.set(new Set());
    }
  }

  protected async discard(paths: string[]): Promise<void> {
    const conflicted = new Set(this.git.conflicts().map((c) => c.path));
    const targets = paths.filter((p) => !conflicted.has(p));
    if (!targets.length || this.git.busy() !== null) return;
    const what =
      targets.length === 1
        ? `your changes to ${targets[0]}`
        : `your changes to ${targets.length} files`;
    if (!confirm(`Discard ${what}? This cannot be undone.`)) return;
    if (await this.git.discard(targets)) {
      this.selected.update((set) => new Set([...set].filter((p) => !targets.includes(p))));
    }
  }

  protected toggleFolder(key: string): void {
    this.collapsed.update((set) => {
      const next = new Set(set);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  protected toggle(path: string): void {
    this.selected.update((set) => {
      const next = new Set(set);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  }

  protected busyIcon(operation: GitOperation, icon: typeof faCheck): typeof faCheck {
    return this.git.busy() === operation ? faSpinner : icon;
  }

  protected spin(operation: GitOperation): 'spin' | undefined {
    return this.git.busy() === operation ? 'spin' : undefined;
  }

  protected describe(change: GitChange): string {
    return change.originalPath ? `${change.originalPath} → ${change.path}` : change.path;
  }
}

/** A line of the changes list: a file, or (in the tree view) a folder heading. */
interface ChangeRow {
  key: string;
  depth: number;
  name: string;
  change?: GitChange;
}

/** Groups changes under their folders, folders first, skipping the contents of collapsed ones. */
function buildTree(changes: GitChange[], collapsed: Set<string>): ChangeRow[] {
  interface Node {
    folders: Map<string, Node>;
    files: GitChange[];
  }
  const root: Node = { folders: new Map(), files: [] };
  for (const change of changes) {
    const parts = change.path.split('/');
    let node = root;
    for (const part of parts.slice(0, -1)) {
      let next = node.folders.get(part);
      if (!next) node.folders.set(part, (next = { folders: new Map(), files: [] }));
      node = next;
    }
    node.files.push(change);
  }

  const rows: ChangeRow[] = [];
  const walk = (node: Node, prefix: string, depth: number): void => {
    for (const [name, child] of [...node.folders].sort(([a], [b]) => a.localeCompare(b))) {
      const key = prefix + name;
      rows.push({ key, depth, name });
      if (!collapsed.has(key)) walk(child, key + '/', depth + 1);
    }
    for (const change of [...node.files].sort((a, b) =>
      fileName(a.path).localeCompare(fileName(b.path)),
    )) {
      rows.push({ key: change.path, depth, name: fileName(change.path), change });
    }
  };
  walk(root, '', 0);
  return rows;
}
