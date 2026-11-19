import { EMPTY_PLAN, planChanges } from './plan-diff-model';
import { PlanForm } from './run-plan-form';

const plan = (patch: Partial<PlanForm>): PlanForm => ({ ...EMPTY_PLAN, ...patch });

const section = (before: PlanForm, after: PlanForm, id: string) =>
  planChanges(before, after).find((s) => s.id === id)!;

describe('plan diff model', () => {
  it('finds no changes between equal plans', () => {
    const a = plan({ name: 'A', requests: ['requests/a.yaml'], threads: '5' });

    expect(planChanges(a, a).every((s) => s.changes === 0)).toBe(true);
  });

  it('compares the requests with their weights and enabled state', () => {
    const before = plan({ requests: ['requests/a.yaml', 'requests/b.yaml'] });
    const after = plan({
      requests: ['requests/a.yaml', 'requests/c.yaml'],
      disabled: ['requests/a.yaml'],
      weights: { 'requests/c.yaml': '3' },
    });

    const requests = section(before, after, 'requests');

    expect(requests.changes).toBe(3);
    expect(requests.rows.map((r) => r.kind)).toEqual(['removed', 'changed', 'added']);
    expect(requests.rows[0].row.key).toBe('requests/b.yaml');
    expect(requests.rows[1].row).toEqual({ key: 'requests/a.yaml', value: '', enabled: false });
    expect(requests.rows[2].row).toEqual({ key: 'requests/c.yaml', value: '3', enabled: true });
  });

  it('pairs a load setting that changed', () => {
    const load = section(
      plan({ threads: '5', thinkMin: '100ms' }),
      plan({ threads: '10', thinkMin: '100ms', thinkMax: '1s', order: 'shuffle' }),
      'load',
    );

    expect(load.changes).toBe(3);
    expect(load.rows).toEqual([
      {
        kind: 'changed',
        before: { key: 'Virtual users (threads)', value: '5', enabled: true },
        row: { key: 'Virtual users (threads)', value: '10', enabled: true },
      },
      {
        kind: 'changed',
        before: { key: 'Think time', value: '100ms', enabled: true },
        row: { key: 'Think time', value: '100ms – 1s', enabled: true },
      },
      { kind: 'added', row: { key: 'Request order', value: 'Shuffled', enabled: true } },
    ]);
  });

  it('describes assertions, bodies and thresholds', () => {
    const after = plan({
      assertions: [
        { key: '*', status: '200', maxTime: '1s', bodyContains: '', bodyNotContains: 'error' },
      ],
      bodies: [
        { key: 'requests/a.yaml', folder: 'data/bodies', pattern: '*.json', mode: '', onEof: '' },
      ],
      thresholds: { ...EMPTY_PLAN.thresholds, p95: '500ms' },
      saveResponses: true,
    });

    expect(section(EMPTY_PLAN, after, 'assertions').rows[0].row).toEqual({
      key: 'Every request',
      value: 'status 200, time at most 1s, body doesn\'t contain "error"',
      enabled: true,
    });
    expect(section(EMPTY_PLAN, after, 'bodies').rows[0].row.value).toBe(
      'data/bodies, files *.json',
    );
    expect(section(EMPTY_PLAN, after, 'thresholds').rows[0].row).toEqual({
      key: 'p95 at most',
      value: '500ms',
      enabled: true,
    });
    expect(section(EMPTY_PLAN, after, 'data').rows[0].row.key).toBe('Save responses');
  });
});
