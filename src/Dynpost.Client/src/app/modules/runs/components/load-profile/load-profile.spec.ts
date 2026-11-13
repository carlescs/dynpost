import { TestBed } from '@angular/core/testing';
import { LoadProfile } from './load-profile';

describe('LoadProfile', () => {
  function create(inputs: Record<string, unknown>) {
    const fixture = TestBed.createComponent(LoadProfile);
    for (const [name, value] of Object.entries(inputs)) fixture.componentRef.setInput(name, value);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('ramps up over its share of the duration', () => {
    const element = create({
      threads: 10,
      rampUpMs: 30_000,
      durationMs: 120_000,
      rampUpLabel: '30s',
      endLabel: '2m',
    });
    expect(element.querySelector('polyline')!.getAttribute('points')).toBe('0,100 25,0 100,0');
    expect(element.textContent).toContain('10 users');
    expect(element.textContent).toContain('30s ramp-up');
    expect(element.textContent).toContain('2m');
  });

  it('starts every user at once without a ramp-up', () => {
    const element = create({ threads: 3, endLabel: 'until done' });
    expect(element.querySelector('polyline')!.getAttribute('points')).toBe('0,100 0,0 100,0');
    expect(element.textContent).not.toContain('ramp-up');
  });

  it('sketches the ramp-up when the run has no duration', () => {
    const element = create({
      threads: 3,
      rampUpMs: 5000,
      rampUpLabel: '5s',
      endLabel: 'until done',
    });
    expect(element.querySelector('polyline')!.getAttribute('points')).toBe('0,100 35,0 100,0');
  });
});
