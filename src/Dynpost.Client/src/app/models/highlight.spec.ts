import { highlightPositions, highlightRanges } from './highlight';

describe('highlight', () => {
  it('groups matched characters into runs', () => {
    expect(highlightPositions('Get user', [0, 1, 4])).toEqual([
      { text: 'Ge', match: true },
      { text: 't ', match: false },
      { text: 'u', match: true },
      { text: 'ser', match: false },
    ]);
  });

  it('splits text around ranges', () => {
    expect(highlightRanges('url: /users', [{ start: 6, length: 4 }])).toEqual([
      { text: 'url: /', match: false },
      { text: 'user', match: true },
      { text: 's', match: false },
    ]);
  });

  it('leaves text without matches whole', () => {
    expect(highlightRanges('plain', [])).toEqual([{ text: 'plain', match: false }]);
    expect(highlightPositions('', [])).toEqual([]);
  });
});
