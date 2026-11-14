import {
  addAssertion,
  addBody,
  readPlanForm,
  removeAssertion,
  removeBody,
  setAssertionStatus,
  setPlanEnvironmentOrder,
  setPlanEnvironments,
  setPlanOrder,
  setPlanRequestEnabled,
  setPlanRequests,
  setPlanSaveResponses,
  setPlanThinkTime,
  setPlanValue,
} from './run-plan-form';

const PLAN = [
  'name: Smoke',
  'requests:',
  '  - a.yaml',
  'load:',
  '  threads: 5 # users',
  '  iterations: 10',
  '',
].join('\n');

describe('run plan form', () => {
  it('reads the fields as text', () => {
    const form = readPlanForm(PLAN)!;
    expect(form.name).toBe('Smoke');
    expect(form.requests).toEqual(['a.yaml']);
    expect(form.threads).toBe('5');
    expect(form.duration).toBe('');
  });

  it('reads and writes the request order and weights', () => {
    let next = setPlanOrder(PLAN, 'random');
    expect(next).toContain('requests:\n  - a.yaml\norder: random\nload:');
    next = setPlanValue(next, ['weights', 'a.yaml'], '3');
    const form = readPlanForm(next)!;
    expect(form.order).toBe('random');
    expect(form.weights).toEqual({ 'a.yaml': '3' });
    expect(next.indexOf('weights:')).toBeLessThan(next.indexOf('load:'));
    expect(setPlanOrder(next, 'sequential')).not.toContain('order:');
  });

  it('reads and writes the environments, their order and weights', () => {
    expect(readPlanForm(PLAN)!.environments).toEqual([]);
    let next = setPlanEnvironments(PLAN, ['environments/dev.yaml', 'environments/load.yaml']);
    expect(next).toContain(
      'requests:\n  - a.yaml\nenvironments:\n  - environments/dev.yaml\n  - environments/load.yaml\nload:',
    );
    next = setPlanEnvironmentOrder(next, 'random');
    next = setPlanValue(next, ['environmentWeights', 'environments/load.yaml'], '3');
    const form = readPlanForm(next)!;
    expect(form.environments).toEqual(['environments/dev.yaml', 'environments/load.yaml']);
    expect(form.environmentOrder).toBe('random');
    expect(form.environmentWeights).toEqual({ 'environments/load.yaml': '3' });
    expect(next.indexOf('environmentWeights:')).toBeLessThan(next.indexOf('load:'));
    expect(setPlanEnvironmentOrder(next, 'sequential')).not.toContain('environmentOrder');
  });

  it('drops the weights of removed environments, and the order with the last one', () => {
    let plan = setPlanEnvironments(PLAN, ['environments/dev.yaml', 'environments/load.yaml']);
    plan = setPlanEnvironmentOrder(plan, 'random');
    plan = setPlanValue(plan, ['environmentWeights', 'environments/load.yaml'], '2');
    const one = setPlanEnvironments(plan, ['environments/dev.yaml']);
    expect(one).not.toContain('environmentWeights');
    expect(one).toContain('environmentOrder: random');
    expect(setPlanEnvironments(one, [])).toBe(PLAN);
  });

  it('turns saving responses on and off', () => {
    expect(readPlanForm(PLAN)!.saveResponses).toBe(false);
    const on = setPlanSaveResponses(PLAN, true);
    expect(on).toBe(PLAN + 'saveResponses: true\n');
    expect(readPlanForm(on)!.saveResponses).toBe(true);
    expect(setPlanSaveResponses(on, false)).toBe(PLAN);
  });

  it('drops the weights of removed requests', () => {
    const plan = setPlanValue(
      setPlanRequests(PLAN, ['a.yaml', 'b.yaml']),
      ['weights', 'b.yaml'],
      '2',
    );
    expect(setPlanRequests(plan, ['a.yaml'])).not.toContain('weights');
  });

  it('adds, reads, edits and removes body folders', () => {
    let next = addBody(setPlanRequests(PLAN, ['a.yaml', 'users/create.yml']), 'users/create.yml');
    expect(next).toContain('bodies:\n  users/create.yml:\n    folder: bodies/create\n');
    next = setPlanValue(next, ['bodies', 'users/create.yml', 'mode'], 'random');
    next = setPlanValue(next, ['bodies', 'users/create.yml', 'pattern'], '*.json');
    expect(next).toContain('folder: bodies/create\n    pattern: "*.json"\n    mode: random\n');
    expect(next.indexOf('bodies:')).toBeGreaterThan(next.indexOf('load:'));
    expect(readPlanForm(next)!.bodies).toEqual([
      {
        key: 'users/create.yml',
        folder: 'bodies/create',
        pattern: '*.json',
        mode: 'random',
        onEof: '',
      },
    ]);
    expect(addBody(next, 'users/create.yml')).toBe(next);
    expect(removeBody(next, 'users/create.yml')).not.toContain('bodies');
  });

  it('drops the body folders of removed requests', () => {
    const plan = addBody(setPlanRequests(PLAN, ['a.yaml', 'b.yaml']), 'b.yaml');
    expect(setPlanRequests(plan, ['a.yaml'])).not.toContain('bodies');
  });

  it('turns requests off and on with the disabled list', () => {
    const two = setPlanRequests(PLAN, ['a.yaml', 'b.yaml']);
    const off = setPlanRequestEnabled(two, 'b.yaml', false);
    expect(off).toContain('  - b.yaml\ndisabled:\n  - b.yaml\nload:');
    expect(readPlanForm(off)!.disabled).toEqual(['b.yaml']);
    expect(setPlanRequestEnabled(off, 'b.yaml', false)).toBe(off);
    expect(setPlanRequestEnabled(off, 'b.yaml', true)).toBe(two);
    expect(readPlanForm(PLAN)!.disabled).toEqual([]);
  });

  it('drops the disabled mark of removed requests', () => {
    const plan = setPlanRequestEnabled(
      setPlanRequests(PLAN, ['a.yaml', 'b.yaml']),
      'b.yaml',
      false,
    );
    expect(setPlanRequests(plan, ['a.yaml'])).not.toContain('disabled');
    expect(setPlanRequests(plan, ['b.yaml', 'a.yaml'])).toContain('disabled:\n  - b.yaml');
  });

  it('returns null for invalid yaml', () => {
    expect(readPlanForm('a: [')).toBeNull();
    expect(readPlanForm('- x')).toBeNull();
  });

  it('keeps comments and unrelated text when editing', () => {
    const next = setPlanValue(PLAN, ['load', 'iterations'], '20');
    expect(next).toBe(PLAN.replace('iterations: 10', 'iterations: 20'));
  });

  it('returns the same text when nothing changes', () => {
    expect(setPlanValue(PLAN, ['name'], 'Smoke')).toBe(PLAN);
  });

  it('removes a key and its empty parent', () => {
    const next = setPlanValue(setPlanValue(PLAN, ['data', 'file'], 'u.csv'), ['data', 'file'], '');
    expect(next).toBe(PLAN);
  });

  it('writes new sections in the documented order', () => {
    const next = setPlanValue(PLAN, ['thresholds', 'p95'], '500ms');
    const withData = setPlanValue(next, ['data', 'file'], 'u.csv');
    expect(withData.indexOf('data:')).toBeLessThan(withData.indexOf('thresholds:'));
    expect(withData.indexOf('load:')).toBeLessThan(withData.indexOf('data:'));
  });

  it('switches between duration and iterations', () => {
    let next = setPlanValue(PLAN, ['load', 'iterations'], '');
    next = setPlanValue(next, ['load', 'duration'], '2m');
    expect(readPlanForm(next)).toMatchObject({ iterations: '', duration: '2m' });
  });

  it('replaces the request list', () => {
    const next = setPlanRequests(PLAN, ['a.yaml', 'b/c.yaml']);
    expect(readPlanForm(next)!.requests).toEqual(['a.yaml', 'b/c.yaml']);
  });

  it('writes think time as a value or a range', () => {
    const fixed = setPlanThinkTime(PLAN, '100ms', '');
    expect(fixed).toContain('thinkTime: 100ms');
    const range = setPlanThinkTime(fixed, '50ms', '300ms');
    expect(readPlanForm(range)).toMatchObject({ thinkMin: '50ms', thinkMax: '300ms' });
    expect(setPlanThinkTime(range, '50ms', '')).toContain('thinkTime: 50ms');
  });

  it('edits assertions', () => {
    let next = addAssertion(PLAN, '*');
    next = setAssertionStatus(next, '*', '200, 201');
    next = setPlanValue(next, ['assert', '*', 'maxTime'], '800ms');
    expect(readPlanForm(next)!.assertions).toEqual([
      { key: '*', status: '200, 201', maxTime: '800ms', bodyContains: '', bodyNotContains: '' },
    ]);
    expect(removeAssertion(next, '*')).toBe(PLAN);
  });

  it('leaves invalid text alone', () => {
    expect(setPlanValue('a: [', ['name'], 'x')).toBe('a: [');
  });
});
