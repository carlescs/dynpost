import { Component, computed, inject, input, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faBan,
  faCircleExclamation,
  faPaperPlane,
  faSpinner,
  faTriangleExclamation,
} from '@fortawesome/free-solid-svg-icons';
import { ERROR_CODES, ResponseDto } from '../../../../models/messages';
import { RequestDocument } from '../../../../models/workspace';
import { WorkspaceStore } from '../../../../services/workspace-store';
import { CodeEditor } from '../../../../components/code-editor/code-editor';
import { languageForMediaType } from '../../../../components/code-editor/monaco-loader';
import { previewKind, ResponsePreview } from '../response-preview/response-preview';

type ResponseTab = 'body' | 'headers';
/** How a text body is shown: re-indented, as received, or rendered (HTML). */
type BodyView = 'pretty' | 'raw' | 'preview';

/** Shows the last response of a request: status, time, size, headers and the (pretty) body. */
@Component({
  selector: 'app-response-viewer',
  imports: [FaIconComponent, CodeEditor, ResponsePreview],
  host: {
    class: 'flex h-2/5 min-h-32 shrink-0 flex-col border-t border-slate-200 bg-slate-50',
  },
  templateUrl: './response-viewer.html',
})
export class ResponseViewer {
  readonly document = input.required<RequestDocument>();

  private readonly store = inject(WorkspaceStore);

  protected readonly activeTab = signal<ResponseTab>('body');
  /** The view last picked; kept across responses and used whenever the body offers it. */
  protected readonly selectedView = signal<BodyView | null>(null);
  protected readonly tabs: { id: ResponseTab; label: string }[] = [
    { id: 'body', label: 'Body' },
    { id: 'headers', label: 'Headers' },
  ];

  protected readonly response = computed<ResponseDto | null>(() => {
    const state = this.document().response;
    return state?.kind === 'response' ? state.response : null;
  });

  protected readonly sendError = computed(() => {
    const state = this.document().response;
    return state?.kind === 'error' ? state : null;
  });

  /** The body re-indented when it is JSON; `null` when it isn't (or can't be parsed). */
  protected readonly prettyBody = computed(() => {
    const r = this.response();
    if (!r || r.bodyEncoding !== 'text' || !looksLikeJson(r)) return null;
    try {
      return JSON.stringify(JSON.parse(r.body), null, 2);
    } catch {
      return null;
    }
  });

  /** The ways the text body can be shown; the first is the default. */
  protected readonly bodyViews = computed<{ id: BodyView; label: string }[]>(() => {
    const r = this.response();
    if (!r) return [];
    if (previewKind(r) === 'html') {
      return [
        { id: 'preview', label: 'Preview' },
        { id: 'raw', label: 'Source' },
      ];
    }
    if (this.prettyBody() !== null) {
      return [
        { id: 'pretty', label: 'Pretty' },
        { id: 'raw', label: 'Raw' },
      ];
    }
    return [];
  });

  protected readonly bodyView = computed<BodyView>(() => {
    const views = this.bodyViews();
    const selected = this.selectedView();
    return views.find((v) => v.id === selected)?.id ?? views[0]?.id ?? 'raw';
  });

  protected readonly isPdf = computed(() => {
    const r = this.response();
    return !!r && previewKind(r) === 'pdf';
  });

  /** The Monaco language for the body; JSON sent with a generic content type counts as JSON. */
  protected readonly language = computed(() => {
    const r = this.response();
    if (!r) return 'plaintext';
    const language = languageForMediaType(r.contentType);
    return language === 'plaintext' && looksLikeJson(r) ? 'json' : language;
  });

  protected readonly cancelledCode = ERROR_CODES.cancelled;
  protected readonly faPaperPlane = faPaperPlane;
  protected readonly faSpinner = faSpinner;
  protected readonly faBan = faBan;
  protected readonly faCircleExclamation = faCircleExclamation;
  protected readonly faTriangleExclamation = faTriangleExclamation;

  protected cancel(): void {
    void this.store.cancelSend(this.document().id);
  }

  protected statusClass(status: number): string {
    if (status >= 500) return 'bg-rose-100 text-rose-700';
    if (status >= 400) return 'bg-amber-100 text-amber-700';
    if (status >= 300) return 'bg-sky-100 text-sky-700';
    if (status >= 200) return 'bg-emerald-100 text-emerald-700';
    return 'bg-slate-200 text-slate-700';
  }

  protected formatDuration(ms: number): string {
    return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(2)} s`;
  }

  protected formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  protected isImage(r: ResponseDto): boolean {
    return !!r.contentType?.startsWith('image/') && !r.truncated;
  }

  protected dataUrl(r: ResponseDto): string {
    return `data:${r.contentType};base64,${r.body}`;
  }
}

function looksLikeJson(r: ResponseDto): boolean {
  const type = r.contentType ?? '';
  if (type === 'application/json' || type.endsWith('+json')) return true;
  // Some servers send JSON as text/plain or without a content type.
  const start = r.body.trimStart()[0];
  return (!type || type === 'text/plain') && (start === '{' || start === '[');
}
