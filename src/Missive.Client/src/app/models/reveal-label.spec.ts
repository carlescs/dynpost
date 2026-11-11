import { revealLabel } from './reveal-label';

describe('revealLabel', () => {
  it('names the file manager of the platform', () => {
    expect(revealLabel('Mozilla/5.0 (Windows NT 10.0; Win64; x64) Edg/130.0')).toBe(
      'Show in File Explorer',
    );
    expect(revealLabel('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)')).toBe('Show in Finder');
    expect(revealLabel('Mozilla/5.0 (X11; Linux x86_64)')).toBe('Show in file manager');
  });
});
