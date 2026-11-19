import { canPrettyPrint, prettyPrint } from './pretty-print';

describe('prettyPrint (JSON)', () => {
  it('re-indents objects and arrays', () => {
    expect(prettyPrint('{"a":1,"b":[true,null,{}],"c":[]}', 'json')).toBe(
      '{\n  "a": 1,\n  "b": [\n    true,\n    null,\n    {}\n  ],\n  "c": []\n}',
    );
  });

  it('keeps literals, escapes and duplicate keys as written', () => {
    expect(prettyPrint('{"id":12345678901234567890,"x":1.0,"x":"a\\u00e9\\n"}', 'json')).toBe(
      '{\n  "id": 12345678901234567890,\n  "x": 1.0,\n  "x": "a\\u00e9\\n"\n}',
    );
  });

  it('formats scalars and already pretty text', () => {
    expect(prettyPrint('  "hi" \n', 'json')).toBe('"hi"');
    const pretty = '{\n  "a": [\n    1\n  ]\n}';
    expect(prettyPrint(pretty, 'json')).toBe(pretty);
  });

  it('rejects text that is not JSON', () => {
    for (const text of ['', '{oops}', '{"a":1,}', '[1 2]', '{"a" 1}', '12ab', '[1]]', "{'a':1}"]) {
      expect(prettyPrint(text, 'json')).toBeNull();
    }
  });

  it('accepts {{variables}} only when asked to', () => {
    const text = '{"id":{{userId}},{{key}}:"{{name}}"}';
    expect(prettyPrint(text, 'json')).toBeNull();
    expect(prettyPrint(text, 'json', { variables: true })).toBe(
      '{\n  "id": {{userId}},\n  {{key}}: "{{name}}"\n}',
    );
  });

  it('uses the indentation given', () => {
    expect(prettyPrint('[1]', 'json', { indent: '\t' })).toBe('[\n\t1\n]');
  });
});

describe('prettyPrint (XML)', () => {
  it('puts each element on its own line and keeps text-only elements on one', () => {
    expect(
      prettyPrint(
        '<?xml version="1.0"?><user id="1"><name> Ada </name><tags><tag/><tag a=\'x>y\'/></tags><empty></empty></user>',
        'xml',
      ),
    ).toBe(
      [
        '<?xml version="1.0"?>',
        '<user id="1">',
        '  <name>Ada</name>',
        '  <tags>',
        '    <tag/>',
        "    <tag a='x>y'/>",
        '  </tags>',
        '  <empty></empty>',
        '</user>',
      ].join('\n'),
    );
  });

  it('copies comments and CDATA as they are and drops whitespace between tags', () => {
    expect(prettyPrint('<a>\n   <!-- note -->\n  <b><![CDATA[<raw>]]></b>\n</a>\n', 'xml')).toBe(
      '<a>\n  <!-- note -->\n  <b>\n    <![CDATA[<raw>]]>\n  </b>\n</a>',
    );
  });

  it('copies a doctype with an internal subset', () => {
    expect(prettyPrint('<!DOCTYPE a [<!ENTITY x "y">]><a>&x;</a>', 'xml')).toBe(
      '<!DOCTYPE a [<!ENTITY x "y">]>\n<a>&x;</a>',
    );
  });

  it('rejects a malformed doctype quickly', () => {
    const start = performance.now();
    expect(prettyPrint('<!doctype[' + ']['.repeat(5000), 'xml')).toBeNull();
    expect(performance.now() - start).toBeLessThan(1000);
  });

  it('keeps namespaces and entities', () => {
    expect(
      prettyPrint('<s:Envelope xmlns:s="urn:x"><s:Body>a &amp; b</s:Body></s:Envelope>', 'xml'),
    ).toBe('<s:Envelope xmlns:s="urn:x">\n  <s:Body>a &amp; b</s:Body>\n</s:Envelope>');
  });

  it('rejects text that is not well-formed', () => {
    for (const text of [
      '',
      'plain text',
      '<a><b></a></b>',
      '<a>',
      '<a/><b/>',
      '<a></a>tail',
      '<a <b>',
    ]) {
      expect(prettyPrint(text, 'xml')).toBeNull();
    }
  });

  it('accepts {{variables}} in text and attributes', () => {
    expect(prettyPrint('<a id="{{id}}">{{name}}</a>', 'xml', { variables: true })).toBe(
      '<a id="{{id}}">{{name}}</a>',
    );
  });
});

describe('canPrettyPrint', () => {
  it('knows JSON and XML', () => {
    expect(canPrettyPrint('json')).toBe(true);
    expect(canPrettyPrint('xml')).toBe(true);
    expect(canPrettyPrint('yaml')).toBe(false);
    expect(prettyPrint('a: 1', 'yaml')).toBeNull();
  });
});
