import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faPaperPlane } from '@fortawesome/free-solid-svg-icons';

@Component({
  imports: [RouterOutlet, FaIconComponent],
  selector: 'app-root',
  host: { class: 'mx-auto my-16 block max-w-3xl px-8 font-sans' },
  template: `
    <main class="space-y-4">
      <h1 class="flex items-center gap-3 text-3xl font-semibold">
        <fa-icon [icon]="faPaperPlane" class="text-sky-600" />
        {{ title() }}
      </h1>
      <p class="text-slate-600">Your Photino.NET + Angular desktop app is ready.</p>
      <button
        type="button"
        class="rounded-md bg-sky-600 px-4 py-2 text-white hover:bg-sky-700"
        (click)="count.update(increment)"
      >
        Clicked {{ count() }} times
      </button>
    </main>
    <router-outlet />
  `,
})
export class App {
  protected readonly faPaperPlane = faPaperPlane;
  protected readonly title = signal('Missive');
  protected readonly count = signal(0);
  protected readonly increment = (value: number) => value + 1;
}
