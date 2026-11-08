import { TestBed } from '@angular/core/testing';
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

  it('explains when the yaml is invalid', () => {
    const { element } = create('a: [');
    expect(element.textContent).toContain('isn\u2019t valid YAML'.replace('\u2019', "'"));
  });
});
