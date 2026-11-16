import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Component, computed, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faEye, faEyeSlash, faXmark } from '@fortawesome/free-solid-svg-icons';
import { GitSignIn } from '../../models/messages';
import { GitSignInRequest, hostOf } from '../../services/git-sign-in-prompt';

/**
 * Asks for the user name and password or access token of the HTTP(S) git host that refused a
 * command. Opened by `GitSignInPrompt`; closes with the credentials, or nothing when cancelled.
 */
@Component({
  selector: 'app-git-sign-in-dialog',
  imports: [FaIconComponent],
  templateUrl: './git-sign-in-dialog.html',
})
export class GitSignInDialog {
  protected readonly request = inject<GitSignInRequest>(DIALOG_DATA);
  protected readonly ref = inject(DialogRef<GitSignIn>);
  protected readonly host = hostOf(this.request.url);
  protected readonly username = signal(this.request.username);
  protected readonly password = signal('');
  protected readonly showPassword = signal(false);
  protected readonly canSignIn = computed(() => !!this.username().trim() && !!this.password());
  protected readonly faXmark = faXmark;
  protected readonly faEye = faEye;
  protected readonly faEyeSlash = faEyeSlash;

  protected signIn(): void {
    if (!this.canSignIn()) return;
    this.ref.close({
      url: this.request.url,
      username: this.username().trim(),
      password: this.password(),
    });
  }
}
