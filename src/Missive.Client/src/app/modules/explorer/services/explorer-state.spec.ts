import { WorkspaceNode } from '../../../models/workspace';
import { canMove } from './explorer-state';

const folder = (path: string): WorkspaceNode => ({ path, name: path, kind: 'folder' });
const request = (path: string): WorkspaceNode => ({ path, name: path, kind: 'request' });

describe('canMove', () => {
  it('rejects moves into the current parent', () => {
    expect(canMove(request('users/get.yaml'), 'users')).toBe(false);
    expect(canMove(request('get.yaml'), '')).toBe(false);
    expect(canMove(folder('a/b'), 'a')).toBe(false);
  });

  it('rejects moving a folder into itself or its descendants', () => {
    expect(canMove(folder('a'), 'a')).toBe(false);
    expect(canMove(folder('a'), 'a/b/c')).toBe(false);
  });

  it('allows other moves, including into a sibling with a shared prefix', () => {
    expect(canMove(request('users/get.yaml'), '')).toBe(true);
    expect(canMove(request('get.yaml'), 'users')).toBe(true);
    expect(canMove(folder('a'), 'ab')).toBe(true);
    expect(canMove(folder('a/b'), '')).toBe(true);
  });
});
