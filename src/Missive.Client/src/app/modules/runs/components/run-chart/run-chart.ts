import { Component, computed, input, signal } from '@angular/core';
import { RunTimelinePointDto } from '../../../../models/messages';
import { formatMs, niceMax } from '../../models/run-format';

/** The plots are drawn in these units and stretched to the space they get. */
const WIDTH = 1000;
const HEIGHT = 100;
/** Units left between the two parts of a stacked bar. */
const GAP = 2;

/**
 * Two small charts over the seconds of a run, sharing their time axis and hover: requests per
 * second (failed ones stacked on top) and average latency.
 */
@Component({
  selector: 'app-run-chart',
  host: { class: 'block' },
  templateUrl: './run-chart.html',
})
export class RunChart {
  readonly points = input.required<RunTimelinePointDto[]>();

  protected readonly viewBox = `0 0 ${WIDTH} ${HEIGHT}`;
  protected readonly formatMs = formatMs;

  /** The second under the pointer. */
  protected readonly hovered = signal<number | null>(null);

  /** How many seconds the time axis covers. */
  protected readonly seconds = computed(
    () => Math.max(0, ...this.points().map((p) => p.second)) + 1,
  );
  private readonly slot = computed(() => WIDTH / this.seconds());

  protected readonly maxSamples = computed(() =>
    niceMax(Math.max(0, ...this.points().map((p) => p.samples))),
  );
  protected readonly maxLatency = computed(() =>
    niceMax(Math.max(0, ...this.points().map((p) => p.averageMs))),
  );

  protected readonly bars = computed(() => {
    const slot = this.slot();
    const width = Math.max(1, slot * 0.8);
    const scale = HEIGHT / this.maxSamples();
    return this.points().map((p) => {
      const failed = p.errors * scale;
      const succeeded = Math.max(0, (p.samples - p.errors) * scale - (failed > 0 ? GAP : 0));
      return {
        x: p.second * slot + (slot - width) / 2,
        width,
        succeeded,
        failed,
        failedY: HEIGHT - p.samples * scale,
      };
    });
  });

  private readonly linePoints = computed(() => {
    const slot = this.slot();
    const scale = HEIGHT / this.maxLatency();
    const points = this.points().map((p) => ({
      x: p.second * slot + slot / 2,
      y: HEIGHT - p.averageMs * scale,
    }));
    // A single second still gets a visible stroke, across its slot.
    return points.length === 1
      ? [
          { x: points[0].x - slot / 2, y: points[0].y },
          { x: points[0].x + slot / 2, y: points[0].y },
        ]
      : points;
  });

  protected readonly line = computed(() =>
    this.linePoints()
      .map((p) => `${p.x.toFixed(1)},${p.y.toFixed(1)}`)
      .join(' '),
  );

  protected readonly area = computed(() => {
    const points = this.linePoints();
    if (points.length === 0) return '';
    return `${points[0].x.toFixed(1)},${HEIGHT} ${this.line()} ${points[points.length - 1].x.toFixed(1)},${HEIGHT}`;
  });

  /** The hovered second: its numbers and where its marks are, in percent of the plot. */
  protected readonly tip = computed(() => {
    const second = this.hovered();
    const point = second === null ? null : this.points().find((p) => p.second === second);
    if (!point) return null;
    const left = ((point.second + 0.5) / this.seconds()) * 100;
    return {
      point,
      left,
      /** The tooltip hangs to the left of the crosshair in the right half, so it stays inside. */
      flip: left > 50,
      latencyTop: 100 - (point.averageMs / this.maxLatency()) * 100,
    };
  });

  protected readonly summary = computed(() => {
    const points = this.points();
    const samples = points.reduce((sum, p) => sum + p.samples, 0);
    const errors = points.reduce((sum, p) => sum + p.errors, 0);
    return `${samples} requests over ${this.seconds()} seconds, ${errors} failed.`;
  });

  protected hover(event: MouseEvent): void {
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    if (rect.width === 0) return;
    const second = Math.floor(((event.clientX - rect.left) / rect.width) * this.seconds());
    this.hovered.set(Math.min(this.seconds() - 1, Math.max(0, second)));
  }
}
