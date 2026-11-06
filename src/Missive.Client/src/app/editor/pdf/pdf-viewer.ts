import {
  afterNextRender,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  signal,
  untracked,
  viewChild,
  ViewEncapsulation,
} from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faCircleExclamation,
  faLeftRight,
  faMagnifyingGlassMinus,
  faMagnifyingGlassPlus,
  faSpinner,
} from '@fortawesome/free-solid-svg-icons';
import type { PDFDocumentProxy, PDFPageProxy, RenderTask } from 'pdfjs-dist';
import { loadPdfJs, pdfAssetUrls, type PdfJs } from './pdf-loader';

/** Zoom steps, as a fraction of the page's natural size (72 dpi = 96 CSS px at 100%). */
const ZOOM_STEPS = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 3, 4];
/** Space around the pages inside the scroll area, in CSS px (matches `p-4`). */
const GUTTER = 16;
/** Most pixels one page canvas may hold (WebKit's canvas area limit), and the longest side. */
const MAX_CANVAS_PIXELS = 16_777_216;
const MAX_CANVAS_SIDE = 8192;

type Zoom = number | 'fit';

interface PageSlot {
  page: PDFPageProxy;
  element: HTMLElement;
  rendered: boolean;
  task?: RenderTask;
  cancelText?: () => void;
}

/**
 * Renders a PDF with pdf.js: one canvas per page, drawn when it scrolls into view, with a text
 * layer on top so text can be selected and copied. Fits the page width by default.
 */
@Component({
  selector: 'app-pdf-viewer',
  imports: [FaIconComponent],
  // The pages are created imperatively, so their styles can't be encapsulated.
  encapsulation: ViewEncapsulation.None,
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  styles: `
    app-pdf-viewer {
      .pdf-page {
        position: relative;
        margin: 0 auto 16px;
        background: white;
        box-shadow: 0 1px 3px rgb(0 0 0 / 0.25);
        --scale-round-x: 1px;
        --scale-round-y: 1px;

        canvas {
          position: absolute;
          inset: 0;
          width: 100%;
          height: 100%;
        }
      }

      // From pdf.js's web/pdf_viewer.css: the transparent, selectable text over the canvas.
      .textLayer {
        position: absolute;
        text-align: initial;
        inset: 0;
        overflow: clip;
        line-height: 1;
        letter-spacing: normal;
        word-spacing: normal;
        text-size-adjust: none;
        forced-color-adjust: none;
        transform-origin: 0 0;
        caret-color: CanvasText;
        z-index: 0;
        --min-font-size: 1;
        --text-scale-factor: calc(var(--total-scale-factor) * var(--min-font-size));
        --min-font-size-inv: calc(1 / var(--min-font-size));

        :is(span, br) {
          color: transparent;
          position: absolute;
          white-space: pre;
          cursor: text;
          transform-origin: 0% 0%;
          user-select: text;
        }

        > :not(.markedContent),
        .markedContent span:not(.markedContent) {
          z-index: 1;
          --font-height: 0;
          font-size: calc(var(--text-scale-factor) * var(--font-height));
          --scale-x: 1;
          --rotate: 0deg;
          transform: rotate(var(--rotate)) scaleX(var(--scale-x)) scale(var(--min-font-size-inv));
        }

        .markedContent {
          display: contents;
        }

        span[role='img'] {
          user-select: none;
          cursor: default;
        }

        ::selection {
          background: rgb(0 0 255 / 0.25);
          color: transparent;
        }

        br::selection {
          background: transparent;
        }

        .endOfContent {
          display: block;
          position: absolute;
          inset: 100% 0 0;
          z-index: 0;
          cursor: default;
          user-select: none;
        }

        &.selecting .endOfContent {
          top: 0;
        }

        &[data-main-rotation='90'] {
          transform: rotate(90deg) translateY(-100%);
        }
        &[data-main-rotation='180'] {
          transform: rotate(180deg) translate(-100%, -100%);
        }
        &[data-main-rotation='270'] {
          transform: rotate(270deg) translateX(-100%);
        }
      }
    }
  `,
  template: `
    <div
      class="flex h-8 shrink-0 items-center gap-1 border-b border-slate-200 px-4 text-xs text-slate-600"
    >
      @if (pageCount(); as count) {
        <span>{{ count }} {{ count === 1 ? 'page' : 'pages' }}</span>
      }
      <div class="ml-auto flex items-center gap-1">
        <button
          type="button"
          class="rounded px-1.5 py-0.5 hover:bg-slate-200 disabled:opacity-40"
          title="Zoom out"
          aria-label="Zoom out"
          [disabled]="!document() || scale() <= zoomSteps[0]"
          (click)="zoomBy(-1)"
        >
          <fa-icon [icon]="faMagnifyingGlassMinus" />
        </button>
        <span class="w-10 text-center tabular-nums">{{ percent() }}</span>
        <button
          type="button"
          class="rounded px-1.5 py-0.5 hover:bg-slate-200 disabled:opacity-40"
          title="Zoom in"
          aria-label="Zoom in"
          [disabled]="!document() || scale() >= zoomSteps[zoomSteps.length - 1]"
          (click)="zoomBy(1)"
        >
          <fa-icon [icon]="faMagnifyingGlassPlus" />
        </button>
        <button
          type="button"
          class="rounded px-1.5 py-0.5 hover:bg-slate-200 disabled:opacity-40"
          title="Fit to width"
          aria-label="Fit to width"
          [class.bg-slate-200]="zoom() === 'fit'"
          [attr.aria-pressed]="zoom() === 'fit'"
          [disabled]="!document()"
          (click)="zoom.set('fit')"
        >
          <fa-icon [icon]="faLeftRight" />
        </button>
      </div>
    </div>

    @if (error(); as message) {
      <div
        class="flex flex-1 flex-col items-center justify-center gap-2 px-6 text-center text-sm text-rose-700"
        role="alert"
      >
        <fa-icon class="text-2xl" [icon]="faCircleExclamation" />
        <p class="max-w-xl break-words">{{ message }}</p>
      </div>
    } @else if (!document()) {
      <div class="flex flex-1 items-center justify-center gap-2 text-sm text-slate-500">
        <fa-icon [icon]="faSpinner" animation="spin" /> Loading PDF…
      </div>
    }
    <div
      #scroller
      class="min-h-0 flex-1 overflow-auto bg-slate-200 p-4"
      [class.hidden]="!document() || error()"
      role="region"
      tabindex="0"
      aria-label="PDF document"
    ></div>
  `,
})
export class PdfViewer {
  /** The PDF file's bytes. */
  readonly data = input.required<Uint8Array<ArrayBuffer>>();

