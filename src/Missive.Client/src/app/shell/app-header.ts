import { Component } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import { faPaperPlane } from '@fortawesome/free-solid-svg-icons';

/** Main application header. Hosts the app name; global options will go on the right. */
@Component({
  selector: 'app-header',
  imports: [FaIconComponent],
  host: {
    class: 'flex h-10 shrink-0 items-center gap-2 border-b border-slate-700 bg-slate-900 px-3',
    role: 'banner',
  },
  template: `
    <fa-icon class="text-sky-400" [icon]="faPaperPlane" />
    <span class="text-sm font-semibold tracking-wide text-white">Missive</span>
    <div class="ml-auto flex items-center gap-1">
      <!-- Global options (settings, workspace switcher, …) go here. -->
    </div>
  `,
})
export class AppHeader {
  protected readonly faPaperPlane = faPaperPlane;
}
