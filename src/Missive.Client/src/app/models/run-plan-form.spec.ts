import {
  addAssertion,
  readPlanForm,
  removeAssertion,
  setAssertionStatus,
  setPlanOrder,
  setPlanRequests,
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

  it('drops the weights of removed requests', () => {
    const plan = setPlanValue(
      setPlanRequests(PLAN, ['a.yaml', 'b.yaml']),
      ['weights', 'b.yaml'],
      '2',
    );
    expect(setPlanRequests(plan, ['a.yaml'])).not.toContain('weights');
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
