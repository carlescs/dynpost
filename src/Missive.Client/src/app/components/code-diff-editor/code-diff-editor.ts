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
import { ThemeStore } from '../../services/theme-store';
import { Monaco, loadMonaco } from '../code-editor/monaco-loader';

/**
 * A read-only Monaco diff editor comparing two texts. Monaco is loaded lazily, like in
 * `CodeEditor`; until then a placeholder is shown.
 */
@Component({
  selector: 'app-code-diff-editor',
  host: { class: 'relative block min-h-0' },
  template: `
    <div #container class="absolute inset-0"></div>
    @if (!ready()) {
      <p class="absolute inset-0 p-2 text-xs text-slate-400">Loading the editor…</p>
    }
  `,
})
export class CodeDiffEditor {
  readonly original = input.required<string>();
  readonly modified = input.required<string>();
  /** A Monaco language id, e.g. `yaml`, `json` or `plaintext`. */
  readonly language = input('plaintext');
  /** Both versions next to each other, or one inline view. */
  readonly sideBySide = input(true);
  /** Collapses long runs of unchanged lines. */
  readonly hideUnchanged = input(false);
  readonly wordWrap = input(false);
  readonly ariaLabel = input('Differences');

  protected readonly ready = signal(false);
  private readonly container = viewChild.required<ElementRef<HTMLElement>>('container');
  private monaco: Monaco | null = null;
  private editor: editor.IStandaloneDiffEditor | null = null;
  private models: { original: editor.ITextModel; modified: editor.ITextModel } | null = null;
  private destroyed = false;
  private readonly themes = inject(ThemeStore);

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.editor?.dispose();
      this.models?.original.dispose();
      this.models?.modified.dispose();
    });

    afterNextRender(() => void this.create());

    effect(() => {
      const theme = this.themes.monacoTheme();
      if (this.ready()) this.monaco?.editor.setTheme(theme);
    });

    effect(() => {
      const original = this.original();
      const modified = this.modified();
      if (!this.models) return;
      if (this.models.original.getValue() !== original) this.models.original.setValue(original);
      if (this.models.modified.getValue() !== modified) this.models.modified.setValue(modified);
    });

    effect(() => {
      const language = this.language();
      if (!this.models || !this.monaco) return;
      this.monaco.editor.setModelLanguage(this.models.original, language);
      this.monaco.editor.setModelLanguage(this.models.modified, language);
    });

    effect(() => {
      // Read the inputs first: before the editor exists they must still be tracked.
      const sideBySide = this.sideBySide();
      const hideUnchanged = this.hideUnchanged();
      const wrap = this.wordWrap() ? 'on' : 'off';
      this.editor?.updateOptions({
        renderSideBySide: sideBySide,
        hideUnchangedRegions: { enabled: hideUnchanged },
        wordWrap: wrap,
        diffWordWrap: wrap,
      });
    });
  }

  private async create(): Promise<void> {
    const monaco = await loadMonaco();
    if (this.destroyed) return;

    this.monaco = monaco;
    const original = monaco.editor.createModel(this.original(), this.language());
    const modified = monaco.editor.createModel(this.modified(), this.language());
    const diffEditor = monaco.editor.createDiffEditor(this.container().nativeElement, {
      readOnly: true,
      originalEditable: false,
      renderSideBySide: this.sideBySide(),
      useInlineViewWhenSpaceIsLimited: true,
      hideUnchangedRegions: { enabled: this.hideUnchanged() },
      wordWrap: this.wordWrap() ? 'on' : 'off',
      diffWordWrap: this.wordWrap() ? 'on' : 'off',
      ignoreTrimWhitespace: false,
      renderOverviewRuler: false,
      automaticLayout: true,
      fontSize: 12,
      lineNumbersMinChars: 3,
      minimap: { enabled: false },
      scrollBeyondLastLine: false,
      renderLineHighlight: 'none',
      fixedOverflowWidgets: true,
      theme: this.themes.monacoTheme(),
    });
    diffEditor.setModel({ original, modified });
    this.models = { original, modified };
    this.editor = diffEditor;
    // Monaco has no ariaLabel option for the diff editor as a whole; label its two sides.
    diffEditor.getOriginalEditor().updateOptions({ ariaLabel: `${this.ariaLabel()}: before` });
    diffEditor.getModifiedEditor().updateOptions({ ariaLabel: `${this.ariaLabel()}: after` });
    this.ready.set(true);
  }
}
