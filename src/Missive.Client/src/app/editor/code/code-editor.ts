import {
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import type { editor } from 'monaco-editor/editor/editor.api';
import { VARIABLE_PATTERN } from '../variables';
import { Monaco, loadMonaco } from './monaco-loader';

/**
 * A Monaco editor with syntax highlighting, for request and response bodies. Monaco is loaded
 * lazily the first time one of these is shown; until then the text is shown as plain text.
 */
@Component({
  selector: 'app-code-editor',
  host: { class: 'relative block min-h-0' },
  template: `
    <div #container class="absolute inset-0"></div>
    @if (!ready()) {
      <pre
        class="absolute inset-0 overflow-auto p-2 font-mono text-xs whitespace-pre-wrap text-slate-800"
        >{{ value() }}</pre>
    }
  `,
})
export class CodeEditor {
  readonly value = input.required<string>();
  /** A Monaco language id, e.g. `json`, `xml` or `plaintext`. */
  readonly language = input('plaintext');
  readonly readOnly = input(false);
  readonly wordWrap = input(false);
  readonly ariaLabel = input('Code editor');
  /** Whether to highlight `{{variable}}` references. */
  readonly highlightVariables = input(false);

  /** Emits the text as the user edits it. */
  readonly valueChange = output<string>();
  /** Emits on Ctrl+Enter (Cmd+Enter on macOS), which Monaco would otherwise consume. */
  readonly submit = output<void>();

  protected readonly ready = signal(false);
  private readonly container = viewChild.required<ElementRef<HTMLElement>>('container');
  private monaco: Monaco | null = null;
  private editor: editor.IStandaloneCodeEditor | null = null;
  /** Set while the text is replaced from the input, so the change isn't echoed back. */
  private applyingInput = false;
  private variableDecorations: editor.IEditorDecorationsCollection | null = null;
  private destroyed = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.editor?.getModel()?.dispose();
      this.editor?.dispose();
    });

    afterNextRender(() => void this.create());

    effect(() => {
      const value = this.value();
      const model = this.editor?.getModel();
      if (model && model.getValue() !== value) {
        this.applyingInput = true;
        try {
          model.setValue(value);
        } finally {
          this.applyingInput = false;
        }
      }
    });

    effect(() => {
      const language = this.language();
      const model = this.editor?.getModel();
      if (model && this.monaco) this.monaco.editor.setModelLanguage(model, language);
    });

    effect(() => {
      this.highlightVariables();
      this.updateVariableDecorations();
    });

    effect(() => {
      this.editor?.updateOptions({
        readOnly: this.readOnly(),
        wordWrap: this.wordWrap() ? 'on' : 'off',
        ariaLabel: this.ariaLabel(),
      });
    });
  }

  private async create(): Promise<void> {
    const monaco = await loadMonaco();
    if (this.destroyed) return;

    this.monaco = monaco;
    const model = monaco.editor.createModel(this.value(), this.language());
    const editor = monaco.editor.create(this.container().nativeElement, {
      model,
      readOnly: this.readOnly(),
      wordWrap: this.wordWrap() ? 'on' : 'off',
      ariaLabel: this.ariaLabel(),
      automaticLayout: true,
      fontSize: 12,
      lineNumbersMinChars: 3,
      minimap: { enabled: false },
      scrollBeyondLastLine: false,
      renderLineHighlight: this.readOnly() ? 'none' : 'line',
      tabSize: 2,
      fixedOverflowWidgets: true,
      theme: 'vs',
    });
    this.variableDecorations = editor.createDecorationsCollection();
    editor.onDidChangeModelContent(() => {
      this.updateVariableDecorations();
      if (!this.applyingInput) this.valueChange.emit(model.getValue());
    });
    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter, () => this.submit.emit());
    this.editor = editor;
    this.updateVariableDecorations();
    this.ready.set(true);
  }

  /** Marks every `{{variable}}` reference (styled by `.variable-highlight` in styles.scss). */
  private updateVariableDecorations(): void {
    const model = this.editor?.getModel();
    if (!model || !this.monaco || !this.variableDecorations) return;
    if (!this.highlightVariables()) {
      this.variableDecorations.clear();
      return;
    }

    const text = model.getValue();
    const decorations = [...text.matchAll(VARIABLE_PATTERN)].map((match) => {
      const start = model.getPositionAt(match.index);
      const end = model.getPositionAt(match.index + match[0].length);
      return {
        range: new this.monaco!.Range(start.lineNumber, start.column, end.lineNumber, end.column),
        options: {
          inlineClassName: 'variable-highlight',
          hoverMessage: { value: `Variable \`${match[1]}\`` },
        },
      };
    });
    this.variableDecorations.set(decorations);
  }
}
