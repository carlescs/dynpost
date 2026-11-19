import { requestBodyLanguage } from './body-language';

describe('requestBodyLanguage', () => {
  const xml = { key: 'content-type', value: 'application/xml; charset=utf-8', enabled: true };

  it('is JSON for a JSON body and plain text for other kinds', () => {
    expect(requestBodyLanguage('json', '', [xml])).toBe('json');
    expect(requestBodyLanguage('graphql', '<a/>', [xml])).toBe('plaintext');
  });

  it('takes a raw body language from its Content-Type, its own before its folders', () => {
    expect(requestBodyLanguage('text', '', [xml])).toBe('xml');
    expect(requestBodyLanguage('text', '', [], [{ key: 'Content-Type', value: 'text/yaml' }])).toBe(
      'yaml',
    );
    expect(
      requestBodyLanguage(
        'text',
        '',
        [{ ...xml, enabled: false }],
        [{ key: 'Content-Type', value: 'application/json' }],
      ),
    ).toBe('json');
  });

  it('goes by what a raw body looks like before its Content-Type', () => {
    const json = { key: 'Content-Type', value: 'application/json' };
    expect(requestBodyLanguage('text', '  <a/>', [json])).toBe('xml');
    expect(requestBodyLanguage('text', '[1]', [xml])).toBe('json');
    expect(requestBodyLanguage('text', '{"a":1}', [])).toBe('json');
    expect(requestBodyLanguage('text', '{{payload}}', [xml])).toBe('xml');
    expect(
      requestBodyLanguage('text', 'hello', [{ key: 'Content-Type', value: 'text/plain' }]),
    ).toBe('plaintext');
  });
});
