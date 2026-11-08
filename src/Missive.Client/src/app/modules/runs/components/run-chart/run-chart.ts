import { Component, computed, input } from '@angular/core';
import { RunTimelinePointDto } from '../../../../models/messages';

const WIDTH = 600;
const HEIGHT = 120;

/** Throughput (bars) and average latency (line) for each second of a run. */
@Component({
  selector: 'app-run-chart',
  templateUrl: './run-chart.html',
})
export class RunChart {
  readonly points = input.required<RunTimelinePointDto[]>();

  protected readonly width = WIDTH;
  protected readonly height = HEIGHT;

  protected readonly bars = computed(() => {
    const points = this.points();
    const max = Math.max(1, ...points.map((p) => p.samples));
    const slot = WIDTH / Math.max(1, points.length);
    return points.map((p, i) => ({
      x: i * slot,
      width: Math.max(1, slot - 1),
      height: (p.samples / max) * HEIGHT,
      errors: p.errors > 0,
    }));
  });

  protected readonly line = computed(() => {
    const points = this.points();
    const max = Math.max(1, ...points.map((p) => p.averageMs));
    const slot = WIDTH / Math.max(1, points.length);
    return points
      .map(
        (p, i) =>
          `${(i * slot + slot / 2).toFixed(1)},${(HEIGHT - (p.averageMs / max) * HEIGHT).toFixed(1)}`,
      )
      .join(' ');
  });
}
