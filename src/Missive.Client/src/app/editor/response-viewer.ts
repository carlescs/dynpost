import { Component, computed, inject, input, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faBan,
  faCircleExclamation,
  faPaperPlane,
  faSpinner,
  faTriangleExclamation,
} from '@fortawesome/free-solid-svg-icons';
import { ERROR_CODES, ResponseDto } from '../bridge/messages';
import { RequestDocument } from '../workspace/models';
import { WorkspaceStore } from '../workspace/workspace-store';
import { CodeEditor } from './code/code-editor';
import { languageForMediaType } from './code/monaco-loader';
import { previewKind, ResponsePreview } from './response-preview';

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
  template: `
    @let doc = document();

    <header class="flex h-9 shrink-0 items-center gap-4 px-4 text-sm">
      <h2 class="text-xs font-semibold tracking-wide text-slate-500 uppercase">Response</h2>
      @if (response(); as r) {
        <div class="flex gap-4" role="tablist">
          @for (tab of tabs; track tab.id) {
            <button
              type="button"
              role="tab"
              class="-mb-px border-b-2 py-1 text-slate-500 hover:text-slate-800"
              [class.border-sky-500]="activeTab() === tab.id"
              [class.text-slate-900]="activeTab() === tab.id"
              [class.border-transparent]="activeTab() !== tab.id"
              [attr.aria-selected]="activeTab() === tab.id"
              (click)="activeTab.set(tab.id)"
            >
              {{ tab.label }}
              @if (tab.id === 'headers') {
                <span class="text-xs text-sky-600">({{ r.headers.length }})</span>
              }
            </button>
          }
        </div>
        <div class="ml-auto flex items-center gap-4 text-xs text-slate-500">
          <span
            class="rounded px-1.5 py-0.5 font-mono font-bold"
            [class]="statusClass(r.status)"
            title="Status"
            >{{ r.status }} {{ r.statusText }}</span
          >
          <span title="Time">{{ formatDuration(r.durationMs) }}</span>
          <span title="Body size">{{ formatSize(r.size) }}</span>
        </div>
      }
    </header>

    <div class="flex min-h-0 flex-1 flex-col">
      @if (doc.sendId) {
        <div class="flex flex-1 flex-col items-center justify-center gap-3 text-sm text-slate-500">
          <fa-icon class="text-2xl" [icon]="faSpinner" animation="spin" />
          Sending request…
          <button
            type="button"
            class="flex items-center gap-2 rounded border border-slate-300 bg-white px-3 py-1 hover:bg-slate-100"
            (click)="cancel()"
          >
            <fa-icon [icon]="faBan" /> Cancel
          </button>
        </div>
      } @else if (sendError(); as error) {
        <div
          class="flex flex-1 flex-col items-center justify-center gap-2 px-6 text-center text-sm"
          [class.text-rose-700]="error.code !== cancelledCode"
          [class.text-slate-500]="error.code === cancelledCode"
          role="alert"
        >
          <fa-icon
            class="text-2xl"
            [icon]="error.code === cancelledCode ? faBan : faCircleExclamation"
          />
          <p class="max-w-xl break-words whitespace-pre-line">{{ error.message }}</p>
        </div>
      } @else if (response(); as r) {
        @if (r.truncated) {
          <p
            class="flex items-center gap-2 border-b border-amber-200 bg-amber-50 px-4 py-1 text-xs text-amber-800"
          >
            <fa-icon [icon]="faTriangleExclamation" />
            The response is too large; only the first {{ formatSize(r.size) }} are shown.
          </p>
        }
        @switch (activeTab()) {
          @case ('body') {
            @if (r.bodyEncoding === 'base64') {
              @if (isPdf()) {
                <app-response-preview class="border-t border-slate-200" [response]="r" />
              } @else if (isImage(r)) {
                <div class="flex-1 overflow-auto p-4">
                  <img
                    class="max-w-full border border-slate-200 bg-white"
                    alt="Response image"
                    [src]="dataUrl(r)"
                  />
                </div>
              } @else {
                <p class="flex flex-1 items-center justify-center text-sm text-slate-500">
                  Binary content ({{ r.contentType || 'unknown type' }}) is not shown.
                </p>
              }
            } @else if (!r.body) {
              <p class="flex flex-1 items-center justify-center text-sm text-slate-400">
                The response has no body.
              </p>
            } @else {
              @if (bodyViews().length > 1) {
                <div class="flex shrink-0 gap-1 px-4 pt-2 text-xs">
                  @for (option of bodyViews(); track option.id) {
                    <button
                      type="button"
                      class="rounded px-2 py-0.5 hover:bg-slate-200"
                      [class.bg-slate-200]="bodyView() === option.id"
                      [attr.aria-pressed]="bodyView() === option.id"
                      (click)="selectedView.set(option.id)"
                    >
                      {{ option.label }}
                    </button>
                  }
                </div>
              }
              @if (bodyView() === 'preview') {
                <app-response-preview class="mt-1 border-t border-slate-200" [response]="r" />
              } @else {
                <app-code-editor
                  class="mt-1 flex-1 border-t border-slate-200"
                  ariaLabel="Response body"
                  [value]="bodyView() === 'pretty' ? (prettyBody() ?? r.body) : r.body"
                  [language]="language()"
                  [readOnly]="true"
                  [wordWrap]="true"
                />
              }
            }
          }
          @case ('headers') {
            <div class="min-h-0 flex-1 overflow-auto px-4 py-2">
              <table class="w-full text-left font-mono text-xs">
                <tbody>
                  @for (header of r.headers; track $index) {
                    <tr class="border-b border-slate-200 align-top">
                      <th class="py-1 pr-4 font-semibold whitespace-nowrap text-slate-600">
                        {{ header.key }}
                      </th>
                      <td class="py-1 break-all text-slate-800 select-text">{{ header.value }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        }
      } @else {
        <div class="flex flex-1 flex-col items-center justify-center gap-2 text-sm text-slate-400">
          <fa-icon class="text-2xl" [icon]="faPaperPlane" />
          Send the request to see the response here.
        </div>
      }
    </div>
  `,
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
