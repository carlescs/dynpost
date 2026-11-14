import { DecimalPipe } from '@angular/common';
import {
  afterEveryRender,
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
} from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faCircleInfo, faFilePdf, faSpinner } from '@fortawesome/free-solid-svg-icons';
import { PdfViewer } from '../../../../components/pdf-viewer/pdf-viewer';
import { decodeBase64 } from '../../../../models/base64';
import { RunResultDto, RunSampleDto, RunSamplesPageDto } from '../../../../models/messages';
import { HttpMethod, METHOD_COLORS } from '../../../../models/workspace';
import { httpStatusTooltip } from '../../../../models/http-status';
import { Bridge } from '../../../../services/bridge';
import { formatElapsed, formatMs, statusFill } from '../../models/run-format';

/** How many samples are read from the log at a time. */
export const PAGE_SIZE = 100;
/** The height of a row, in pixels, until one is measured. */
const ROW_HEIGHT = 25;
/** The height of the table's viewport, in pixels, until it is measured. */
const VIEWPORT_HEIGHT = 600;
/** How many rows are rendered above and below the visible ones. */
const OVERSCAN = 20;

/** A saved PDF response opened in the previewer, by the row's position in the log. */
interface OpenPdf {
  index: number;
  data: Uint8Array<ArrayBuffer> | null;
  error: string | null;
}

/** A page of the log read for the current filter, with the update of the run it was read at. */
interface CachedPage {
  epoch: number;
  samples: RunSampleDto[];
}

/** A rendered row: a sample, or `null` while its page is being read. */
interface Row {
  index: number;
  sample: RunSampleDto | null;
}

/**
 * Every request a run sent with its response, as a virtualized table read from the run's request
 * log (`run.samples`): only the rows in view (and a few around them) are rendered, and the log is
 * read a page at a time as they scroll into view. While the run goes on, the pages in view are
 * read again on each update. A PDF response the run saved (plans with `saveResponses`) opens in
 * the PDF previewer.
 */
@Component({
  selector: 'app-run-samples',
  imports: [DecimalPipe, FaIconComponent, PdfViewer],
  host: { class: 'flex min-h-0 flex-col gap-2' },
  templateUrl: './run-samples.html',
})
export class RunSamples {
  private readonly bridge = inject(Bridge);
  private readonly destroyRef = inject(DestroyRef);
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  readonly run = input.required<RunResultDto>();

  protected readonly faCircleInfo = faCircleInfo;
  protected readonly faFilePdf = faFilePdf;
  protected readonly faSpinner = faSpinner;
  protected readonly formatMs = formatMs;
  protected readonly formatElapsed = formatElapsed;
  protected readonly statusFill = statusFill;
  protected readonly statusTooltip = httpStatusTooltip;

  protected readonly failedOnly = signal(false);
  /** The counts of the log from the latest page read; `null` until one is. */
  protected readonly info = signal<Omit<RunSamplesPageDto, 'offset' | 'samples'> | null>(null);
  protected readonly error = signal<string | null>(null);
  /** The row whose response is shown, by its position in the log. */
  protected readonly expanded = signal<number | null>(null);
  /** The saved PDF response shown in the previewer, while it loads and once it has. */
  protected readonly pdf = signal<OpenPdf | null>(null);

  /** The pages read so far, by page number. */
  private readonly pages = signal<ReadonlyMap<number, CachedPage>>(new Map());
  /** Counts the run's updates: a page read at an earlier one is read again when in view. */
  private readonly epoch = signal(0);
  private readonly scrollTop = signal(0);
  private readonly viewportHeight = signal(VIEWPORT_HEIGHT);
  private readonly headerHeight = signal(0);
  private readonly rowHeight = signal(ROW_HEIGHT);
  /** The height of the expanded row's details, measured while they are rendered. */
  private readonly detailHeight = signal(0);

  /** The rows rendered: those in view and {@link OVERSCAN} on each side. */
  private readonly range = computed(
    () => {
      const count = this.info()?.matching ?? 0;
      const top = Math.max(0, this.scrollTop() - this.headerHeight());
      const first = this.rowAt(top) - OVERSCAN;
      const last = this.rowAt(top + this.viewportHeight()) + OVERSCAN + 1;
      const start = Math.min(Math.max(0, first), count);
      return { start, end: Math.min(Math.max(start, last), count) };
    },
    { equal: (a, b) => a.start === b.start && a.end === b.end },
  );

