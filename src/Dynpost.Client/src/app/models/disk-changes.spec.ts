import { DiskSnapshots, touches, touchesArea } from './disk-changes';

describe('touches', () => {
  it('matches the path itself and the folders holding it', () => {
    expect(touches(['requests/users/get.yaml'], 'requests/users/get.yaml')).toBe(true);
    expect(touches(['requests/users'], 'requests/users/get.yaml')).toBe(true);
    expect(touches(['requests/user'], 'requests/users/get.yaml')).toBe(false);
    expect(touches(['requests/users/get.yaml'], 'requests/users')).toBe(false);
  });

  it('matches anything without paths', () => {
    expect(touches(undefined, 'requests/a.yaml')).toBe(true);
  });
});

describe('touchesArea', () => {
  it('matches the folder, anything in it and the named top-level files', () => {
    expect(touchesArea(['requests'], 'requests')).toBe(true);
    expect(touchesArea(['requests/a.yaml'], 'requests')).toBe(true);
    expect(touchesArea(['requests-old/a.yaml'], 'requests')).toBe(false);
    expect(touchesArea(['.gitignore'], 'requests', '.gitignore')).toBe(true);
    expect(touchesArea(undefined, 'plans')).toBe(true);
  });
});

describe('DiskSnapshots', () => {
  it('tells content that changed from content written back', () => {
    const snapshots = new DiskSnapshots();
    expect(snapshots.differs('tab-1', { a: 1 })).toBe(true);

    snapshots.remember('tab-1', { a: 1 });
    expect(snapshots.differs('tab-1', { a: 1 })).toBe(false);
    expect(snapshots.differs('tab-1', { a: 2 })).toBe(true);

    snapshots.forget('tab-1');
    expect(snapshots.differs('tab-1', { a: 1 })).toBe(true);
  });

  it('asks one question at a time per tab', async () => {
    const snapshots = new DiskSnapshots();
    let answer!: (value: boolean) => void;
    const first = snapshots.ask('tab-1', () => new Promise((resolve) => (answer = resolve)));

    expect(await snapshots.ask('tab-1', async () => true)).toBeNull();
    expect(await snapshots.ask('tab-2', async () => false)).toBe(false);
    answer(true);
    expect(await first).toBe(true);
    expect(await snapshots.ask('tab-1', async () => false)).toBe(false);
  });
});
