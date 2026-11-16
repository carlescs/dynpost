import { TestBed } from '@angular/core/testing';
import { defer, of } from 'rxjs';
import { BridgeError } from './bridge';
import { Dialogs } from './dialogs';
import { GitSignInPrompt, hostOf, userOf } from './git-sign-in-prompt';
import { GitSignInDialog } from '../components/git-sign-in-dialog/git-sign-in-dialog';

describe('GitSignInPrompt', () => {
  const refused = (target: string | null = 'https://me@github.com') =>
    new BridgeError('git.authentication_failed', 'Git could not sign in.', target);
  let answers: unknown[];
  let open: ReturnType<typeof vi.fn>;
  let prompt: GitSignInPrompt;

  beforeEach(() => {
    answers = [];
    open = vi.fn(() => ({ closed: defer(() => of(answers.shift())) }));
    TestBed.configureTestingModule({ providers: [{ provide: Dialogs, useValue: { open } }] });
    prompt = TestBed.inject(GitSignInPrompt);
  });

  it('runs once without credentials when git signs in by itself', async () => {
    const call = vi.fn().mockResolvedValue('done');

    expect(await prompt.run(call)).toBe('done');
    expect(call).toHaveBeenCalledExactlyOnceWith(null);
    expect(open).not.toHaveBeenCalled();
  });

  it('asks for credentials and tries again with them until git accepts them', async () => {
    const first = { url: 'https://me@github.com', username: 'me', password: 'wrong' };
    const second = { ...first, password: 'token' };
    answers = [first, second];
    const call = vi
      .fn()
      .mockRejectedValueOnce(refused())
      .mockRejectedValueOnce(refused())
      .mockResolvedValue('done');

    expect(await prompt.run(call)).toBe('done');

    expect(call.mock.calls).toEqual([[null], [first], [second]]);
    expect(open).toHaveBeenNthCalledWith(1, GitSignInDialog, {
      data: { url: 'https://me@github.com', username: 'me', refused: false },
      ariaLabelledBy: 'git-sign-in-title',
    });
    expect(open.mock.calls[1][1].data).toEqual({
      url: 'https://me@github.com',
      username: 'me',
      refused: true,
    });
  });

  it('throws the sign-in error when the user cancels', async () => {
    answers = [undefined];
    const error = refused();

    await expect(prompt.run(() => Promise.reject(error))).rejects.toBe(error);
  });

  it('leaves other failures, and sign-in failures without an HTTP(S) URL, to the caller', async () => {
    const ssh = refused(null);
    const other = new BridgeError('git.remote_unreachable', 'Offline');

    await expect(prompt.run(() => Promise.reject(ssh))).rejects.toBe(ssh);
    await expect(prompt.run(() => Promise.reject(other))).rejects.toBe(other);
    expect(open).not.toHaveBeenCalled();
  });

  it('reads the user name and host from the URL', () => {
    expect(userOf('https://me%40corp@git.example.com:8443')).toBe('me@corp');
    expect(userOf('https://github.com')).toBe('');
    expect(hostOf('https://me@git.example.com:8443')).toBe('https://git.example.com:8443');
  });
});
