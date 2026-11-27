import { JsonValue, filterJson, jsonPathAt, jsonPathCompletions, queryJsonPath } from './json-path';

const store: JsonValue = {
  store: {
    book: [
      { title: 'A', price: 8, isbn: 'x' },
      { title: 'B', price: 12 },
      { title: 'C', price: 9, isbn: 'y' },
    ],
    'odd key': { color: 'red' },
  },
};

function query(path: string) {
  const result = queryJsonPath(store, path);
  if (!result.ok) throw new Error(result.error);
  return result.matches;
}

describe('json-path', () => {
  it('selects children, indexes, wildcards and slices', () => {
    expect(query('$.store.book[0].title')).toEqual(['A']);
    expect(query("$['store']['odd key'].color")).toEqual(['red']);
    expect(query('$.store.book[-1].title')).toEqual(['C']);
    expect(query('$.store.book[*].title')).toEqual(['A', 'B', 'C']);
    expect(query('$.store.book[0,2].title')).toEqual(['A', 'C']);
    expect(query('$.store.book[1:].title')).toEqual(['B', 'C']);
  });

  it('descends recursively and filters', () => {
    expect(query('$..price')).toEqual([8, 12, 9]);
    expect(query('$.store.book[?(@.price < 10)].title')).toEqual(['A', 'C']);
    expect(query('$.store.book[?(@.isbn)].title')).toEqual(['A', 'C']);
    expect(query("$.store.book[?(@.title == 'B')].price")).toEqual([12]);
  });

  it('reports invalid paths', () => {
    expect(queryJsonPath(store, 'store').ok).toBe(false);
    expect(queryJsonPath(store, '$.a[').ok).toBe(false);
  });

  it('filters a body to the value or the list of matches', () => {
    const body = JSON.stringify(store);
    expect(filterJson(body, '$.store.book[1].title')).toEqual({ text: '"B"', count: 1 });
    expect(filterJson(body, '$..title')).toEqual({
      text: JSON.stringify(['A', 'B', 'C'], null, 2),
      count: 3,
    });
    expect(filterJson('nope', '$')).toEqual({ error: 'The body is not valid JSON.' });
  });

  it('finds the path of the node at an offset', () => {
    const text = JSON.stringify(store, null, 2);
    expect(jsonPathAt(text, text.indexOf('"B"') + 1)).toBe('$.store.book[1].title');
    expect(jsonPathAt(text, text.indexOf('12'))).toBe('$.store.book[1].price');
    expect(jsonPathAt(text, text.indexOf('"odd key"') + 2)).toBe("$.store['odd key']");
    expect(jsonPathAt(text, text.indexOf('"color"') + 2)).toBe("$.store['odd key'].color");
  });
});

describe('jsonPathCompletions', () => {
  const body: JsonValue = {
    headers: { 'Content-Length': '19', 'Content-Type': 'text/plain', Host: 'example.com' },
    items: [
      { id: 1, name: 'a' },
      { id: 2, tags: [] },
    ],
    url: 'https://example.com',
  };
  const texts = (text: string) => jsonPathCompletions(body, text).map((c) => c.text);

  it('offers the top-level keys from an empty filter or $', () => {
    expect(texts('')).toEqual(['$.headers', '$.items', '$.url']);
    expect(texts('$')).toEqual(['$.headers', '$.items', '$.url']);
  });

  it('offers the keys after a dot that start with, then contain, what was typed, ignoring case', () => {
    expect(texts('$.headers.con')).toEqual([
      "$.headers['Content-Length']",
      "$.headers['Content-Type']",
    ]);
    expect(texts('$.headers.ost')).toEqual(['$.headers.Host']);
  });

  it('completes quoted bracket names with the quote typed', () => {
    expect(texts('$.headers["Content-L')).toEqual(['$.headers["Content-Length"]']);
    expect(texts("$.headers['ho")).toEqual(["$.headers['Host']"]);
  });

  it('offers [*] and the first indexes of arrays, and the keys of all items', () => {
    expect(texts('$.items[')).toEqual(['$.items[*]', '$.items[0]', '$.items[1]']);
    expect(texts('$.items[*].')).toEqual(['$.items[*].id', '$.items[*].name', '$.items[*].tags']);
  });

  it('offers the keys found at any depth after ..', () => {
    expect(texts('$..na')).toEqual(['$..name']);
  });

  it('offers nothing for an invalid path, inside a filter or once a name is complete', () => {
    expect(texts('$.nope.')).toEqual([]);
    expect(texts('$.items[?(@.')).toEqual([]);
    expect(texts('x.')).toEqual([]);
    expect(texts('$.url')).toEqual([]);
  });
});
