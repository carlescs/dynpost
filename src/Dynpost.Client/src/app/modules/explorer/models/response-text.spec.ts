import { ResponseDto } from '../../../models/messages';
import { comparableBody, comparableHeaders, looksLikeJson, prettyJson } from './response-text';

const response = (overrides: Partial<ResponseDto> = {}): ResponseDto => ({
  status: 200,
  statusText: 'OK',
  headers: [{ key: 'Content-Type', value: 'application/json' }],
  contentType: 'application/json',
  body: '{"a":1}',
  bodyEncoding: 'text',
  size: 7,
  truncated: false,
  durationMs: 12,
  ...overrides,
});

describe('response text', () => {
  it('recognises JSON sent without a JSON content type', () => {
    expect(looksLikeJson(response({ contentType: 'text/plain', body: ' [1]' }))).toBe(true);
    expect(looksLikeJson(response({ contentType: 'text/html', body: '{' }))).toBe(false);
  });

  it('pretty prints JSON bodies and leaves others as received', () => {
    expect(prettyJson(response())).toBe('{\n  "a": 1\n}');
    expect(prettyJson(response({ body: '{oops' }))).toBeNull();
    expect(comparableBody(response({ contentType: 'text/plain', body: 'hi' }))).toBe('hi');
  });

  it('describes binary and omitted bodies instead of comparing them', () => {
    expect(comparableBody(response({ bodyEncoding: 'base64', contentType: 'image/png' }))).toBe(
      '(Binary body: 7 B, image/png.)',
    );
    expect(comparableBody(response({ body: '' }), true)).toContain('too large to keep');
  });

  it('lists the status line before the headers', () => {
    expect(comparableHeaders(response())).toBe('200 OK\nContent-Type: application/json\n');
  });

  it('hides cookie values, as kept responses do', () => {
    const headers = [
      { key: 'set-cookie', value: 'id=abc; Path=/' },
      { key: 'Set-Cookie', value: 'flag' },
    ];
    expect(comparableHeaders(response({ headers }))).toBe(
      '200 OK\nset-cookie: id=••••••; Path=/\nSet-Cookie: ••••••\n',
    );
  });
});
