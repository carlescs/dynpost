import { Component, computed, input } from '@angular/core';

/** The share of the width the ramp-up takes when the run has no set duration. */
const OPEN_RAMP = 35;

/**
 * A sketch of the load a plan produces: virtual users over time, ramping up and then holding
 * until the duration is over (or, without a duration, until every user finished its iterations).
 */
@Component({
  selector: 'app-load-profile',
  host: { class: 'block' },
  templateUrl: './load-profile.html',
})
export class LoadProfile {
  readonly threads = input.required<number>();
  /** Milliseconds over which the users start; 0 starts them all at once. */
  readonly rampUpMs = input(0);
  /** How long the run lasts, or `null` when it stops after a number of iterations. */
  readonly durationMs = input<number | null>(null);
  /** What the axes say: the ramp-up and the end as written in the plan. */
  readonly rampUpLabel = input('');
  readonly endLabel = input('');

  /** Where the ramp-up ends, in percent of the width. */
  protected readonly ramp = computed(() => {
    const ramp = Math.max(0, this.rampUpMs());
    const duration = this.durationMs();
    if (ramp === 0) return 0;
    return duration ? Math.min(100, (ramp / duration) * 100) : OPEN_RAMP;
  });

  protected readonly shape = computed(() => `0,100 ${this.ramp()},0 100,0 100,100`);
  protected readonly outline = computed(() => `0,100 ${this.ramp()},0 100,0`);

  protected readonly summary = computed(
    () =>
      `${this.threads()} virtual users` +
      (this.ramp() > 0 ? `, started over ${this.rampUpLabel()}` : ', all started at once') +
      `, running ${this.endLabel()}.`,
  );
}
