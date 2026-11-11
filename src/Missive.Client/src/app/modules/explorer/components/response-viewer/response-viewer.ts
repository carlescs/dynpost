import {
  Component,
  computed,
  DestroyRef,
  inject,
  input,
  linkedSignal,
  signal,
} from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faArrowRight,
  faBan,
  faCircleExclamation,
  faClockRotateLeft,
  faCopy,
  faDownload,
  faPaperPlane,
  faSpinner,
  faTriangleExclamation,
} from '@fortawesome/free-solid-svg-icons';
import { ERROR_CODES, ResponseDto } from '../../../../models/messages';
import { RequestDocument } from '../../../../models/workspace';
import { formatSize } from '../../../../models/format-size';
import { WorkspaceStore } from '../../../../services/workspace-store';
import { CodeEditor } from '../../../../components/code-editor/code-editor';
import { languageForMediaType } from '../../../../components/code-editor/monaco-loader';
import { previewKind, ResponsePreview } from '../response-preview/response-preview';
import { ResponseHistory } from '../response-history/response-history';
import { looksLikeJson, prettyJson } from '../../models/response-text';

type ResponseTab = 'body' | 'headers' | 'cookies' | 'captures' | 'timing' | 'request';

/** One bar of the timing waterfall: where it starts and how wide it is, in % of the total. */
export interface TimingPhase {
  label: string;
  ms: number;
  offset: number;
  width: number;
}
/** A request as it went on the wire: one per redirect hop, then the final one. */
export interface SentHop {
  method: string;
  url: string;
  headers: { key: string; value: string }[];
  /** The redirect status it got; `null` for the final request. */
  status: number | null;
}

/** How a text body is shown: re-indented, as received, or rendered (HTML). */
type BodyView = 'pretty' | 'raw' | 'preview';

/**
 * Shows the last response of a request: status, time, size, the (pretty) body, headers, the
 * cookies it set, a timing breakdown with the redirects followed, and the request as sent. For a
 * saved request, the History button shows the responses kept for it instead.
 */
@Component({
  selector: 'app-response-viewer',
  imports: [FaIconComponent, CodeEditor, ResponsePreview, ResponseHistory],
  host: {
    class: 'flex h-2/5 min-h-32 shrink-0 flex-col border-t border-slate-200 bg-slate-50',
  },
  templateUrl: './response-viewer.html',
})
export class ResponseViewer {
  readonly document = input.required<RequestDocument>();

  private readonly store = inject(WorkspaceStore);

  protected readonly activeTab = signal<ResponseTab>('body');
  /** Whether the request's history is shown instead of its last response. */
  protected readonly historyOpen = signal(false);
  /** The history is kept per request file: an unsaved request has none. */
  protected readonly showHistory = computed(() => this.historyOpen() && !!this.document().path);
  /** A short confirmation after copying or saving; cleared when another response arrives. */
  protected readonly note = linkedSignal<ResponseDto | null, string | null>({
    source: () => this.response(),
    computation: () => null,
  });
  protected readonly saving = signal(false);
  private noteTimer: ReturnType<typeof setTimeout> | undefined;
  /** The view last picked; kept across responses and used whenever the body offers it. */
  protected readonly selectedView = signal<BodyView | null>(null);
  protected readonly tabs: { id: ResponseTab; label: string }[] = [
    { id: 'body', label: 'Body' },
    { id: 'headers', label: 'Headers' },
    { id: 'cookies', label: 'Cookies' },
    { id: 'captures', label: 'Captures' },
    { id: 'timing', label: 'Timing' },
    { id: 'request', label: 'Request' },
  ];

  /** The tabs a response offers: Captures only when the request captures values. */
  protected tabsFor(r: ResponseDto): { id: ResponseTab; label: string }[] {
    return r.captures?.length ? this.tabs : this.tabs.filter((t) => t.id !== 'captures');
  }

