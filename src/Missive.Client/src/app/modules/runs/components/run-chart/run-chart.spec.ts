import { TestBed } from '@angular/core/testing';
import { RunChart } from './run-chart';

describe('RunChart', () => {
  function create() {
    const fixture = TestBed.createComponent(RunChart);
    fixture.componentRef.setInput('points', [
      { second: 0, samples: 40, errors: 0, averageMs: 10 },
      { second: 10, samples: 50, errors: 10, averageMs: 20 },
      { second: 19, samples: 20, errors: 0, averageMs: 40 },
    ]);
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  it('positions bars and latency points using elapsed seconds', () => {
    const { element } = create();
    const bars = element.querySelectorAll('rect[data-bar="succeeded"]');
    const line = element.querySelector('polyline')!;
    // 20 seconds share 1000 units: 50 each, with bars 40 wide centered in their slot; 20 ms on a
    // 0-50 ms axis is 40% up.
    expect(Number(bars[1].getAttribute('x'))).toBeCloseTo(505);
    expect(line.getAttribute('points')!.split(' ')[1]).toBe('525.0,60.0');
  });

  it('stacks failed requests on top of the succeeded ones', () => {
    const { element } = create();
    const failed = element.querySelectorAll('rect[data-bar="failed"]');
    expect(failed.length).toBe(1);
    // 50 samples fill the 0-50 axis; the 10 failed ones are its top fifth.
    expect(Number(failed[0].getAttribute('y'))).toBeCloseTo(0);
    expect(Number(failed[0].getAttribute('height'))).toBeCloseTo(20);
  });

  it('shows the numbers of the hovered second', () => {
    const { fixture, element } = create();
    const plot = element.querySelector<HTMLElement>('[data-plot]')!;
    plot.getBoundingClientRect = () => ({ left: 0, width: 200 }) as DOMRect;
    plot.dispatchEvent(new MouseEvent('mousemove', { clientX: 105 }));
    fixture.detectChanges();
    const tooltip = element.querySelector('[role="tooltip"]')!;
    expect(tooltip.textContent).toContain('Second 11');
    expect(tooltip.textContent).toContain('20 ms');

    plot.dispatchEvent(new MouseEvent('mouseleave'));
    fixture.detectChanges();
    expect(element.querySelector('[role="tooltip"]')).toBeNull();
  });
});
