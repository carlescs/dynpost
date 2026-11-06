import { fileName, folderOf, languageForPath, relativeTime } from './git-format';

describe('git format', () => {
  it('splits paths into folder and file name', () => {
    expect(fileName('users/create-user.yaml')).toBe('create-user.yaml');
    expect(fileName('readme.md')).toBe('readme.md');
    expect(folderOf('a/b/c.yaml')).toBe('a/b');
    expect(folderOf('c.yaml')).toBe('');
  });

  it('picks a Monaco language from the extension', () => {
    expect(languageForPath('users/get.YAML')).toBe('yaml');
    expect(languageForPath('.missive/x.json')).toBe('json');
    expect(languageForPath('docs/README.md')).toBe('markdown');
    expect(languageForPath('src/index.ts')).toBe('javascript');
    expect(languageForPath('Makefile')).toBe('plaintext');
    expect(languageForPath('folder.d/file')).toBe('plaintext');
  });

  it('describes how long ago a date was in the largest whole unit', () => {
    const now = Date.parse('2026-01-10T12:00:00Z');
    const format = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
    expect(relativeTime('2026-01-10T11:59:30Z', now)).toBe(format.format(0, 'minute'));
    expect(relativeTime('2026-01-10T09:00:00Z', now)).toBe(format.format(-3, 'hour'));
    expect(relativeTime('2026-01-07T12:00:00Z', now)).toBe(format.format(-3, 'day'));
    expect(relativeTime('2025-01-01T00:00:00Z', now)).toBe(format.format(-1, 'year'));
  });
});
