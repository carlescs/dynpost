import { DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faChevronLeft,
  faChevronRight,
  faCircleInfo,
  faFilePdf,
  faSpinner,
} from '@fortawesome/free-solid-svg-icons';
import { PdfViewer } from '../../../../components/pdf-viewer/pdf-viewer';
import { decodeBase64 } from '../../../../models/base64';
import { RunResultDto, RunSampleDto, RunSamplesPageDto } from '../../../../models/messages';
import { HttpMethod, METHOD_COLORS } from '../../../../models/workspace';
import { Bridge } from '../../../../services/bridge';
import { formatElapsed, formatMs, statusFill } from '../../models/run-format';

export const PAGE_SIZE = 100;

/** A saved PDF response opened in the previewer, by the row's position in the log. */
interface OpenPdf {
  index: number;
  data: Uint8Array<ArrayBuffer> | null;
  error: string | null;
}

/**
 * Every request a run sent with its response, as a paged table read from the run's request log
 * (`run.samples`). While the run goes on, the page shown is read again on each update. A PDF
 * response the run saved (plans with `saveResponses`) opens in the PDF previewer.
 */
@Component({
  selector: 'app-run-samples',
  imports: [DecimalPipe, FaIconComponent, PdfViewer],
  host: { class: 'flex min-h-0 flex-col gap-2' },
  templateUrl: './run-samples.html',
})
export class RunSamples {
  private readonly bridge = inject(Bridge);

  readonly run = input.required<RunResultDto>();

  protected readonly faChevronLeft = faChevronLeft;
  protected readonly faChevronRight = faChevronRight;
  protected readonly faCircleInfo = faCircleInfo;
  protected readonly faFilePdf = faFilePdf;
  protected readonly faSpinner = faSpinner;
  protected readonly formatMs = formatMs;
  protected readonly formatElapsed = formatElapsed;
  protected readonly statusFill = statusFill;
  protected readonly pageSize = PAGE_SIZE;

  protected readonly offset = signal(0);
  protected readonly failedOnly = signal(false);
  protected readonly page = signal<RunSamplesPageDto | null>(null);
  protected readonly error = signal<string | null>(null);
  /** The row whose response is shown, by its position in the log. */
  protected readonly expanded = signal<number | null>(null);
  /** The saved PDF response shown in the previewer, while it loads and once it has. */
  protected readonly pdf = signal<OpenPdf | null>(null);

  protected readonly last = computed(() => {
    const page = this.page();
    return page ? page.offset + page.samples.length : 0;
  });
  protected readonly hasPrevious = computed(() => (this.page()?.offset ?? 0) > 0);
  protected readonly hasNext = computed(() => {
    const page = this.page();
    return !!page && this.last() < page.matching;
  });

  private generation = 0;
  private runId: string | null = null;

  constructor() {
    // Reads the page again whenever the run is updated, the filter changes or the user pages.
    effect(() => {
      const run = this.run();
      const failedOnly = this.failedOnly();
      const offset = this.offset();
      untracked(() => {
        if (run.runId !== this.runId) {
          this.runId = run.runId;
          this.collapse();
          if (offset !== 0) {
            this.offset.set(0);
            return;
          }
        }
        void this.load(run.runId, offset, failedOnly);
      });
    });
  }

  protected setFailedOnly(failedOnly: boolean): void {
    if (failedOnly === this.failedOnly()) return;
    this.collapse();
    this.offset.set(0);
    this.failedOnly.set(failedOnly);
  }

  protected previous(): void {
    this.collapse();
    this.offset.update((offset) => Math.max(0, offset - PAGE_SIZE));
  }

  protected next(): void {
    this.collapse();
    this.offset.update((offset) => offset + PAGE_SIZE);
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
    return `.missive/runs/responses/${this.run().runId}/${file}`;
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

  private async load(runId: string, offset: number, failedOnly: boolean): Promise<void> {
    const generation = ++this.generation;
    try {
      const page = await this.bridge.send('run.samples', {
        runId,
        offset,
        limit: PAGE_SIZE,
        failedOnly,
      });
      if (generation !== this.generation) return;
      this.page.set(page);
      this.error.set(null);
    } catch (error) {
      if (generation !== this.generation) return;
      this.error.set(error instanceof Error ? error.message : String(error));
    }
  }
}
