import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faTrash } from '@fortawesome/free-solid-svg-icons';
import { HistoryEntryDetailsDto, HistoryEntryDto, ResponseDto } from '../../../../models/messages';
import { formatSize } from '../../../../models/format-size';
import { Bridge, BridgeError } from '../../../../services/bridge';
import { Dialogs } from '../../../../services/dialogs';
import { CodeEditor } from '../../../../components/code-editor/code-editor';
import { CodeDiffEditor } from '../../../../components/code-diff-editor/code-diff-editor';
import { languageForMediaType } from '../../../../components/code-editor/monaco-loader';
import { comparableBody, comparableHeaders, looksLikeJson } from '../../models/response-text';

type HistoryView = 'body' | 'headers';

/**
 * The responses kept for a request file (`.missive/history/`, on this computer only), newest
 * first. The selected one is shown, or compared with the response in the viewer in a diff editor.
 */
@Component({
  selector: 'app-response-history',
  imports: [FaIconComponent, CodeEditor, CodeDiffEditor],
  host: { class: 'flex min-h-0 flex-1 border-t border-slate-200' },
  templateUrl: './response-history.html',
})
export class ResponseHistory {
  /** The workspace path of the request file. */
  readonly path = input.required<string>();
  /** The response in the viewer, which the selected entry is compared with. */
  readonly current = input<ResponseDto | null>(null);

  private readonly bridge = inject(Bridge);
  private readonly dialogs = inject(Dialogs);

  protected readonly entries = signal<HistoryEntryDto[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly selected = signal<HistoryEntryDetailsDto | null>(null);
  protected readonly view = signal<HistoryView>('body');
  /** Whether to compare the selected entry with the current response, when there is one. */
  protected readonly compareWanted = signal(true);
  private loads = 0;
  private selections = 0;

  protected readonly faTrash = faTrash;
  protected readonly formatSize = formatSize;

  /** The id of the entry that keeps the response in the viewer, if any. */
  protected readonly currentId = computed(() => this.current()?.historyId ?? null);

  /** Comparing makes sense when there is a current response and another entry is selected. */
  protected readonly canCompare = computed(() => {
    const selected = this.selected();
    return !!this.current() && !!selected && selected.entry.id !== this.currentId();
  });

  protected readonly comparing = computed(() => this.compareWanted() && this.canCompare());

  /** The selected entry's text in the current view. */
  protected readonly selectedText = computed(() => {
    const selected = this.selected();
    if (!selected) return '';
    return this.view() === 'body'
      ? comparableBody(selected.response, selected.entry.bodyOmitted)
      : comparableHeaders(selected.response);
  });

  /** The current response's text in the current view. */
  protected readonly currentText = computed(() => {
    const current = this.current();
    if (!current) return '';
    return this.view() === 'body' ? comparableBody(current) : comparableHeaders(current);
  });

  protected readonly language = computed(() => {
    const r = this.selected()?.response;
    if (!r || this.view() === 'headers' || r.bodyEncoding !== 'text') return 'plaintext';
    const language = languageForMediaType(r.contentType);
    return language === 'plaintext' && looksLikeJson(r) ? 'json' : language;
  });

  constructor() {
    // Reload when the request changes (renamed, moved) and when a new response was kept.
    effect(() => {
      const path = this.path();
      this.currentId();
      untracked(() => void this.load(path));
    });
  }

  private async load(path: string): Promise<void> {
    const load = ++this.loads;
    this.error.set(null);
    try {
      const entries = await this.bridge.send('history.list', { path });
      if (load !== this.loads) return;
      this.entries.set(entries);
      const kept = this.selected();
      if (!kept || !entries.some((e) => e.id === kept.entry.id)) {
        // By default, the newest response other than the one in the viewer.
        const current = this.currentId();
        const first = entries.find((e) => e.id !== current) ?? entries[0];
        if (first) await this.select(first.id);
        else this.selected.set(null);
      }
    } catch (error) {
      if (load === this.loads) this.error.set(message(error));
    } finally {
      if (load === this.loads) this.loading.set(false);
    }
  }

  protected async select(id: string): Promise<void> {
    const selection = ++this.selections;
    try {
      const entry = await this.bridge.send('history.get', { path: this.path(), id });
      if (selection === this.selections) this.selected.set(entry);
    } catch (error) {
      if (selection === this.selections) this.error.set(message(error));
    }
  }

  protected async remove(entry: HistoryEntryDto): Promise<void> {
    try {
      const left = await this.bridge.send('history.delete', { path: this.path(), id: entry.id });
      this.entries.set(left);
      if (this.selected()?.entry.id === entry.id) {
        this.selected.set(null);
        if (left[0]) await this.select(left[0].id);
      }
    } catch (error) {
      this.error.set(message(error));
    }
  }

  protected async clear(): Promise<void> {
    const confirmed = await this.dialogs.confirm(
      `Delete the ${this.entries().length} responses kept for this request?`,
      { title: 'Clear history', confirmLabel: 'Delete', danger: true },
    );
    if (!confirmed) return;
    try {
      this.entries.set(await this.bridge.send('history.delete', { path: this.path() }));
      this.selected.set(null);
    } catch (error) {
      this.error.set(message(error));
    }
  }

  protected formatTime(iso: string): string {
    const date = new Date(iso);
    return Number.isNaN(date.getTime()) ? iso : date.toLocaleString();
  }

  protected formatDuration(ms: number): string {
    return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(2)} s`;
  }

  protected statusClass(status: number): string {
    if (status >= 500) return 'bg-rose-100 text-rose-700';
    if (status >= 400) return 'bg-amber-100 text-amber-700';
    if (status >= 300) return 'bg-sky-100 text-sky-700';
    if (status >= 200) return 'bg-emerald-100 text-emerald-700';
    return 'bg-slate-200 text-slate-700';
  }
}

function message(error: unknown): string {
  return error instanceof BridgeError ? error.message : String(error);
}
