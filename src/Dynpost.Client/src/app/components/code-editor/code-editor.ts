import {
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import type { editor } from 'monaco-editor/editor/editor.api';
import {
  VARIABLE_PATTERN,
  VariableLookup,
  describeVariable,
  variableClass,
} from '../../models/variables';
import { ThemeStore } from '../../services/theme-store';
import { jsonPathAt } from '../../models/json-path';
import { copyText } from '../../models/clipboard';
import { canPrettyPrint, prettyPrint } from '../../models/pretty-print';
import { Monaco, loadMonaco } from './monaco-loader';

/**
 * A Monaco editor with syntax highlighting, for request and response bodies. Monaco is loaded
 * lazily the first time one of these is shown; until then the text is shown as plain text.
 */
@Component({
  selector: 'app-code-editor',
  host: { class: 'relative block min-h-0' },
  templateUrl: './code-editor.html',
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
  /** The defined variables; references to others are marked. `null` marks none. */
  readonly variables = input<VariableLookup | null>(null);
  /** Whether a JSON text gets a "Copy JSONPath" action in its context menu. */
  readonly jsonPaths = input(false);

  /** Emits the text as the user edits it. */
  readonly valueChange = output<string>();
  /** Emits on Ctrl+Enter (Cmd+Enter on macOS), which Monaco would otherwise consume. */
  readonly submit = output<void>();
  /** Emits why the text couldn't be prettified (Shift+Alt+F or the context menu). */
  readonly prettifyFailed = output<string>();

  protected readonly ready = signal(false);
  private readonly container = viewChild.required<ElementRef<HTMLElement>>('container');
  private monaco: Monaco | null = null;
  private editor: editor.IStandaloneCodeEditor | null = null;
  /** Set while the text is replaced from the input, so the change isn't echoed back. */
  private applyingInput = false;
  private variableDecorations: editor.IEditorDecorationsCollection | null = null;
  /** Whether "Prettify" is offered: an editable JSON or XML text. */
  private readonly canPrettify = computed(
    () => !this.readOnly() && canPrettyPrint(this.language()),
  );
  /** Whether "Copy JSONPath" is offered: a JSON text that asked for it. */
  private readonly canCopyJsonPath = computed(() => this.jsonPaths() && this.language() === 'json');
  private canPrettifyKey: { set(value: boolean): void } | null = null;
  private canCopyJsonPathKey: { set(value: boolean): void } | null = null;
  private destroyed = false;
  private readonly themes = inject(ThemeStore);

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.editor?.getModel()?.dispose();
      this.editor?.dispose();
    });

    afterNextRender(() => void this.create());

    effect(() => {
      const theme = this.themes.monacoTheme();
      if (this.ready()) this.monaco?.editor.setTheme(theme);
    });

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
      this.variables();
      this.updateVariableDecorations();
    });

    effect(() => {
      // Read the inputs before the keys exist, so the effect follows them once Monaco has loaded.
      const canPrettify = this.canPrettify();
      const canCopyJsonPath = this.canCopyJsonPath();
      this.canPrettifyKey?.set(canPrettify);
      this.canCopyJsonPathKey?.set(canCopyJsonPath);
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
      scrollbar: {
        useShadows: false,
        verticalScrollbarSize: 8,
        horizontalScrollbarSize: 8,
        verticalSliderSize: 4,
        horizontalSliderSize: 4,
      },
      scrollBeyondLastLine: false,
      renderLineHighlight: this.readOnly() ? 'none' : 'line',
      tabSize: 2,
      fixedOverflowWidgets: true,
      theme: this.themes.monacoTheme(),
    });
    this.variableDecorations = editor.createDecorationsCollection();
    editor.onDidChangeModelContent(() => {
      this.updateVariableDecorations();
      if (!this.applyingInput) this.valueChange.emit(model.getValue());
    });
    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter, () => this.submit.emit());
    this.canCopyJsonPathKey = editor.createContextKey(
      'dynpost.canCopyJsonPath',
      this.canCopyJsonPath(),
    );
    editor.addAction({
      id: 'dynpost.copyJsonPath',
      label: 'Copy JSONPath',
      contextMenuGroupId: '9_cutcopypaste',
      precondition: 'dynpost.canCopyJsonPath',
      run: (ed) => {
        const position = ed.getPosition();
        const current = ed.getModel();
        if (!position || !current || !this.jsonPaths() || this.language() !== 'json') return;
        const path = jsonPathAt(current.getValue(), current.getOffsetAt(position));
        if (path) void copyText(path);
      },
    });
    this.canPrettifyKey = editor.createContextKey('dynpost.canPrettify', this.canPrettify());
    editor.addAction({
      id: 'dynpost.prettify',
      label: 'Prettify',
      contextMenuGroupId: '1_modification',
      precondition: 'dynpost.canPrettify',
      // Replaces Monaco's own Format Document, which doesn't know XML or `{{variables}}`.
      keybindings: [monaco.KeyMod.Shift | monaco.KeyMod.Alt | monaco.KeyCode.KeyF],
      run: () => {
        const error = this.prettify();
        if (error) this.prettifyFailed.emit(error);
      },
    });
    this.editor = editor;
    this.updateVariableDecorations();
    this.ready.set(true);
  }

  /** Opens Monaco's find widget (Ctrl+F), once the editor is ready. */
  find(): void {
    this.editor?.focus();
    void this.editor?.getAction('actions.find')?.run();
  }

  /**
   * Re-indents the JSON or XML text as one undoable edit. Returns why it couldn't, or `null` when
   * it did (or the text was already pretty). `{{variable}}` references are kept when the editor
   * highlights them.
   */
  prettify(): string | null {
    const language = this.language();
    if (!canPrettyPrint(language)) return 'Only JSON and XML can be prettified.';
    const model = this.editor?.getModel();
    const text = model?.getValue() ?? this.value();
    const pretty = prettyPrint(text, language, { variables: this.highlightVariables() });
    if (pretty === null) {
      return `The text is not valid ${language.toUpperCase()}, so it was left as it is.`;
    }
    if (pretty === text) return null;
    if (!this.editor || !model) {
      this.valueChange.emit(pretty);
      return null;
    }
    this.editor.pushUndoStop();
    this.editor.executeEdits('dynpost.prettify', [
      { range: model.getFullModelRange(), text: pretty },
    ]);
    this.editor.pushUndoStop();
    return null;
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
    const variables = this.variables();
    const decorations = [...text.matchAll(VARIABLE_PATTERN)].map((match) => {
      const start = model.getPositionAt(match.index);
      const end = model.getPositionAt(match.index + match[0].length);
      return {
        range: new this.monaco!.Range(start.lineNumber, start.column, end.lineNumber, end.column),
        options: {
          inlineClassName: variableClass(match[1], variables),
          hoverMessage: { value: escapeMarkdown(describeVariable(match[1], variables)) },
        },
      };
    });
    this.variableDecorations.set(decorations);
  }
}

/** Monaco renders hover messages as Markdown; variable values are plain text. */
function escapeMarkdown(text: string): string {
  return text.replace(/[\\`*_{}[\]()#+\-.!<>|~]/g, '\\$&');
}
