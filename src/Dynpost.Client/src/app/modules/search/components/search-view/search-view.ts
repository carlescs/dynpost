import { Component, ElementRef, afterNextRender, computed, inject, viewChild } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { HighlightedText } from '../../../../components/highlighted-text/highlighted-text';
import {
  faArrowsRotate,
  faChevronDown,
  faChevronRight,
  faFont,
} from '@fortawesome/free-solid-svg-icons';
import { highlightRanges } from '../../../../models/highlight';
import { SearchFileDto } from '../../../../models/messages';
import { HttpMethod, METHOD_COLORS } from '../../../../models/workspace';
import { WorkspaceStore } from '../../../../services/workspace-store';
import { SearchState } from '../../services/search-state';

/**
 * The Search view: finds text in every request file of the workspace (URLs, headers, bodies…) and
 * lists the matching lines grouped by file; clicking a file or a line opens the request.
 */
@Component({
  selector: 'app-search-view',
  imports: [FaIconComponent, HighlightedText],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  templateUrl: './search-view.html',
})
export class SearchView {
  private readonly input = viewChild<ElementRef<HTMLInputElement>>('input');
  protected readonly state = inject(SearchState);
  protected readonly workspace = inject(WorkspaceStore);
  protected readonly faArrowsRotate = faArrowsRotate;
  protected readonly faChevronDown = faChevronDown;
  protected readonly faChevronRight = faChevronRight;
  protected readonly faFont = faFont;

  protected readonly headerButton =
    'flex size-6 items-center justify-center rounded text-slate-500 hover:bg-slate-200 hover:text-slate-800 disabled:opacity-40';

  /** The results with each line split into highlighted and plain parts. */
  protected readonly files = computed(() =>
    (this.state.results()?.files ?? []).map((file) => ({
      file,
      folder: file.path.slice(0, file.path.lastIndexOf('/')),
      lines: file.lines.map((line) => ({ line, parts: highlightRanges(line.text, line.ranges) })),
    })),
  );

  constructor() {
    // The files may have changed while another view was shown.
    if (this.state.query()) void this.state.search();
    // Showing the view (Ctrl+Shift+F) is a request to type a search.
    afterNextRender(() => this.input()?.nativeElement.select());
  }

  protected methodColor(method: string | null): string {
    return method ? (METHOD_COLORS[method as HttpMethod] ?? 'text-slate-500') : 'text-slate-400';
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      void this.state.search();
    } else if (event.key === 'Escape' && this.state.query()) {
      event.preventDefault();
      this.state.setQuery('');
    }
  }

  protected open(file: SearchFileDto): void {
    void this.workspace.openRequest({ path: file.path, name: file.name, kind: 'request' });
  }
}
