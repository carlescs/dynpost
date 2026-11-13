import { RunPlanFileDto } from '../../../models/messages';
import { buildRunTree } from './run-tree';

const plan = (path: string, name: string | null = null): RunPlanFileDto => ({
  path,
  name,
  error: null,
});

describe('buildRunTree', () => {
  it('nests plans in their folders, folders first and sorted by name', () => {
    const tree = buildRunTree(
      ['perf', 'perf/nightly', 'empty'],
      [plan('perf/b.run.yaml', 'Beta'), plan('perf/a.run.yaml', 'alpha'), plan('top.run.yaml')],
    );

    expect(tree.map((n) => n.path)).toEqual(['empty', 'perf', 'top.run.yaml']);
    expect(tree[1].children!.map((n) => n.name)).toEqual(['nightly', 'alpha', 'Beta']);
    expect(tree[0].children).toEqual([]);
  });

  it('names plans without a name after their file', () => {
    expect(buildRunTree([], [plan('perf/smoke.run.yaml')])[0].children![0].name).toBe(
      'smoke.run.yaml',
    );
  });

  it('adds the folders of plans that the folder list lacks', () => {
    const tree = buildRunTree([], [plan('a/b/c.run.yaml', 'C')]);

    expect(tree[0].path).toBe('a');
    expect(tree[0].children![0].path).toBe('a/b');
    expect(tree[0].children![0].children![0].path).toBe('a/b/c.run.yaml');
  });

  it('shows the contents of the plans folder as the top level', () => {
    const tree = buildRunTree(
      ['plans/perf'],
      [plan('plans/top.run.yaml', 'Top'), plan('plans/perf/a.run.yaml', 'A')],
      'plans',
    );

    expect(tree.map((n) => n.path)).toEqual(['plans/perf', 'plans/top.run.yaml']);
    expect(tree[0].children!.map((n) => n.path)).toEqual(['plans/perf/a.run.yaml']);
  });
});
