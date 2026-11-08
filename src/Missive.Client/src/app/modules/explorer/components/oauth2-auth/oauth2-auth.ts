import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faArrowsRotate, faBan, faSpinner, faTrash } from '@fortawesome/free-solid-svg-icons';
import { OAuth2TokenDto } from '../../../../models/messages';
import { toAuthDto } from '../../../../models/request-mapping';
import { OAUTH2_GRANT_LABELS, OAUTH2_GRANTS, RequestAuth } from '../../../../models/workspace';
import { Bridge, BridgeError } from '../../../../services/bridge';
import { EnvironmentStore } from '../../../../services/environment-store';

/**
 * The OAuth 2.0 settings of a request and its cached token: **Get new token** obtains one (the
 * authorization code grant opens the system browser), **Clear** forgets it. Tokens are cached by
 * the host per environment, so the status follows the selected environment.
 */
@Component({
  selector: 'app-oauth2-auth',
  imports: [FaIconComponent],
  templateUrl: './oauth2-auth.html',
})
export class OAuth2Auth {
  readonly auth = input.required<RequestAuth>();
  /** Changes whenever the request was sent, since sending may obtain or refresh the token. */
  readonly sent = input<unknown>(null);

  readonly authChange = output<Partial<RequestAuth>>();

  private readonly bridge = inject(Bridge);
  private readonly environments = inject(EnvironmentStore);

  protected readonly grants = OAUTH2_GRANTS;
  protected readonly grantLabels = OAUTH2_GRANT_LABELS;
  protected readonly faArrowsRotate = faArrowsRotate;
  protected readonly faTrash = faTrash;
  protected readonly faSpinner = faSpinner;
  protected readonly faBan = faBan;
  protected readonly secretExample = '{{clientSecret}}';
  protected readonly fieldClass =
    'min-w-0 rounded border border-slate-300 px-2 py-1 font-mono focus:border-sky-500 focus:outline-none';

  protected readonly status = signal<OAuth2TokenDto | null>(null);
  protected readonly getting = signal(false);
  protected readonly error = signal<string | null>(null);

  /** The settings as the host reads them; status is only reloaded when they really change. */
  private readonly dto = computed(() => JSON.stringify(toAuthDto(this.auth())));
  private statusLoad = 0;

  constructor() {
    let timer: ReturnType<typeof setTimeout> | undefined;
    effect(() => {
      const auth = this.dto();
      this.environments.activeId();
      this.sent();
      clearTimeout(timer);
      // Typing in a field changes the settings on every key: wait for a pause.
      timer = setTimeout(() => void this.loadStatus(auth), 300);
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(timer));
  }

  protected patch(patch: Partial<RequestAuth>): void {
    this.authChange.emit(patch);
  }

  protected async getToken(): Promise<void> {
    this.getting.set(true);
    this.error.set(null);
    try {
      this.status.set(await this.bridge.send('oauth2.token.get', { auth: toAuthDto(this.auth()) }));
    } catch (error) {
      this.error.set(error instanceof BridgeError ? error.message : String(error));
    } finally {
      this.getting.set(false);
    }
  }

  protected async cancelSignIn(): Promise<void> {
    await this.bridge.send('oauth2.signIn.cancel');
  }

  protected async clearToken(): Promise<void> {
    this.error.set(null);
    try {
      await this.bridge.send('oauth2.token.clear', { auth: toAuthDto(this.auth()) });
      this.status.set(null);
    } catch (error) {
      this.error.set(error instanceof BridgeError ? error.message : String(error));
    }
  }

  protected formatTime(iso: string): string {
    return new Date(iso).toLocaleString();
  }

  private async loadStatus(auth: string): Promise<void> {
    const load = ++this.statusLoad;
    try {
      const status = await this.bridge.send('oauth2.token.status', { auth: JSON.parse(auth) });
      if (load === this.statusLoad) this.status.set(status);
    } catch {
      // The status is a convenience: sending reports what's wrong with the settings.
      if (load === this.statusLoad) this.status.set(null);
    }
  }
}
