import { DOCUMENT } from '@angular/common';
import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { Bridge } from './bridge';
import { Theme } from '../models/messages';

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
  private selection = 0;
  private saveQueue: Promise<void> = Promise.resolve();

  constructor() {
    effect(() => {
      this.document.documentElement.classList.toggle('dark', this.isDark());
    });
  }

  async load(): Promise<void> {
    const selection = this.selection;
    try {
      const settings = await this.bridge.send('settings.get');
      if (selection === this.selection) this.theme.set(settings.theme);
    } catch (error) {
      console.warn('Could not load the settings', error);
    }
  }

  async setDark(dark: boolean): Promise<void> {
    const theme = dark ? 'dark' : 'light';
    const previous = this.theme();
    const selection = ++this.selection;
    this.theme.set(theme);
    const save = this.saveQueue.then(() => this.bridge.send('settings.setTheme', { theme }));
    this.saveQueue = save.then(
      () => undefined,
      () => undefined,
    );
    try {
      await save;
    } catch (error) {
      if (selection === this.selection) this.theme.set(previous);
      console.warn('Could not save the theme', error);
    }
  }
}
