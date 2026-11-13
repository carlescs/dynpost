import { decodeBase64 } from './base64';

describe('decodeBase64', () => {
  it('decodes base64 into bytes', () => {
    expect([...decodeBase64('JVBERv8=')]).toEqual([0x25, 0x50, 0x44, 0x46, 0xff]);
    expect(decodeBase64('').length).toBe(0);
  });
});
