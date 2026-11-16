import { repositoryName } from './repository-name';

describe('repositoryName', () => {
  it.each([
    ['https://github.com/team/my-api.git', 'my-api'],
    ['https://github.com/team/my-api/', 'my-api'],
    ['git@github.com:team/my-api.git', 'my-api'],
    ['git@github.com:my-api', 'my-api'],
    ['ssh://git@host:2222/team/my-api.git/', 'my-api'],
    ['C:\\repos\\my-api', 'my-api'],
    ['  https://example.com/a/b  ', 'b'],
    ['https://', ''],
    ['', ''],
    ['..', ''],
  ])('takes the folder name from %s', (url, name) => {
    expect(repositoryName(url)).toBe(name);
  });
});