  protected readonly rows = computed<Row[]>(() => {
    const { start, end } = this.range();
    const pages = this.pages();
    return Array.from({ length: end - start }, (_, i) => {
      const index = start + i;
      const page = pages.get(Math.floor(index / PAGE_SIZE));
      return { index, sample: page?.samples[index % PAGE_SIZE] ?? null };
    });
  });

  /** The height of the rows above the rendered ones. */
  protected readonly topSpace = computed(() => {
    const { start } = this.range();
    const expanded = this.expanded();
    const detail = expanded !== null && expanded < start ? this.detailHeight() : 0;
    return start * this.rowHeight() + detail;
  });

  /** The height of the rows below the rendered ones. */
  protected readonly bottomSpace = computed(() => {
    const { end } = this.range();
    const count = this.info()?.matching ?? 0;
    const expanded = this.expanded();
    const detail = expanded !== null && expanded >= end ? this.detailHeight() : 0;
    return (count - end) * this.rowHeight() + detail;
  });

  /** The pages the rendered rows are on (the first one while the log is empty or unknown). */
  private readonly neededPages = computed(
    () => {
      const { start, end } = this.range();
      const first = Math.floor(start / PAGE_SIZE);
      const last = Math.floor(Math.max(start, end - 1) / PAGE_SIZE);
      return Array.from({ length: last - first + 1 }, (_, i) => first + i);
    },
    { equal: (a, b) => a.length === b.length && a.every((page, i) => page === b[i]) },
  );

  /** Discards the answers of reads made before the run or the filter changed. */
  private generation = 0;
  /** The reads in flight, as `epoch:page`. */
  private readonly pending = new Set<string>();
  private runId: string | null = null;

  constructor() {
    // Starts over when another run is shown and reads the pages in view again when it is updated.
    effect(() => {
      const run = this.run();
      untracked(() => {
        if (run.runId !== this.runId) {
          this.runId = run.runId;
          this.reset();
        } else {
          this.refresh();
        }
      });
    });

    // Reads the pages in view that were not read yet or were read before the latest update.
    effect(() => {
      const needed = this.neededPages();
      const epoch = this.epoch();
      const failedOnly = this.failedOnly();
      const pages = this.pages();
      untracked(() => {
        for (const page of needed) {
          if (pages.get(page)?.epoch !== epoch) void this.load(page, epoch, failedOnly);
        }
      });
    });

    afterNextRender(() => {
      const element = this.scroller()?.nativeElement;
      if (!element || typeof ResizeObserver === 'undefined') return;
      const resize = new ResizeObserver(() => this.measureViewport(element));
      resize.observe(element);
      this.destroyRef.onDestroy(() => resize.disconnect());
    });

    // Rows have the same height, but the expanded row's details don't: both are measured once
    // rendered, so the space kept for the rows that are not rendered matches theirs.
    afterEveryRender({ read: () => this.measureRows() });
  }

  protected onScroll(): void {
    const element = this.scroller()?.nativeElement;
    if (!element) return;
    this.scrollTop.set(element.scrollTop);
    // A focused row that leaves the rendered ones would be removed with focus on it, and the
    // browser would then scroll the table on its own: focus moves to the table first.
    const focused = element.querySelector<HTMLElement>('[data-sample]:focus');
    if (focused) {
      const index = Number(focused.dataset['index']);
      const { start, end } = this.range();
      if (index < start || index >= end) element.focus({ preventScroll: true });
    }
  }

  protected setFailedOnly(failedOnly: boolean): void {
    if (failedOnly === this.failedOnly()) return;
    this.failedOnly.set(failedOnly);
    this.reset();
  }

  protected toggle(index: number): void {
    this.expanded.update((current) => (current === index ? null : index));
    this.pdf.set(null);
  }

  /** Whether a sample's response is a PDF. */
  protected isPdf(sample: RunSampleDto): boolean {
    return sample.contentType === 'application/pdf';
  }

