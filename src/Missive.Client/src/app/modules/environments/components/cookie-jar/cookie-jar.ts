import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faCookieBite, faRotate, faTrash } from '@fortawesome/free-solid-svg-icons';
import { CookieDto } from '../../../../models/messages';
import { EnvironmentStore } from '../../../../services/environment-store';

/** Lists the cookies kept for an environment and lets the user delete them. */
@Component({
  selector: 'app-cookie-jar',
  imports: [FaIconComponent],
  templateUrl: './cookie-jar.html',
})
export class CookieJar {
  private readonly store = inject(EnvironmentStore);

  /** The environment's id (its file name). */
  readonly environmentId = input.required<string>();

  protected readonly cookies = signal<CookieDto[]>([]);
  protected readonly loading = signal(false);

  protected readonly faCookieBite = faCookieBite;
  protected readonly faRotate = faRotate;
  protected readonly faTrash = faTrash;

  private loadCount = 0;

  constructor() {
    effect(() => {
      const id = this.environmentId();
      untracked(() => void this.load(id));
    });
  }

  /** Re-reads the jar, e.g. after sending requests. */
  async refresh(): Promise<void> {
    await this.load(this.environmentId());
  }

  protected async remove(cookie: CookieDto): Promise<void> {
    const id = this.environmentId();
    const left = await this.store.deleteCookie(id, cookie);
    if (left && id === this.environmentId()) this.cookies.set(left);
  }

  protected async clear(): Promise<void> {
    const id = this.environmentId();
    if (!confirm('Delete every cookie kept for this environment?')) return;
    if ((await this.store.clearCookies(id)) && id === this.environmentId()) this.cookies.set([]);
  }

  protected formatExpires(iso: string | null | undefined): string {
    if (!iso) return 'Session';
    const date = new Date(iso);
    return Number.isNaN(date.getTime()) ? iso : date.toLocaleString();
  }

  private async load(id: string): Promise<void> {
    const load = ++this.loadCount;
    this.loading.set(true);
    const cookies = await this.store.cookies(id);
    if (load !== this.loadCount) return;
    this.loading.set(false);
    if (cookies) this.cookies.set(cookies);
  }
}
