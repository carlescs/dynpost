import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { WorkspaceSearchDto } from '../../../models/messages';
import { Bridge } from '../../../services/bridge';
import { WorkspaceStore } from '../../../services/workspace-store';

/** How long typing pauses before the workspace is searched. */
export const SEARCH_DELAY_MS = 250;

/**
 * The workspace search: its query, options and results. The host searches the request files; this
 * keeps what it found while the sidebar shows another view, and forgets it when another workspace
 * is opened.
 */
@Injectable({ providedIn: 'root' })
export class SearchState {
  private readonly bridge = inject(Bridge);
  private readonly workspace = inject(WorkspaceStore);

  readonly query = signal('');
  readonly matchCase = signal(false);
  readonly results = signal<WorkspaceSearchDto | null>(null);
  readonly searching = signal(false);
  readonly error = signal<string | null>(null);
  /** The files whose matches are folded away, by path. */
  readonly collapsed = signal<ReadonlySet<string>>(new Set());

  readonly summary = computed(() => {
    const results = this.results();
    if (!results) return null;
    const { files, matchCount, truncated } = results;
    if (!files.length) return 'No results.';
    const counted = `${matchCount} ${matchCount === 1 ? 'result' : 'results'} in ${files.length} ${files.length === 1 ? 'file' : 'files'}`;
    return truncated
      ? `${counted}. Only the first ones are shown: refine the search.`
      : `${counted}.`;
  });

  private readonly root = computed(() => this.workspace.workspace()?.root ?? null);
  private timer: ReturnType<typeof setTimeout> | undefined;
  /** Bumped by each search, so an answer to an older query never replaces a newer one. */
  private searches = 0;

  constructor() {
    // Results belong to the workspace they were found in.
    let searchedIn = untracked(this.root);
    effect(() => {
      const root = this.root();
      if (root === searchedIn) return;
      searchedIn = root;
      untracked(() => this.clear());
    });
  }

  /** Changes the query and searches once typing pauses. */
  setQuery(query: string): void {
    this.query.set(query);
    this.schedule();
  }

  toggleMatchCase(): void {
    this.matchCase.update((value) => !value);
    this.schedule(0);
  }

  toggleFile(path: string): void {
    this.collapsed.update((paths) => {
      const next = new Set(paths);
      if (!next.delete(path)) next.add(path);
      return next;
    });
  }

  /** Searches now with the current query and options; an empty query clears the results. */
  async search(): Promise<void> {
    clearTimeout(this.timer);
    const search = ++this.searches;
    const query = this.query();
    if (!query || !this.workspace.workspace()) {
      this.results.set(null);
      this.error.set(null);
      this.searching.set(false);
      return;
    }

    this.searching.set(true);
    try {
      const results = await this.bridge.send('workspace.search', {
        query,
        matchCase: this.matchCase(),
      });
      if (search !== this.searches) return;
      this.results.set(results);
      this.collapsed.set(new Set());
      this.error.set(null);
    } catch (error) {
      if (search !== this.searches) return;
      this.results.set(null);
      this.error.set(error instanceof Error ? error.message : String(error));
    } finally {
      if (search === this.searches) this.searching.set(false);
    }
  }

  private schedule(delay = SEARCH_DELAY_MS): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => void this.search(), delay);
  }

  private clear(): void {
    clearTimeout(this.timer);
    this.searches++;
    this.results.set(null);
    this.error.set(null);
    this.searching.set(false);
    this.collapsed.set(new Set());
  }
}
