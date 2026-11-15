import { TestBed } from '@angular/core/testing';
import { Mock } from 'vitest';
import { Bridge } from '../../../../services/bridge';
import { WorkspaceStore } from '../../../../services/workspace-store';
import { SearchState } from '../../services/search-state';
import { SearchView } from './search-view';

describe('SearchView', () => {
  let send: Mock;

  async function render() {
    send = vi.fn().mockResolvedValue({
      files: [
        {
          path: 'requests/users/get-user.yaml',
          name: 'Get user',
          method: 'GET',
          lines: [
            { line: 3, text: 'url: /users/1', ranges: [{ start: 6, length: 5 }] },
            { line: 5, text: 'X-Users: all', ranges: [{ start: 2, length: 5 }] },
          ],
        },
      ],
      matchCount: 2,
      truncated: false,
    });
    TestBed.configureTestingModule({
      providers: [{ provide: Bridge, useValue: { isDesktop: true, send } }],
    });
    const workspace = TestBed.inject(WorkspaceStore);
    workspace.workspace.set({ name: 'api', root: '/r' });
    const openRequest = vi.spyOn(workspace, 'openRequest').mockResolvedValue();
    const state = TestBed.inject(SearchState);
    state.query.set('users');
    await state.search();
    const fixture = TestBed.createComponent(SearchView);
    await fixture.whenStable();
    return { fixture, host: fixture.nativeElement as HTMLElement, openRequest, state };
  }

  it('lists the matching lines under their file, highlighting the matches', async () => {
    const { host } = await render();

    expect(host.textContent).toContain('Get user');
    expect(host.textContent).toContain('requests/users');
    expect(host.textContent).toContain('2 results in 1 file.');
    expect([...host.querySelectorAll('mark')].map((m) => m.textContent)).toEqual([
      'users',
      'Users',
    ]);
  });

  it('opens the request when a line is clicked', async () => {
    const { host, openRequest } = await render();

    host
      .querySelector<HTMLButtonElement>('[title="Line 5 of requests/users/get-user.yaml"]')!
      .click();

    expect(openRequest).toHaveBeenCalledWith({
      path: 'requests/users/get-user.yaml',
      name: 'Get user',
      kind: 'request',
    });
  });

  it('folds a file’s matches away', async () => {
    const { fixture, host } = await render();

    host.querySelector<HTMLButtonElement>('[aria-label="Hide the matches in Get user"]')!.click();
    await fixture.whenStable();

    expect(host.querySelectorAll('mark')).toHaveLength(0);
  });

  it('asks for a workspace when none is open', async () => {
    TestBed.configureTestingModule({
      providers: [{ provide: Bridge, useValue: { isDesktop: true, send: vi.fn() } }],
    });
    const fixture = TestBed.createComponent(SearchView);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Open a workspace folder to search its requests.',
    );
  });
});
