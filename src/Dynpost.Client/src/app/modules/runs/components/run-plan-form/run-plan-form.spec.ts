import { TestBed } from '@angular/core/testing';
import { EnvironmentStore } from '../../../../services/environment-store';
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

  it('offers the workspace environments, defaulting to the selected one', () => {
    const { fixture, element, emitted } = create(PLAN);
    const environments = TestBed.inject(EnvironmentStore);
    environments.environments.set([
      { id: 'dev.yaml', name: 'Dev' },
      { id: 'load.yaml', name: 'Load' },
    ]);
    environments.activeId.set('dev.yaml');
    fixture.detectChanges();
    const select = element.querySelector<HTMLSelectElement>('select[aria-label="Environment"]')!;
    const options = [...select.options].map((option) => option.textContent!.trim());
    expect(options).toEqual(['Selected environment (Dev)', 'Dev', 'Load']);
    expect(select.value).toBe('');

    select.value = 'environments/load.yaml';
    select.dispatchEvent(new Event('change'));
    expect(emitted).toEqual([
      PLAN.replace('name: Smoke\n', 'name: Smoke\nenvironment: environments/load.yaml\n'),
    ]);
  });

  it('clears the environment and flags one that is not in the workspace', () => {
    const plan = PLAN.replace(
      'name: Smoke\n',
      'name: Smoke\nenvironment: environments/gone.yaml\n',
    );
    const { element, emitted } = create(plan);
    const select = element.querySelector<HTMLSelectElement>('select[aria-label="Environment"]')!;
    expect(select.value).toBe('environments/gone.yaml');
    expect(element.querySelector('[data-environment-missing]')).not.toBeNull();

    select.value = '';
    select.dispatchEvent(new Event('change'));
    expect(emitted).toEqual([PLAN]);
  });

  it('sums up the load of the plan', () => {
    const { element } = create(PLAN);
    const summary = element.querySelector('[data-summary]')!.textContent!.replace(/\s+/g, ' ');
    expect(summary).toContain('5 users × 10 iterations × 1 request = 50 requests');
  });

  it('sends one request per iteration with the random order, showing each share', () => {
    const plan = PLAN.replace(
      '  - a.yaml\n',
      '  - a.yaml\n  - b.yaml\norder: random\nweights:\n  a.yaml: 3\n',
    );
    const { element } = create(plan);
    const summary = element.querySelector('[data-summary]')!.textContent!.replace(/\s+/g, ' ');
    expect(summary).toContain('5 users × 10 iterations × 1 request = 50 requests');
    const shares = [...element.querySelectorAll('[data-share]')].map((s) => s.textContent!.trim());
    expect(shares).toEqual(['75%', '25%']);
  });

  it('leaves disabled requests out of the load and the shares', () => {
    const plan = PLAN.replace(
      '  - a.yaml\n',
      '  - a.yaml\n  - b.yaml\n  - c.yaml\ndisabled:\n  - c.yaml\norder: random\n',
    );
    const { element } = create(plan);
    const steps = [...element.querySelectorAll('[data-step]')];
    expect(steps.map((step) => step.hasAttribute('data-disabled'))).toEqual([false, false, true]);
    expect(steps[2].textContent).toContain('Disabled: not sent');
    const shares = [...element.querySelectorAll('[data-share]')].map((s) => s.textContent!.trim());
    expect(shares).toEqual(['50%', '50%', '0%']);
  });

  it('writes a request switched off or on back as yaml', () => {
    const { element, emitted } = create(PLAN);
    const toggle = element.querySelector<HTMLInputElement>('[data-step] input[type="checkbox"]')!;
    expect(toggle.checked).toBe(true);
    toggle.checked = false;
    toggle.dispatchEvent(new Event('change'));
    expect(emitted).toEqual([PLAN.replace('load:', 'disabled:\n  - a.yaml\nload:')]);
  });

  it('asks to enable a request when every one is disabled', () => {
    const { element } = create(PLAN.replace('load:', 'disabled:\n  - a.yaml\nload:'));
    expect(element.querySelector('[data-none-enabled]')).not.toBeNull();
    const summary = element.querySelector('[data-summary]')!.textContent!.replace(/\s+/g, ' ');
    expect(summary).toContain('× 0 requests');
  });

  it('writes a chosen order and weight back as yaml', () => {
    const { element, emitted } = create(PLAN);
    const select = element.querySelector<HTMLSelectElement>('select[aria-label="Request order"]')!;
    select.value = 'shuffle';
    select.dispatchEvent(new Event('change'));
    expect(emitted).toEqual([PLAN.replace('load:', 'order: shuffle\nload:')]);
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

  it('adds a body folder for a request and edits it', () => {
    const { element, emitted, fixture } = create(PLAN);
    const select = element.querySelector<HTMLSelectElement>('[aria-label="Add body folder"]')!;
    select.value = 'a.yaml';
    select.dispatchEvent(new Event('change'));
    expect(emitted.at(-1)).toContain('bodies:\n  a.yaml:\n    folder: bodies/a\n');

    fixture.componentRef.setInput('yaml', emitted.at(-1)!);
    fixture.detectChanges();
    const body = element.querySelector('[data-body]')!;
    expect(body.textContent).toContain('a.yaml');
    const pattern = body.querySelectorAll('input')[1];
    pattern.value = '*.json';
    pattern.dispatchEvent(new Event('change'));
    expect(emitted.at(-1)).toContain('pattern: "*.json"');
  });

  it('explains when the yaml is invalid', () => {
    const { element } = create('a: [');
    expect(element.textContent).toContain('isn\u2019t valid YAML'.replace('\u2019', "'"));
  });
});
