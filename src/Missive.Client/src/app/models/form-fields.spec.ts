import {
  bodyText,
  bulkProblem,
  emptyField,
  fieldsOf,
  formatBulk,
  isAbsolutePath,
  parseBulk,
} from './form-fields';

describe('form fields', () => {
  it('parses bulk text: files, options, disabled fields and lines without a value', () => {
    expect(
      parseBulk(
        'title = Report\n\n~debug=true\nfile=@docs/a.pdf;type=application/pdf;filename=q3.pdf\nflag\n',
        'form',
      ),
    ).toEqual([
      emptyField({ name: 'title', value: 'Report' }),
      emptyField({ name: 'debug', value: 'true', enabled: false }),
      emptyField({
        name: 'file',
        value: 'docs/a.pdf',
        kind: 'file',
        contentType: 'application/pdf',
        fileName: 'q3.pdf',
      }),
      emptyField({ name: 'flag' }),
    ]);
  });

  it('splits urlencoded lines on & and never reads files', () => {
    expect(parseBulk('a=1&b=%40x\n~c=@d', 'urlencoded')).toEqual([
      emptyField({ name: 'a', value: '1' }),
      emptyField({ name: 'b', value: '%40x' }),
      emptyField({ name: 'c', value: '@d', enabled: false }),
    ]);
  });

  it('round-trips fields through bulk text', () => {
    const fields = [
      emptyField({ name: 'meta', value: '{"a": 1}' }),
      emptyField({ name: 'file', value: 'a.png', kind: 'file', contentType: 'image/png' }),
      emptyField({ name: 'file', value: 'b.png', kind: 'file', fileName: 'c.png', enabled: false }),
    ];
    const text = formatBulk(fields, 'form');
    expect(text).toBe('meta={"a": 1}\nfile=@a.png;type=image/png\n~file=@b.png;filename=c.png');
    expect(parseBulk(text, 'form')).toEqual(fields);
  });

  it('says why fields cannot be bulk edited', () => {
    expect(bulkProblem([emptyField({ name: 'a', value: 'b' })], 'form')).toBeNull();
    expect(bulkProblem([emptyField({ name: 'a', value: 'x\ny' })], 'form')).toContain(
      'line breaks',
    );
    expect(bulkProblem([emptyField({ name: 'a', value: '@x' })], 'form')).toContain('@');
    expect(bulkProblem([emptyField({ name: 'a', contentType: 'text/csv' })], 'form')).toContain(
      'content type',
    );
    expect(bulkProblem([emptyField({ name: 'a', encode: true })], 'urlencoded')).toContain(
      'encoded',
    );
  });

  it('reads the fields of a body, or of the text of an older file', () => {
    const field = emptyField({ name: 'a', value: '1' });
    expect(fieldsOf({ bodyType: 'form', body: 'x=y', bodyFields: [field] })).toEqual([field]);
    expect(fieldsOf({ bodyType: 'form', body: 'a=1', bodyFields: null })).toEqual([field]);
    expect(fieldsOf({ bodyType: 'json', body: 'a=1', bodyFields: [field] })).toEqual([]);
  });

  it('describes field bodies as text for diffs', () => {
    expect(
      bodyText({
        bodyType: 'form',
        body: '',
        bodyFields: [
          emptyField({ name: 'a', value: 'x\ny', contentType: 'text/csv' }),
          emptyField({ name: 'f', value: 'a.png', kind: 'file', fileName: 'b.png' }),
        ],
      }),
    ).toBe('a=x\\ny ; type=text/csv\nf=@a.png ; filename=b.png');
    expect(bodyText({ bodyType: 'json', body: '{}', bodyFields: null })).toBe('{}');
  });

  it('recognises absolute paths', () => {
    expect(isAbsolutePath('/tmp/a')).toBe(true);
    expect(isAbsolutePath('C:\\a')).toBe(true);
    expect(isAbsolutePath('\\\\server\\share')).toBe(true);
    expect(isAbsolutePath('fixtures/a.png')).toBe(false);
  });
});
