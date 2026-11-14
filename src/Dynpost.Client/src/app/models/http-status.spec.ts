import { httpStatusInfo, httpStatusName, httpStatusTooltip } from './http-status';

describe('http-status', () => {
  it('names registered codes', () => {
    expect(httpStatusName(404)).toBe('Not Found');
    expect(httpStatusName('201')).toBe('Created');
    expect(httpStatusName(299)).toBe('');
  });

  it('describes every registered code', () => {
    for (const code of [100, 200, 204, 301, 304, 400, 401, 418, 429, 500, 503, 511]) {
      const info = httpStatusInfo(code);
      expect(info?.name).toBeTruthy();
      expect(info?.description).toBeTruthy();
    }
  });

  it('builds a tooltip with the name and explanation', () => {
    expect(httpStatusTooltip(404)).toBe(
      '404 Not Found — The server cannot find the requested resource.',
    );
  });

  it('falls back to the class for unregistered codes', () => {
    expect(httpStatusTooltip(599)).toBe(
      '599 Server Error (5xx) — The server failed to fulfil a valid request.',
    );
  });

  it('returns just the value for anything that is not a status code', () => {
    expect(httpStatusInfo('other')).toBeNull();
    expect(httpStatusTooltip('other')).toBe('other');
    expect(httpStatusTooltip(42)).toBe('42');
  });
});