  protected readonly zoom = signal<Zoom>('fit');
  protected readonly document = signal<PDFDocumentProxy | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly pageCount = computed(() => this.document()?.numPages ?? 0);
  /** The scale the pages are drawn at, resolved from `zoom`. */
  protected readonly scale = signal(1);
  protected readonly percent = computed(() => `${Math.round(this.scale() * 100)}%`);

  protected readonly zoomSteps = ZOOM_STEPS;
  protected readonly faMagnifyingGlassMinus = faMagnifyingGlassMinus;
  protected readonly faMagnifyingGlassPlus = faMagnifyingGlassPlus;
  protected readonly faLeftRight = faLeftRight;
  protected readonly faSpinner = faSpinner;
  protected readonly faCircleExclamation = faCircleExclamation;

  private readonly destroyRef = inject(DestroyRef);
  private readonly scroller = viewChild.required<ElementRef<HTMLElement>>('scroller');
  /** The scroll area's inner width, updated as it resizes. */
  private readonly width = signal(0);
  private pdfjs: PdfJs | null = null;
  private slots: PageSlot[] = [];
  private observer: IntersectionObserver | null = null;

  constructor() {
    afterNextRender(() => {
      const element = this.scroller().nativeElement;
      const resize = new ResizeObserver(() => this.width.set(element.clientWidth));
      resize.observe(element);
      this.destroyRef.onDestroy(() => resize.disconnect());
    });

    // Loads the document whenever the bytes change; the cleanup destroys it (on change or when
    // the viewer is destroyed), which also stops pending page renders.
    effect((onCleanup) => {
      const data = this.data();
      let cancelled = false;
      let destroy: (() => void) | null = null;
      this.document.set(null);
      this.error.set(null);
      onCleanup(() => {
        cancelled = true;
        destroy?.();
      });

      void (async () => {
        try {
          const pdfjs = (this.pdfjs ??= await loadPdfJs());
          if (cancelled) return;
          // pdf.js takes ownership of the buffer it is given, so it gets a copy.
          const task = pdfjs.getDocument({ data: data.slice(), ...pdfAssetUrls() });
          destroy = () => void task.destroy();
          const document = await task.promise;
          if (!cancelled) this.document.set(document);
        } catch (e) {
          if (!cancelled) this.error.set(describeError(this.pdfjs, e));
        }
      })();
    });

    // Lays the pages out again when the document, the zoom or (when fitting) the width changes.
    effect((onCleanup) => {
      const document = this.document();
      const zoom = this.zoom();
      const width = zoom === 'fit' ? this.width() : 0;
      if (!document || (zoom === 'fit' && width === 0)) return;
      let cancelled = false;
      onCleanup(() => {
        cancelled = true;
        this.clearPages();
      });
      void untracked(async () => {
        try {
          const pages = await Promise.all(
            Array.from({ length: document.numPages }, (_, i) => document.getPage(i + 1)),
          );
          if (cancelled) return;
          this.layout(pages, zoom, width);
        } catch (e) {
          if (!cancelled) this.error.set(describeError(this.pdfjs, e));
        }
      });
    });
  }

