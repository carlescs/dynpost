import { ENVIRONMENT_COLOR_NAMES, environmentColor } from './environment-colors';

describe('environmentColor', () => {
  it('styles every known color', () => {
    for (const name of ENVIRONMENT_COLOR_NAMES) expect(environmentColor(name)).not.toBeNull();
    expect(environmentColor('red')?.bg).toBe('bg-red-500');
  });

  it('returns null for no color or an unknown one', () => {
    expect(environmentColor(null)).toBeNull();
    expect(environmentColor(undefined)).toBeNull();
    expect(environmentColor('')).toBeNull();
    expect(environmentColor('magenta')).toBeNull();
    expect(environmentColor('toString')).toBeNull();
  });
});
