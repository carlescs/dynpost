import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  styles: `
    :host {
      display: block;
      max-width: 48rem;
      margin: 4rem auto;
      padding: 0 2rem;
      font-family: system-ui, sans-serif;
    }
  `,
  template: `
    <main>
      <h1>{{ title() }}</h1>
      <p>Your Photino.NET + Angular desktop app is ready.</p>
      <button type="button" (click)="count.update(increment)">
        Clicked {{ count() }} times
      </button>
    </main>
    <router-outlet />
  `,
})
export class App {
  protected readonly title = signal('Missive');
  protected readonly count = signal(0);
  protected readonly increment = (value: number) => value + 1;
}