  protected zoomBy(direction: 1 | -1): void {
    const current = this.scale();
    const next =
      direction > 0
        ? ZOOM_STEPS.find((step) => step > current + 0.001)
        : [...ZOOM_STEPS].reverse().find((step) => step < current - 0.001);
    if (next) this.zoom.set(next);
  }

  /** Creates an empty, correctly sized box per page and draws each one as it comes into view. */
  private layout(pages: PDFPageProxy[], zoom: Zoom, width: number): void {
    const container = this.scroller().nativeElement;
    // Fit to width uses the widest page so that no page needs horizontal scrolling.
    const widest = Math.max(...pages.map((p) => p.getViewport({ scale: 1 }).width));
    const scale = zoom === 'fit' ? Math.max(0.1, (width - 2 * GUTTER) / widest) : zoom;
    this.scale.set(scale);

    this.observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          const slot = this.slots.find((s) => s.element === entry.target);
          if (!slot) continue;
          if (entry.isIntersecting && !slot.rendered) this.renderPage(slot, scale);
          // Pages far out of view are released, so only a few canvases are held at a time.
          else if (!entry.isIntersecting && slot.rendered) this.releasePage(slot);
        }
      },
      // Draws pages a little before they scroll into view, and keeps them a little after.
      { root: container, rootMargin: '50% 0px' },
    );

    for (const page of pages) {
      const viewport = page.getViewport({ scale });
      const element = document.createElement('div');
      element.className = 'pdf-page';
      element.style.width = `${Math.floor(viewport.width)}px`;
      element.style.height = `${Math.floor(viewport.height)}px`;
      element.style.setProperty('--total-scale-factor', String(scale * viewport.userUnit));
      element.setAttribute('aria-label', `Page ${page.pageNumber}`);
      container.appendChild(element);
      this.slots.push({ page, element, rendered: false });
      this.observer.observe(element);
    }
  }

  private renderPage(slot: PageSlot, scale: number): void {
    const pdfjs = this.pdfjs;
    if (!pdfjs) return;
    slot.rendered = true;
    const viewport = slot.page.getViewport({ scale });
    // Draws at the screen's pixel density so text stays sharp, capped by the canvas size limits
    // (in both directions, so very tall or wide pages still render) to keep memory in check.
    const width = Math.max(viewport.width, 1);
    const height = Math.max(viewport.height, 1);
    const ratio = Math.min(
      window.devicePixelRatio || 1,
      3,
      Math.sqrt(MAX_CANVAS_PIXELS / (width * height)),
      MAX_CANVAS_SIDE / width,
      MAX_CANVAS_SIDE / height,
    );
    const canvas = document.createElement('canvas');
    canvas.width = Math.floor(viewport.width * ratio);
    canvas.height = Math.floor(viewport.height * ratio);
    slot.element.appendChild(canvas);

    slot.task = slot.page.render({
      canvas,
      viewport,
      transform: ratio === 1 ? undefined : [ratio, 0, 0, ratio, 0, 0],
    });
    slot.task.promise.catch((e: unknown) => {
      if (!(e instanceof pdfjs.RenderingCancelledException)) console.error(e);
    });

    const textDiv = document.createElement('div');
    textDiv.className = 'textLayer';
    slot.element.appendChild(textDiv);
    const textLayer = new pdfjs.TextLayer({
      textContentSource: slot.page.streamTextContent(),
      container: textDiv,
      viewport,
    });
    slot.cancelText = () => textLayer.cancel();
    textLayer.render().catch(() => {
      // Text selection is a nicety: the page is still shown without it.
    });
  }

  /** Drops a page's canvas and text layer; it is drawn again when it scrolls back into view. */
  private releasePage(slot: PageSlot): void {
    slot.task?.cancel();
    slot.cancelText?.();
    slot.task = undefined;
    slot.cancelText = undefined;
    for (const canvas of slot.element.querySelectorAll('canvas')) {
      // Zero-sized canvases let the webview free their buffers right away.
      canvas.width = 0;
      canvas.height = 0;
    }
    slot.element.replaceChildren();
    slot.rendered = false;
  }

  private clearPages(): void {
    this.observer?.disconnect();
    this.observer = null;
    for (const slot of this.slots) {
      slot.task?.cancel();
      slot.cancelText?.();
      // Fails harmlessly when the document was already destroyed.
      try {
        slot.page.cleanup();
      } catch {}
      slot.element.remove();
    }
    this.slots = [];
  }
}

function describeError(pdfjs: PdfJs | null, e: unknown): string {
  if (pdfjs && e instanceof pdfjs.PasswordException) {
    return 'The PDF is password-protected and cannot be previewed.';
  }
  const message = e instanceof Error ? e.message : String(e);
  return `The PDF could not be shown: ${message}`;
}
