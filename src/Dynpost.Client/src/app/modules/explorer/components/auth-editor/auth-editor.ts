import { Component, inject, input, output } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faFolderOpen } from '@fortawesome/free-solid-svg-icons';
import { InheritedSettingsDto } from '../../../../models/messages';
import { AUTH_LABELS, AUTH_TYPES, AuthType, RequestAuth } from '../../../../models/workspace';
import { FolderSettingsStore } from '../../../../services/folder-settings-store';
import { OAuth2Auth } from '../oauth2-auth/oauth2-auth';

/**
 * The auth settings of a request or a folder: the type and its fields. `inherit` uses the auth of
 * the closest folder above that sets one, which is shown with a link to that folder's settings.
 */
@Component({
  selector: 'app-auth-editor',
  imports: [FaIconComponent, OAuth2Auth],
  templateUrl: './auth-editor.html',
})
export class AuthEditor {
  readonly auth = input.required<RequestAuth>();
  /** What a request or folder with this auth inherits; `null` while unknown. */
  readonly inherited = input<InheritedSettingsDto | null>(null);
  /** What the settings belong to, for the texts. */
  readonly subject = input<'request' | 'folder'>('request');
  /** Changes whenever the request was sent, since sending may obtain or refresh an OAuth 2.0 token. */
  readonly sent = input<unknown>(null);

  readonly authChange = output<Partial<RequestAuth>>();

  protected readonly folders = inject(FolderSettingsStore);
  protected readonly authTypes = AUTH_TYPES;
  protected readonly authLabels = AUTH_LABELS;
  protected readonly faFolderOpen = faFolderOpen;
  protected readonly secretExample = '{{apiToken}}';
  protected readonly fieldClass =
    'min-w-0 rounded border border-slate-300 px-2 py-1 font-mono focus:border-sky-500 focus:outline-none';

  /** The label of the inherited auth type. */
  protected inheritedLabel(inherited: InheritedSettingsDto): string {
    return AUTH_LABELS[inherited.auth.type as AuthType] ?? inherited.auth.type;
  }

  protected patch(patch: Partial<RequestAuth>): void {
    this.authChange.emit(patch);
  }
}