  /** Where the run saved a sample's response, relative to the workspace. */
  protected savedPath(file: string): string {
    return `.dynpost/runs/responses/${this.run().runId}/${file}`;
  }

  /** Reads a saved PDF response and shows it in the previewer (or hides it when it is shown). */
  protected async openPdf(index: number, sample: RunSampleDto): Promise<void> {
    if (!sample.responseFile) return;
    if (this.pdf()?.index === index) {
      this.pdf.set(null);
      return;
    }
    const open: OpenPdf = { index, data: null, error: null };
    this.pdf.set(open);
    try {
      const { body } = await this.bridge.send('run.response', {
        runId: this.run().runId,
        file: sample.responseFile,
      });
      if (this.pdf() === open) this.pdf.set({ ...open, data: decodeBase64(body) });
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      if (this.pdf() === open) this.pdf.set({ ...open, error: message });
    }
  }

  private collapse(): void {
    this.expanded.set(null);
    this.pdf.set(null);
  }

  protected methodColor(method: string): string {
    return METHOD_COLORS[method as HttpMethod] ?? 'text-slate-500';
  }

  protected dataRow(sample: RunSampleDto): [string, string][] {
    return Object.entries(sample.dataRow ?? {});
  }

  /** Forgets the pages read and goes back to the top of the table. */
  private reset(): void {
    this.generation++;
    this.pending.clear();
    this.collapse();
    this.detailHeight.set(0);
    this.pages.set(new Map());
    this.info.set(null);
    this.error.set(null);
    this.epoch.update((epoch) => epoch + 1);
    this.scrollTop.set(0);
    const element = this.scroller()?.nativeElement;
    if (element) element.scrollTop = 0;
  }

  /** Marks the pages read as out of date, keeping those in view on screen until read again. */
  private refresh(): void {
    const needed = new Set(this.neededPages());
    this.pages.update((pages) => new Map([...pages].filter(([page]) => needed.has(page))));
    this.epoch.update((epoch) => epoch + 1);
  }

  /** The row at a height into the table's body, counting the expanded row's details. */
  private rowAt(y: number): number {
    const height = this.rowHeight();
    const expanded = this.expanded();
    if (expanded === null || y < (expanded + 1) * height) return Math.floor(y / height);
    const detail = this.detailHeight();
    if (y < (expanded + 1) * height + detail) return expanded;
    return Math.floor((y - detail) / height);
  }

  private measureViewport(element: HTMLElement): void {
    const height = element.clientHeight;
    if (height > 0) this.viewportHeight.set(height);
  }

  private measureRows(): void {
    const element = this.scroller()?.nativeElement;
    if (!element) return;
    const header = element.querySelector('thead')?.getBoundingClientRect().height ?? 0;
    if (header > 0) this.headerHeight.set(header);
    const row = element.querySelector('[data-sample]')?.getBoundingClientRect().height ?? 0;
    if (row > 0) this.rowHeight.set(row);
    if (this.expanded() === null) {
      this.detailHeight.set(0);
    } else {
      const detail = element.querySelector('[data-detail]')?.getBoundingClientRect().height ?? 0;
      if (detail > 0) this.detailHeight.set(detail);
    }
  }

  private async load(page: number, epoch: number, failedOnly: boolean): Promise<void> {
    const key = `${epoch}:${page}`;
    if (this.pending.has(key)) return;
    this.pending.add(key);
    const generation = this.generation;
    try {
      const result = await this.bridge.send('run.samples', {
        runId: this.run().runId,
        offset: page * PAGE_SIZE,
        limit: PAGE_SIZE,
        failedOnly,
      });
      if (generation !== this.generation) return;
      this.pending.delete(key);
      const current = this.pages().get(page);
      if (current && current.epoch > epoch) return;
      this.pages.update((pages) => new Map(pages).set(page, { epoch, samples: result.samples }));
      if (epoch === this.epoch() || !this.info()) {
        const { matching, logged, sent } = result;
        this.info.set({ matching, logged, sent });
      }
      this.error.set(null);
    } catch (error) {
      if (generation !== this.generation) return;
      this.pending.delete(key);
      this.error.set(error instanceof Error ? error.message : String(error));
    }
  }
}
