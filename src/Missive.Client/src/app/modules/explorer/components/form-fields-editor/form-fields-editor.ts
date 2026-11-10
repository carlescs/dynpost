import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList } from '@angular/cdk/drag-drop';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faCircleInfo,
  faFileArrowUp,
  faGripVertical,
  faMaximize,
  faMinimize,
  faTrash,
  faTriangleExclamation,
} from '@fortawesome/free-solid-svg-icons';
import { FormField, KeyValue } from '../../../../models/workspace';
import { RequestFileInfoDto } from '../../../../models/messages';
import { VariableLookup } from '../../../../models/variables';
import {
  FieldBodyType,
  PART_CONTENT_TYPES,
  bulkProblem,
  emptyField,
  fileNameOf,
  formatBulk,
  isAbsolutePath,
  parseBulk,
} from '../../../../models/form-fields';
import { formatSize } from '../../../../models/format-size';
import { Bridge } from '../../../../services/bridge';
import { VariableInput } from '../../../../components/variable-input/variable-input';
import { CodeEditor } from '../../../../components/code-editor/code-editor';

/** How long to wait after the last edit of a file path before looking the files up. */
const INSPECT_DELAY_MS = 300;

/**
 * The fields of a `form` (multipart) or `urlencoded` body as a table: one row per field, with
 * an enabled checkbox, name, kind (text or file), value or file, and for multipart parts a
 * content type and file name. Rows reorder by dragging, since part order is kept on the wire,
 * and a blank row at the end takes new fields. Bulk edit switches to `name=value` text.
 */
@Component({
  selector: 'app-form-fields-editor',
  imports: [FaIconComponent, CdkDropList, CdkDrag, CdkDragHandle, VariableInput, CodeEditor],
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  templateUrl: './form-fields-editor.html',
})
export class FormFieldsEditor {
  readonly fields = input.required<FormField[]>();
  readonly type = input.required<FieldBodyType>();
  /** The request's headers, to show which `Content-Type` is sent. */
  readonly headers = input<KeyValue[]>([]);
  readonly variables = input<VariableLookup | null>(null);

  readonly fieldsChange = output<FormField[]>();
  /** A file dialog or lookup failed. */
  readonly failed = output<string>();
  readonly submit = output<void>();

  private readonly bridge = inject(Bridge);

  protected readonly contentTypes = PART_CONTENT_TYPES;
  protected readonly faGripVertical = faGripVertical;
  protected readonly faTrash = faTrash;
  protected readonly faMaximize = faMaximize;
  protected readonly faMinimize = faMinimize;
  protected readonly faFileArrowUp = faFileArrowUp;
  protected readonly faTriangleExclamation = faTriangleExclamation;
  protected readonly faCircleInfo = faCircleInfo;
  protected readonly cellClass = 'w-full px-2 py-1 font-mono text-sm';
  protected readonly fileNameOf = fileNameOf;

  protected readonly multipart = computed(() => this.type() === 'form');
  protected readonly rows = computed(() => [...this.fields(), emptyField()]);

  /** Rows whose text value is edited in a multi-line box; values with line breaks always are. */
  private readonly expanded = signal<ReadonlySet<number>>(new Set());

  /** The bulk-edit text while bulk edit is on; `null` shows the table. */
  protected readonly bulkText = signal<string | null>(null);
  protected readonly bulkProblem = computed(() => bulkProblem(this.fields(), this.type()));

  /** The `Content-Type` header the request sets itself, if any. */
  protected readonly contentTypeHeader = computed(
    () =>
      this.headers().find((h) => h.enabled && h.key.trim().toLowerCase() === 'content-type')
        ?.value ?? null,
  );

  /** What the editor knows about each file path, keyed by path. */
  protected readonly files = signal<ReadonlyMap<string, RequestFileInfoDto>>(new Map());
  private readonly filePaths = computed(
    () =>
      this.multipart()
        ? [
            ...new Set(
              this.fields()
                .filter((f) => f.kind === 'file' && f.value.trim() && !f.value.includes('{{'))
                .map((f) => f.value),
            ),
          ]
        : [],
    { equal: (a, b) => a.length === b.length && a.every((p, i) => p === b[i]) },
  );

