import { describeMigration } from './layout-migration';

describe('describeMigration', () => {
  it('says what was moved and that nothing was committed', () => {
    const message = describeMigration({ requests: 3, plans: 1, leftInPlace: [] });

    expect(message).toContain('moved 3 request files into requests/ and 1 run plan into plans/');
    expect(message).toContain('Nothing was committed');
    expect(message).not.toContain('left where they are');
  });

  it('lists the files left in place, summing up long lists', () => {
    const leftInPlace = Array.from(
      { length: 7 },
      (_, i) => `f${i}.yaml ('requests/f${i}.yaml' already exists)`,
    );

    const message = describeMigration({ requests: 1, plans: 0, leftInPlace });

    expect(message).toContain('1 request file into requests/');
    expect(message).not.toContain('run plan into');
    expect(message).toContain('• f4.yaml');
    expect(message).not.toContain('• f5.yaml');
    expect(message).toContain('• and 2 more');
  });

  it('reports an error', () => {
    expect(
      describeMigration({ requests: 0, plans: 2, leftInPlace: [], error: 'disk full' }),
    ).toContain('Not everything could be updated: disk full');
  });
});
