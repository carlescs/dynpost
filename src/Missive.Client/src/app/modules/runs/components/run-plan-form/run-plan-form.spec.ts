import { TestBed } from '@angular/core/testing';
import { WorkspaceStore } from '../../../../services/workspace-store';
import { RunPlanForm } from './run-plan-form';

const PLAN = 'name: Smoke\nrequests:\n  - a.yaml\nload:\n  threads: 5\n  iterations: 10\n';

describe('RunPlanForm', () => {
  function create(yaml: string) {
    const fixture = TestBed.createComponent(RunPlanForm);
    fixture.componentRef.setInput('yaml', yaml);
    const emitted: string[] = [];
    fixture.componentInstance.yamlChange.subscribe((value) => emitted.push(value));
    fixture.detectChanges();
    return { fixture, emitted, element: fixture.nativeElement as HTMLElement };
  }

  it('shows the plan fields', () => {
    const { element } = create(PLAN);
    const values = [...element.querySelectorAll('input')].map((input) => input.value);
    expect(values).toContain('Smoke');
    expect(values).toContain('5');
    expect(element.textContent).toContain('a.yaml');
  });

  it('writes a changed field back as yaml', () => {
    const { element, emitted } = create(PLAN);
    const threads = [...element.querySelectorAll('input')].find((i) => i.value === '5')!;
    threads.value = '8';
    threads.dispatchEvent(new Event('change'));
    expect(emitted).toEqual([PLAN.replace('threads: 5', 'threads: 8')]);
  });

  it('shows each request as a step with its method, flagging missing ones', () => {
    TestBed.inject(WorkspaceStore).tree.set([
      { path: 'a.yaml', name: 'Get A', kind: 'request', method: 'GET' },
    ]);
    const { element } = create(PLAN.replace('  - a.yaml\n', '  - a.yaml\n  - gone.yaml\n'));
    const steps = [...element.querySelectorAll('[data-step]')].map((step) => step.textContent);
    expect(steps[0]).toContain('GET');
    expect(steps[0]).toContain('Get A');
    expect(steps[1]).toContain('Not found in the workspace');
  });

  it('sums up the load of the plan', () => {
    const { element } = create(PLAN);
    const summary = element.querySelector('[data-summary]')!.textContent!.replace(/\s+/g, ' ');
    expect(summary).toContain('5 users × 10 iterations × 1 request = 50 requests');
  });

  it('describes a timed run and sketches its ramp-up', () => {
    const plan =
      'name: Soak\nrequests:\n  - a.yaml\nload:\n  threads: 4\n  duration: 2m\n  rampUp: 30s\n';
    const { element } = create(plan);
    const summary = element.querySelector('[data-summary]')!.textContent!.replace(/\s+/g, ' ');
    expect(summary).toContain('4 users loop over 1 request for 2m');
    expect(element.querySelector('app-load-profile polyline')!.getAttribute('points')).toBe(
      '0,100 25,0 100,0',
    );
  });

  it('switches between stopping after iterations and after a duration', () => {
    const { element, emitted } = create(PLAN);
    const duration = [...element.querySelectorAll('button')].find((b) =>
      b.textContent!.includes('Duration'),
    )!;
    duration.click();
    expect(emitted).toEqual([PLAN.replace('iterations: 10', 'duration: 1m')]);
  });

  it('highlights the thresholds that are set', () => {
    const { element } = create(PLAN + 'thresholds:\n  p95: 500ms\n');
    expect(element.querySelector('[data-threshold="p95"]')!.className).toContain('border-sky-600');
    expect(element.querySelector('[data-threshold="p99"]')!.className).not.toContain(
      'border-sky-600',
    );
  });

  it('explains when the yaml is invalid', () => {
    const { element } = create('a: [');
    expect(element.textContent).toContain('isn\u2019t valid YAML'.replace('\u2019', "'"));
  });
});
