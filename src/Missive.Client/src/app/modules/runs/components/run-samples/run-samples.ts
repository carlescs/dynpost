import { DecimalPipe } from '@angular/common';
import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faChevronLeft, faChevronRight, faCircleInfo } from '@fortawesome/free-solid-svg-icons';
import { RunResultDto, RunSampleDto, RunSamplesPageDto } from '../../../../models/messages';
import { HttpMethod, METHOD_COLORS } from '../../../../models/workspace';
import { Bridge } from '../../../../services/bridge';
import { formatElapsed, formatMs, statusFill } from '../../models/run-format';

export const PAGE_SIZE = 100;

/**
 * Every request a run sent with its response, as a paged table read from the run's request log
 * (`run.samples`). While the run goes on, the page shown is read again on each update.
 */
@Component({
  selector: 'app-run-samples',
  imports: [DecimalPipe, FaIconComponent],
  host: { class: 'flex min-h-0 flex-col gap-2' },
  templateUrl: './run-samples.html',
})
export class RunSamples {
  private readonly bridge = inject(Bridge);

  readonly run = input.required<RunResultDto>();

  protected readonly faChevronLeft = faChevronLeft;
  protected readonly faChevronRight = faChevronRight;
  protected readonly faCircleInfo = faCircleInfo;
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
          this.expanded.set(null);
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
    this.expanded.set(null);
    this.offset.set(0);
    this.failedOnly.set(failedOnly);
  }

  protected previous(): void {
    this.expanded.set(null);
    this.offset.update((offset) => Math.max(0, offset - PAGE_SIZE));
  }

  protected next(): void {
    this.expanded.set(null);
    this.offset.update((offset) => offset + PAGE_SIZE);
  }

  protected toggle(index: number): void {
    this.expanded.update((current) => (current === index ? null : index));
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
