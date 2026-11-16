/**
 * The folder name `git clone` picks for a repository URL: its last path segment without a
 * trailing `.git` (`https://host/team/api.git` and `git@host:team/api` give `api`), or `''` when
 * the URL has none. Mirrors `RemoteUrl.RepositoryName` on the host.
 */
export function repositoryName(url: string): string {
  let path = url.trim().replaceAll('\\', '/').replace(/\/+$/, '');
  if (path.toLowerCase().endsWith('.git')) path = path.slice(0, -4).replace(/\/+$/, '');
  const name = path.slice(Math.max(path.lastIndexOf('/'), path.lastIndexOf(':')) + 1);
  return name === '.' || name === '..' ? '' : name;
}