  /** The tab shown: the one picked, unless this response doesn't offer it. */
  protected readonly shownTab = computed<ResponseTab>(() => {
    const tab = this.activeTab();
    return tab === 'captures' && !this.response()?.captures?.length ? 'body' : tab;
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.noteTimer));
  }

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
    return r ? prettyJson(r) : null;
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

  /** The waterfall of the response's timing phases; empty when they weren't measured. */
  protected readonly phases = computed<TimingPhase[]>(() => timingPhases(this.response()));

  /** Every request sent, oldest first; empty when the host didn't report them. */
  protected readonly sentHops = computed<SentHop[]>(() => sentHops(this.response()));

  /** Whether an earlier hop sent `Authorization` and the final request didn't (redirects drop it). */
  protected readonly authDropped = computed(() => {
    const hops = this.sentHops();
    const hasAuth = (hop: SentHop) => hop.headers.some((h) => /^authorization$/i.test(h.key));
    return hops.length > 1 && !hasAuth(hops[hops.length - 1]) && hops.some(hasAuth);
  });

  /** The text shown in the body view, which is what "Copy" copies. */
  protected readonly shownBody = computed(() => {
    const r = this.response();
    if (!r || r.bodyEncoding !== 'text') return null;
    return this.bodyView() === 'pretty' ? (this.prettyBody() ?? r.body) : r.body;
  });

  protected readonly cancelledCode = ERROR_CODES.cancelled;
  protected readonly faArrowRight = faArrowRight;
  protected readonly faCopy = faCopy;
  protected readonly faDownload = faDownload;
  protected readonly faPaperPlane = faPaperPlane;
  protected readonly faSpinner = faSpinner;
  protected readonly faBan = faBan;
  protected readonly faCircleExclamation = faCircleExclamation;
  protected readonly faClockRotateLeft = faClockRotateLeft;
  protected readonly faTriangleExclamation = faTriangleExclamation;

  protected cancel(): void {
    void this.store.cancelSend(this.document().id);
  }

  protected async copyBody(): Promise<void> {
    const text = this.shownBody();
    if (text === null) return;
    this.showNote((await copyText(text)) ? 'Copied' : 'Could not copy');
  }

  protected async saveBody(): Promise<void> {
    if (this.saving()) return;
    this.saving.set(true);
    try {
      const path = await this.store.saveResponseBody(this.document().id);
      if (path) this.showNote(`Saved to ${path}`);
    } finally {
      this.saving.set(false);
    }
  }

  protected tabCount(tab: ResponseTab, r: ResponseDto): number | null {
    if (tab === 'headers') return r.headers.length;
    if (tab === 'cookies') return r.cookies?.length ?? 0;
    if (tab === 'request') return r.sent?.headers.length ?? null;
    if (tab === 'captures') return r.captures?.filter((c) => c.value != null).length ?? 0;
    return null;
  }

  protected formatDate(iso: string | null | undefined): string {
    if (!iso) return 'Session';
    const date = new Date(iso);
    return Number.isNaN(date.getTime()) ? iso : date.toLocaleString();
  }

  protected formatMs(ms: number): string {
    return ms < 10 ? `${ms.toFixed(1)} ms` : this.formatDuration(Math.round(ms));
  }

  private showNote(text: string): void {
    this.note.set(text);
    clearTimeout(this.noteTimer);
    this.noteTimer = setTimeout(() => this.note.set(null), 4000);
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

  protected readonly formatSize = formatSize;

  protected isImage(r: ResponseDto): boolean {
    return !!r.contentType?.startsWith('image/') && !r.truncated;
  }

  protected dataUrl(r: ResponseDto): string {
    return `data:${r.contentType};base64,${r.body}`;
  }
}

/** The requests sent for a response: those that got a redirect, then the final one. */
export function sentHops(r: ResponseDto | null): SentHop[] {
  if (!r?.sent) return [];
  const redirects = (r.redirects ?? []).map((hop) => ({
    method: hop.method,
    url: hop.url,
    headers: hop.sentHeaders ?? [],
    status: hop.status,
  }));
  return [...redirects, { ...r.sent, status: null }];
}

/**
 * The timing phases as a waterfall, each bar starting where the previous one ended. Phases that
 * didn't happen (no new connection, plain http) are left out.
 */
export function timingPhases(r: ResponseDto | null): TimingPhase[] {
  const t = r?.timings;
  if (!t) return [];
  const phases = [
    { label: 'DNS lookup', ms: t.dns },
    { label: 'TCP connect', ms: t.connect },
    { label: 'TLS handshake', ms: t.tls },
    { label: 'Waiting (TTFB)', ms: t.wait },
    { label: 'Download', ms: t.download },
  ].filter((p): p is { label: string; ms: number } => p.ms !== null && p.ms !== undefined);
  const total = phases.reduce((sum, p) => sum + p.ms, 0);
  let start = 0;
  return phases.map((p) => {
    const phase = {
      ...p,
      offset: total ? (start / total) * 100 : 0,
      width: total ? (p.ms / total) * 100 : 0,
    };
    start += p.ms;
    return phase;
  });
}

/**
 * Copies text to the clipboard. The asynchronous clipboard API needs a secure context, which the
 * desktop host's custom scheme may not be; then a hidden text area is copied instead.
 */
export async function copyText(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    // Fall back below.
  }
  const area = document.createElement('textarea');
  area.value = text;
  area.setAttribute('readonly', '');
  area.style.position = 'fixed';
  area.style.opacity = '0';
  document.body.appendChild(area);
  area.select();
  try {
    return document.execCommand('copy');
  } catch {
    return false;
  } finally {
    area.remove();
  }
}
