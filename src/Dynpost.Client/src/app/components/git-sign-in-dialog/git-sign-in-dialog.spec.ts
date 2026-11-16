import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { TestBed } from '@angular/core/testing';
import { GitSignInRequest } from '../../services/git-sign-in-prompt';
import { GitSignInDialog } from './git-sign-in-dialog';

describe('GitSignInDialog', () => {
  function setup(request: GitSignInRequest) {
    const close = vi.fn();
    TestBed.configureTestingModule({
      imports: [GitSignInDialog],
      providers: [
        { provide: DIALOG_DATA, useValue: request },
        { provide: DialogRef, useValue: { close } },
      ],
    });
    const fixture = TestBed.createComponent(GitSignInDialog);
    fixture.detectChanges();
    const root: HTMLElement = fixture.nativeElement;
    const input = (name: string) => root.querySelector<HTMLInputElement>(`input[name="${name}"]`)!;
    const type = (name: string, value: string) => {
      input(name).value = value;
      input(name).dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };
    const button = (label: string) =>
      [...root.querySelectorAll('button')].find(
        (b) => b.textContent?.trim() === label || b.getAttribute('aria-label') === label,
      )!;
    return { root, close, fixture, input, type, button };
  }

  it('names the host and returns the credentials entered', () => {
    const { root, close, input, type, button } = setup({
      url: 'https://me@github.com',
      username: 'me',
      refused: false,
    });
    expect(root.textContent).toContain('https://github.com');
    expect(root.querySelector('[role="alert"]')).toBeNull();
    expect(input('username').value).toBe('me');
    expect(input('password').type).toBe('password');
    expect(button('Sign in').disabled).toBe(true);

    type('password', 'token');
    button('Sign in').click();

    expect(close).toHaveBeenCalledWith({
      url: 'https://me@github.com',
      username: 'me',
      password: 'token',
    });
  });

  it('says when the last credentials were refused, and can show the password', () => {
    const { root, fixture, input, type, button } = setup({
      url: 'https://github.com',
      username: '',
      refused: true,
    });
    expect(root.querySelector('[role="alert"]')!.textContent).toContain("couldn't sign in");

    type('password', 'token');
    button('Show password').click();
    fixture.detectChanges();

    expect(input('password').type).toBe('text');
    expect(button('Sign in').disabled).toBe(true);
  });
});
