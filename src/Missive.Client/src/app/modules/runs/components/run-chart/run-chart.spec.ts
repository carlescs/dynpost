import { TestBed } from '@angular/core/testing';
import { RunChart } from './run-chart';

describe('RunChart', () => {
  it('positions bars and latency points using elapsed seconds', () => {
    const fixture = TestBed.createComponent(RunChart);
    fixture.componentRef.setInput('points', [
      { second: 0, samples: 1, errors: 0, averageMs: 10 },
      { second: 10, samples: 2, errors: 0, averageMs: 20 },
      { second: 20, samples: 1, errors: 0, averageMs: 30 },
    ]);

    fixture.detectChanges();

    const bars = fixture.nativeElement.querySelectorAll('rect');
    const line = fixture.nativeElement.querySelector('polyline');
    expect(Number(bars[1].getAttribute('x'))).toBeCloseTo((600 * 10) / 21);
    expect(Number(line.getAttribute('points').split(' ')[1].split(',')[0])).toBeCloseTo(300);
  });
});
