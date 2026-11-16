import { JsonValue, filterJson, jsonPathAt, queryJsonPath } from './json-path';

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
