import { DialogRef } from '@angular/cdk/dialog';
import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { HighlightedText } from '../highlighted-text/highlighted-text';
import { faMagnifyingGlass } from '@fortawesome/free-solid-svg-icons';
import { TextSegment, highlightPositions } from '../../models/highlight';
import { QuickOpenItemDto } from '../../models/messages';
import { HttpMethod, METHOD_COLORS } from '../../models/workspace';
import { Bridge } from '../../services/bridge';
import { WorkspaceStore } from '../../services/workspace-store';

/** A quick open result as shown: its texts split into highlighted and plain parts. */
interface QuickOpenRow {
  item: QuickOpenItemDto;
  name: TextSegment[];
  path: TextSegment[];
  url: TextSegment[];
}

/**
 * Quick open (Ctrl+P): finds requests by a fuzzy query over their names, paths, methods and URLs —
 * the host does the matching — and opens the chosen one in a tab. Opened with `Dialogs.open`.
 */
@Component({
  selector: 'app-quick-open',
  imports: [FaIconComponent, HighlightedText],
  templateUrl: './quick-open.html',
})
export class QuickOpen {
  private readonly bridge = inject(Bridge);
  private readonly workspace = inject(WorkspaceStore);
  private readonly ref = inject(DialogRef);
  private readonly list = viewChild<ElementRef<HTMLElement>>('list');

  protected readonly query = signal('');
  protected readonly rows = signal<QuickOpenRow[]>([]);
  protected readonly selected = signal(0);
  protected readonly error = signal<string | null>(null);
  protected readonly loaded = signal(false);
  protected readonly faMagnifyingGlass = faMagnifyingGlass;
  /** Bumped by each search, so an answer to an older query never replaces a newer one. */
  private searches = 0;

  constructor() {
    void this.search('');
  }

  protected methodColor(method: string | null): string {
    return method ? (METHOD_COLORS[method as HttpMethod] ?? 'text-slate-500') : 'text-slate-400';
  }

  protected onInput(value: string): void {
    this.query.set(value);
    void this.search(value);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const count = this.rows().length;
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (count) this.select((this.selected() + 1) % count);
        break;
      case 'ArrowUp':
        event.preventDefault();
        if (count) this.select((this.selected() - 1 + count) % count);
        break;
      case 'Enter': {
        event.preventDefault();
        const row = this.rows()[this.selected()];
        if (row) void this.open(row.item);
        break;
      }
    }
  }

  /** Closes the dialog and opens the request in a tab (or switches to its tab). */
  protected async open(item: QuickOpenItemDto): Promise<void> {
    this.ref.close();
    await this.workspace.openRequest({ path: item.path, name: item.name, kind: 'request' });
  }

  private select(index: number): void {
    this.selected.set(index);
    queueMicrotask(() =>
      this.list()
        ?.nativeElement.querySelector(`[data-index="${index}"]`)
        ?.scrollIntoView({ block: 'nearest' }),
    );
  }

  private async search(query: string): Promise<void> {
    const search = ++this.searches;
    try {
      const items = await this.bridge.send('workspace.quickOpen', { query: query.trim() });
      if (search !== this.searches) return;
      this.rows.set(
        items.map((item) => ({
          item,
          name: highlightPositions(item.name, item.nameMatches),
          path: highlightPositions(item.path, item.pathMatches),
          url: highlightPositions(item.url, item.urlMatches),
        })),
      );
      this.selected.set(0);
      this.error.set(null);
    } catch (error) {
      if (search !== this.searches) return;
      this.rows.set([]);
      this.error.set(error instanceof Error ? error.message : String(error));
    } finally {
      if (search === this.searches) this.loaded.set(true);
    }
  }
}
