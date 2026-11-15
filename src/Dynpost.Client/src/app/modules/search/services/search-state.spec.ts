import { TestBed } from '@angular/core/testing';
import { Mock } from 'vitest';
import { WorkspaceSearchDto } from '../../../models/messages';
import { Bridge } from '../../../services/bridge';
import { WorkspaceStore } from '../../../services/workspace-store';
import { SEARCH_DELAY_MS, SearchState } from './search-state';

const results = (matchCount: number, truncated = false): WorkspaceSearchDto => ({
  files: [
    {
      path: 'requests/get-user.yaml',
      name: 'Get user',
      method: 'GET',
      lines: [{ line: 3, text: 'url: /users/1', ranges: [{ start: 6, length: 5 }] }],
    },
  ],
  matchCount,
  truncated,
});

describe('SearchState', () => {
  let send: Mock;
  let state: SearchState;
  let workspace: WorkspaceStore;

  beforeEach(() => {
    vi.useFakeTimers();
    send = vi.fn().mockResolvedValue(results(1));
    TestBed.configureTestingModule({
      providers: [{ provide: Bridge, useValue: { isDesktop: true, send } }],
    });
    workspace = TestBed.inject(WorkspaceStore);
    workspace.workspace.set({ name: 'api', root: '/r' });
    state = TestBed.inject(SearchState);
    TestBed.tick();
  });
  afterEach(() => vi.useRealTimers());

  it('searches once typing pauses', async () => {
    state.setQuery('us');
    state.setQuery('users');
    expect(send).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(SEARCH_DELAY_MS);

    expect(send).toHaveBeenCalledTimes(1);
    expect(send).toHaveBeenCalledWith('workspace.search', { query: 'users', matchCase: false });
    expect(state.results()?.files).toHaveLength(1);
    expect(state.summary()).toBe('1 result in 1 file.');
  });

  it('searches again right away when match case changes', async () => {
    state.query.set('Users');
    state.toggleMatchCase();
    await vi.advanceTimersByTimeAsync(0);

    expect(send).toHaveBeenCalledWith('workspace.search', { query: 'Users', matchCase: true });
  });

  it('clears the results for an empty query without asking the host', async () => {
    state.query.set('users');
    await state.search();
    state.query.set('');
    await state.search();

    expect(send).toHaveBeenCalledTimes(1);
    expect(state.results()).toBeNull();
  });

  it('says when the results were cut short', async () => {
    send.mockResolvedValue(results(2000, true));
    state.query.set('a');
    await state.search();

    expect(state.summary()).toBe(
      '2000 results in 1 file. Only the first ones are shown: refine the search.',
    );
  });

  it('shows why a search failed', async () => {
    send.mockRejectedValue(new Error('Open a workspace folder first.'));
    state.query.set('a');
    await state.search();

    expect(state.error()).toBe('Open a workspace folder first.');
    expect(state.results()).toBeNull();
  });

  it('folds and unfolds a file', () => {
    state.toggleFile('requests/a.yaml');
    expect(state.collapsed().has('requests/a.yaml')).toBe(true);
    state.toggleFile('requests/a.yaml');
    expect(state.collapsed().has('requests/a.yaml')).toBe(false);
  });

  it('forgets the results when another workspace is opened', async () => {
    state.query.set('users');
    await state.search();

    workspace.workspace.set({ name: 'other', root: '/other' });
    TestBed.tick();

    expect(state.results()).toBeNull();
  });
});
