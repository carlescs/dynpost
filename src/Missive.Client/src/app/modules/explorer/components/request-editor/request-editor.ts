import {
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  linkedSignal,
  signal,
  untracked,
} from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faBan, faFloppyDisk, faPaperPlane, faSpinner } from '@fortawesome/free-solid-svg-icons';
import {
  BODY_LABELS,
  BODY_TYPES,
  BodyType,
  HTTP_METHODS,
  KeyValue,
  METHOD_COLORS,
  RequestAuth,
  RequestContent,
  RequestDocument,
} from '../../../../models/workspace';
import { EnvironmentStore } from '../../../../services/environment-store';
import { WorkspaceStore } from '../../../../services/workspace-store';
import { CodeEditor } from '../../../../components/code-editor/code-editor';
import { KeyValueTable } from '../../../../components/key-value-table/key-value-table';
import { ResponseViewer } from '../response-viewer/response-viewer';
import { VariableInput } from '../../../../components/variable-input/variable-input';
import { MarkdownView } from '../../../../components/markdown-view/markdown-view';
import { AuthEditor } from '../auth-editor/auth-editor';
import { InheritedHeaders } from '../inherited-headers/inherited-headers';
import { FolderSettingsStore } from '../../../../services/folder-settings-store';
import { InheritedSettingsDto } from '../../../../models/messages';
import { FormFieldsEditor } from '../form-fields-editor/form-fields-editor';
import { fieldsOf, isAbsolutePath } from '../../../../models/form-fields';
import { Bridge } from '../../../../services/bridge';

type RequestTab = 'params' | 'headers' | 'auth' | 'body' | 'capture' | 'docs';

@Component({
  selector: 'app-request-editor',
  imports: [
    FaIconComponent,
    KeyValueTable,
    ResponseViewer,
    CodeEditor,
    VariableInput,
    MarkdownView,
    AuthEditor,
    InheritedHeaders,
    FormFieldsEditor,
  ],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  templateUrl: './request-editor.html',
})
export class RequestEditor {
  readonly document = input.required<RequestDocument>();

  private readonly store = inject(WorkspaceStore);
  private readonly bridge = inject(Bridge);
  protected readonly environments = inject(EnvironmentStore);
  private readonly folders = inject(FolderSettingsStore);

  /**
   * What the request inherits from the folders it is in (or will be saved in): shown next to its
   * own headers and auth. `null` while loading or when it can't be read.
   */
  protected readonly inherited = signal<InheritedSettingsDto | null>(null);
  private readonly folder = computed(() => this.document().folder);
  private inheritedLoad = 0;

  constructor() {
    effect(() => {
      const folder = this.folder();
      this.folders.changed();
      const load = ++this.inheritedLoad;
      void this.folders.inherited(folder).then((inherited) => {
        if (load === this.inheritedLoad) this.inherited.set(inherited);
      });
    });
  }

  protected readonly activeTab = signal<RequestTab>('params');
  protected readonly tabs: { id: RequestTab; label: string }[] = [
    { id: 'params', label: 'Params' },
    { id: 'headers', label: 'Headers' },
    { id: 'auth', label: 'Auth' },
    { id: 'body', label: 'Body' },
    { id: 'capture', label: 'Capture' },
    { id: 'docs', label: 'Docs' },
  ];

  /**
   * Whether the Docs tab shows the Markdown source. Decided per tab (its key never changes, unlike
   * the id, which a first save or a rename changes): a request opened without a description starts
   * in Edit, one with a description in Preview, and the user's pick then holds while they type.
   */
  private readonly documentKey = computed(() => this.document().key);
  protected readonly editingDocs = linkedSignal<string, boolean>({
    source: this.documentKey,
    computation: () => untracked(() => !this.document().description.trim()),
  });

  protected readonly urlPlaceholder = 'https://api.example.com/users or {{baseUrl}}/users';
  protected readonly methods = HTTP_METHODS;
  protected readonly methodColors = METHOD_COLORS;
  protected readonly bodyTypes = BODY_TYPES;
  protected readonly bodyLabels = BODY_LABELS;
  protected readonly faPaperPlane = faPaperPlane;
  protected readonly faFloppyDisk = faFloppyDisk;
  protected readonly faSpinner = faSpinner;
  protected readonly faBan = faBan;
  protected readonly captureExample = '{{token}}';

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  protected readonly responseHeight = signal<number | null>(null);
  protected readonly dragging = signal(false);

  protected startResize(event: PointerEvent): void {
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    this.dragging.set(true);
    event.preventDefault();
  }

  protected resize(event: PointerEvent): void {
    if (!this.dragging()) return;
    const rect = this.host.nativeElement.getBoundingClientRect();
    this.setResponseHeight(rect.bottom - event.clientY);
  }

  protected endResize(event: PointerEvent): void {
    this.dragging.set(false);
    (event.currentTarget as HTMLElement).releasePointerCapture?.(event.pointerId);
  }

  protected resizeByKey(event: KeyboardEvent): void {
    if (event.key !== 'ArrowUp' && event.key !== 'ArrowDown') return;
    const current =
      this.responseHeight() ??
      (this.host.nativeElement.querySelector('app-response-viewer') as HTMLElement).offsetHeight;
    this.setResponseHeight(current + (event.key === 'ArrowUp' ? 24 : -24));
    event.preventDefault();
  }

  private setResponseHeight(height: number): void {
    const max = this.host.nativeElement.clientHeight - 160;
    this.responseHeight.set(Math.round(Math.max(128, Math.min(height, max))));
  }

  protected patch(patch: Partial<RequestContent>): void {
    this.store.update(this.document().id, patch);
  }

  protected patchAuth(patch: Partial<RequestAuth>): void {
    this.patch({ auth: { ...this.document().auth, ...patch } });
  }

  protected save(): void {
    void this.store.save(this.document().id);
  }

  protected send(): void {
    void this.store.send(this.document().id);
  }

  protected cancel(): void {
    void this.store.cancelSend(this.document().id);
  }

  protected async pickBodyFile(): Promise<void> {
    try {
      const file = await this.bridge.send('request.pickFile');
      if (file) this.patch({ body: file.path });
    } catch (error) {
      this.showError(error);
    }
  }

  protected showError(error: unknown): void {
    this.store.error.set(error instanceof Error ? error.message : String(error));
  }

  /** The fields of a form or urlencoded body, also when an older file keeps them as text. */
  protected readonly bodyFields = computed(() => fieldsOf(this.document()));
  protected readonly isAbsolutePath = isAbsolutePath;

  /** The captures as table rows: variable name and expression. */
  protected captureRows(doc: RequestDocument): KeyValue[] {
    return doc.captures.map((c) => ({ key: c.variable, value: c.expression, enabled: true }));
  }

  protected patchCaptures(rows: KeyValue[]): void {
    this.patch({ captures: rows.map((r) => ({ variable: r.key, expression: r.value })) });
  }

  protected setEditingDocs(editing: boolean): void {
    this.editingDocs.set(editing);
  }

  protected count(doc: RequestDocument, tab: RequestTab): number {
    const enabled = (rows: KeyValue[]) => rows.filter((r) => r.enabled && r.key).length;
    if (tab === 'params') return enabled(doc.params);
    if (tab === 'headers') return enabled(doc.headers);
    if (tab === 'capture') return doc.captures.filter((c) => c.variable).length;
    return 0;
  }
}
