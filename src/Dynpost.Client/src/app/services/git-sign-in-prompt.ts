import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { GitSignIn } from '../models/messages';
import { BridgeError } from './bridge';
import { Dialogs } from './dialogs';

/** The host's error when git couldn't sign in to a remote. */
export const GIT_AUTHENTICATION_FAILED = 'git.authentication_failed';

/** What the sign-in dialog shows. */
export interface GitSignInRequest {
  /** The URL git couldn't sign in to, maybe with `user@`. */
  url: string;
  /** The user name to start with: the last one tried, or the one in the URL. */
  username: string;
  /** Whether the credentials entered last were refused. */
  refused: boolean;
}

/**
 * Lets the user sign in when git can't sign in to an HTTP(S) remote by itself (no credential
 * helper, or nothing saved for the host): asks for a user name and password or token and tries
 * the command again with them. The host saves them through the user's git credential helper once
 * they work, so they are asked for once. SSH remotes are left to the user's SSH setup.
 */
@Injectable({ providedIn: 'root' })
export class GitSignInPrompt {
  private readonly dialogs = inject(Dialogs);

  /**
   * Runs `call`, first without credentials. While it fails because git couldn't sign in to an
   * HTTP(S) URL, asks for credentials and runs it again with them; when the user cancels, the
   * sign-in error is thrown as it came.
   */
  async run<T>(call: (signIn: GitSignIn | null) => Promise<T>): Promise<T> {
    let signIn: GitSignIn | null = null;
    for (;;) {
      try {
        return await call(signIn);
      } catch (error) {
        if (
          !(error instanceof BridgeError) ||
          error.code !== GIT_AUTHENTICATION_FAILED ||
          !error.target
        ) {
          throw error;
        }
        const entered = await this.ask({
          url: error.target,
          username: signIn?.username ?? userOf(error.target),
          refused: signIn !== null,
        });
        if (!entered) throw error;
        signIn = entered;
      }
    }
  }

  /** Shows the sign-in dialog (loaded on first use); `null` when the user cancels. */
  private async ask(request: GitSignInRequest): Promise<GitSignIn | null> {
    const { GitSignInDialog } = await import('../components/git-sign-in-dialog/git-sign-in-dialog');
    const ref = this.dialogs.open<GitSignIn, GitSignInRequest>(GitSignInDialog, {
      data: request,
      ariaLabelledBy: 'git-sign-in-title',
    });
    return (await firstValueFrom(ref.closed)) ?? null;
  }
}

/** The user name in `https://user@host`, or `''`. */
export function userOf(url: string): string {
  try {
    return decodeURIComponent(new URL(url).username);
  } catch {
    return '';
  }
}

/** The URL without its user name, as the user knows the host: `https://host:port`. */
export function hostOf(url: string): string {
  try {
    const parsed = new URL(url);
    return `${parsed.protocol}//${parsed.host}`;
  } catch {
    return url;
  }
}
