import { DOCUMENT } from '@angular/common';
import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { Bridge } from '../bridge/bridge';
import { Theme } from '../bridge/messages';

/**
 * The color theme. It is remembered by the host in the user's settings file. Dark mode is the
 * `dark` class on the root element, which re-maps the color palette in `tailwind.css`.
 */
@Injectable({ providedIn: 'root' })
export class ThemeStore {
  private readonly bridge = inject(Bridge);
  private readonly document = inject(DOCUMENT);

  readonly theme = signal<Theme>('light');
  readonly isDark = computed(() => this.theme() === 'dark');
  readonly monacoTheme = computed(() => (this.isDark() ? 'vs-dark' : 'vs'));

  constructor() {
    effect(() => {
      this.document.documentElement.classList.toggle('dark', this.isDark());
    });
  }

  async load(): Promise<void> {
    try {
      this.theme.set((await this.bridge.send('settings.get')).theme);
    } catch (error) {
      console.warn('Could not load the settings', error);
    }
  }

  async setDark(dark: boolean): Promise<void> {
    const previous = this.theme();
    this.theme.set(dark ? 'dark' : 'light');
    try {
      await this.bridge.send('settings.setTheme', { theme: this.theme() });
    } catch (error) {
      this.theme.set(previous);
      console.warn('Could not save the theme', error);
    }
  }
}