  constructor() {
    let timer: ReturnType<typeof setTimeout> | undefined;
    let request = 0;
    effect(() => {
      const paths = this.filePaths();
      clearTimeout(timer);
      if (paths.length === 0) return;
      timer = setTimeout(async () => {
        const current = ++request;
        try {
          const infos = await this.bridge.send('request.inspectFiles', { paths });
          if (current === request) this.files.set(new Map(infos.map((i) => [i.path, i])));
        } catch {
          // Only a hint: the request reports missing files when it's sent.
        }
      }, INSPECT_DELAY_MS);
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(timer));
  }

  protected patch(index: number, patch: Partial<FormField>): void {
    const fields = [...this.fields()];
    fields[index] = { ...(fields[index] ?? emptyField()), ...patch };
    this.fieldsChange.emit(fields);
  }

  protected remove(index: number): void {
    this.fieldsChange.emit(this.fields().filter((_, i) => i !== index));
    this.expanded.update((set) => new Set([...set].filter((i) => i !== index)));
  }

  protected drop(event: CdkDragDrop<unknown>): void {
    const { previousIndex: from, currentIndex: to } = event;
    const count = this.fields().length;
    if (from === to || from >= count) return;
    const fields = [...this.fields()];
    const [moved] = fields.splice(from, 1);
    fields.splice(Math.min(to, count - 1), 0, moved);
    this.expanded.set(new Set());
    this.fieldsChange.emit(fields);
  }

  protected setKind(index: number, file: boolean): void {
    this.patch(index, file ? { kind: 'file' } : { kind: 'text', fileName: '' });
  }

  protected hasLineBreaks(field: FormField): boolean {
    return field.value.includes('\n');
  }

  protected isExpanded(index: number, field: FormField): boolean {
    return this.hasLineBreaks(field) || this.expanded().has(index);
  }

  protected toggleExpanded(index: number, field: FormField): void {
    if (this.isExpanded(index, field)) {
      this.expanded.update((set) => new Set([...set].filter((i) => i !== index)));
    } else {
      this.expanded.update((set) => new Set([...set, index]));
    }
  }

  /**
   * Picks files for a row: the first goes in the row, the others in new rows under the same
   * name right after it. On the blank row (`index` past the end) every file is a new row.
   */
  protected async pickFiles(index: number): Promise<void> {
    try {
      const picked = await this.bridge.send('request.pickFiles', { multiple: true });
      if (picked.length === 0) return;
      const fields = [...this.fields()];
      const row = fields[index] ?? emptyField({ name: 'file' });
      const name = row.name || 'file';
      const added = picked.map((file, i) =>
        i === 0
          ? { ...row, name, kind: 'file' as const, value: file.path }
          : emptyField({ name, kind: 'file', value: file.path, contentType: row.contentType }),
      );
      fields.splice(index, index < fields.length ? 1 : 0, ...added);
      this.fieldsChange.emit(fields);
    } catch (error) {
      this.failed.emit(error instanceof Error ? error.message : String(error));
    }
  }

  /** What to show under a file row: its name and size, or why it can't be sent. */
  protected fileStatus(field: FormField): { warning: boolean; text: string } | null {
    const path = field.value.trim();
    if (!path) return { warning: true, text: 'Choose a file.' };
    const info = this.files().get(field.value);
    if (info?.error) return { warning: true, text: info.error };
    if (info && !info.exists) return { warning: true, text: `${fileNameOf(path)} was not found.` };
    const size = info?.size != null ? ` · ${formatSize(info.size)}` : '';
    const sent =
      field.fileName && field.fileName !== fileNameOf(path) ? ` (sent as ${field.fileName})` : '';
    const absolute = isAbsolutePath(path)
      ? ' · absolute path: this request will not be portable to other machines'
      : '';
    return { warning: !!absolute, text: `${fileNameOf(path)}${size}${sent}${absolute}` };
  }

  protected startBulk(): void {
    this.bulkText.set(formatBulk(this.fields(), this.type()));
  }

  protected bulkChanged(text: string): void {
    this.bulkText.set(text);
    this.fieldsChange.emit(parseBulk(text, this.type()));
  }

  protected stopBulk(): void {
    this.bulkText.set(null);
  }

  /** The `Content-Type` hint under the table, and whether it's a warning. */
  protected readonly contentTypeHint = computed(() => {
    const header = this.contentTypeHeader();
    if (this.multipart()) {
      return header === null
        ? { warning: false, text: 'Sent with Content-Type: multipart/form-data; boundary=…' }
        : {
            warning: true,
            text: `The Content-Type header (${header || 'empty'}) is replaced by multipart/form-data; boundary=…, which the body needs.`,
          };
    }
    const urlencoded = 'application/x-www-form-urlencoded';
    if (header === null) return { warning: false, text: `Sent with Content-Type: ${urlencoded}` };
    return header.split(';')[0].trim().toLowerCase() === urlencoded
      ? { warning: false, text: `Sent with Content-Type: ${header}` }
      : {
          warning: true,
          text: `The Content-Type header (${header || 'empty'}) is sent instead of ${urlencoded}.`,
        };
  });

  /** Bulk edit ends when the body type changes. */
  protected readonly resetBulk = effect(() => {
    this.type();
    untracked(() => this.bulkText.set(null));
  });
}
