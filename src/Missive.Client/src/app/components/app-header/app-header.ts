import { Component, ElementRef, inject, signal } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faCircleArrowUp,
  faGear,
  faMoon,
  faFolderOpen,
  faLayerGroup,
  faPaperPlane,
} from '@fortawesome/free-solid-svg-icons';
import { EnvironmentStore } from '../../services/environment-store';
import { ThemeStore } from '../../services/theme-store';
import { UpdateStore } from '../../services/update-store';
import { WorkspaceStore } from '../../services/workspace-store';

/** Main application header: app name, open workspace and global options. */
@Component({
  selector: 'app-header',
  imports: [FaIconComponent],
  host: {
    class:
      'always-dark flex h-10 shrink-0 items-center gap-2 border-b border-slate-700 bg-slate-900 px-3',
    role: 'banner',
    '(document:click)': 'onDocumentClick($event)',
    '(document:keydown.escape)': 'onEscape()',
  },
  templateUrl: './app-header.html',
})
export class AppHeader {
  protected readonly store = inject(WorkspaceStore);
  protected readonly environments = inject(EnvironmentStore);
  protected readonly updates = inject(UpdateStore);
  protected readonly theme = inject(ThemeStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  protected readonly menuOpen = signal(false);
  protected readonly faGear = faGear;
  protected readonly faMoon = faMoon;
  protected readonly faCircleArrowUp = faCircleArrowUp;
  protected readonly faPaperPlane = faPaperPlane;
  protected readonly faLayerGroup = faLayerGroup;
  protected readonly faFolderOpen = faFolderOpen;

  protected onDocumentClick(event: MouseEvent): void {
    if (!this.host.nativeElement.contains(event.target as Node)) this.menuOpen.set(false);
  }

  protected onEscape(): void {
    if (!this.menuOpen()) return;
    this.menuOpen.set(false);
    this.host.nativeElement.querySelector<HTMLButtonElement>('[aria-label="Settings"]')?.focus();
  }
}
