import {
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  effect,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import type { editor } from 'monaco-editor/editor/editor.api';
import { Monaco, loadMonaco } from './monaco-loader';

/**
 * A read-only Monaco diff editor comparing two versions of a text, side by side or inline. Monaco
 * is loaded lazily the first time one is shown.
 */
@Component({
  selector: 'app-diff-editor',
  host: { class: 'relative block min-h-0' },
  template: `
    <div #container class="absolute inset-0"></div>
    @if (!ready()) {
      <p class="absolute inset-0 p-3 text-sm text-slate-500">Loading…</p>
    }
  `,
})
export class DiffEditor {
  /** The version before the change. */
  readonly original = input.required<string>();
  /** The version after the change. */
  readonly modified = input.required<string>();
  /** A Monaco language id, e.g. `yaml`, `json` or `plaintext`. */
  readonly language = input('plaintext');
  /** Whether to show both versions side by side rather than inline. */
  readonly sideBySide = input(true);
  readonly ariaLabel = input('Changes');

  protected readonly ready = signal(false);
  private readonly container = viewChild.required<ElementRef<HTMLElement>>('container');
  private monaco: Monaco | null = null;
  private readonly editor = signal<editor.IStandaloneDiffEditor | null>(null);
  private destroyed = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      const diff = this.editor();
      const model = diff?.getModel();
      diff?.dispose();
      model?.original.dispose();
      model?.modified.dispose();
    });

    afterNextRender(() => void this.create());

    effect(() => {
      const original = this.original();
      const modified = this.modified();
      const language = this.language();
      const diff = this.editor();
      if (!diff || !this.monaco) return;
      const old = diff.getModel();
      diff.setModel({
        original: this.monaco.editor.createModel(original, language),
        modified: this.monaco.editor.createModel(modified, language),
      });
      old?.original.dispose();
      old?.modified.dispose();
    });

    effect(() => {
      this.editor()?.updateOptions({
        renderSideBySide: this.sideBySide(),
        ariaLabel: this.ariaLabel(),
      });
    });
  }

  private async create(): Promise<void> {
    const monaco = await loadMonaco();
    if (this.destroyed) return;

    this.monaco = monaco;
    const diff = monaco.editor.createDiffEditor(this.container().nativeElement, {
      readOnly: true,
      originalEditable: false,
      renderSideBySide: this.sideBySide(),
      ariaLabel: this.ariaLabel(),
      automaticLayout: true,
      fontSize: 12,
      lineNumbersMinChars: 3,
      minimap: { enabled: false },
      scrollBeyondLastLine: false,
      renderLineHighlight: 'none',
      renderOverviewRuler: false,
      fixedOverflowWidgets: true,
      theme: 'vs',
    });
    // Setting the signal makes the effect above load the texts.
    this.editor.set(diff);
    this.ready.set(true);
  }
}

/** The Monaco language for a workspace file, from its extension. */
export function languageForPath(path: string): string {
  const extension = path.slice(path.lastIndexOf('.') + 1).toLowerCase();
  switch (extension) {
    case 'yaml':
    case 'yml':
      return 'yaml';
    case 'json':
      return 'json';
    case 'md':
      return 'markdown';
    case 'xml':
      return 'xml';
    case 'graphql':
    case 'gql':
      return 'graphql';
    default:
      return 'plaintext';
  }
}
