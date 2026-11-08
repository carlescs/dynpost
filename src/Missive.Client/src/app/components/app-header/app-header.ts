import { Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faChevronDown,
  faCircleArrowUp,
  faGear,
  faMoon,
  faFolderOpen,
  faLayerGroup,
  faPaperPlane,
  faXmark,
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
  protected readonly recentOpen = signal(false);
  /** Always rendered, unlike the panel, so it contains the click that opens the panel. */
  private readonly recentMenu = viewChild.required<ElementRef<HTMLElement>>('recentMenu');
  /** Recent workspaces other than the open one. */
  protected readonly otherRecent = computed(() =>
    this.store.recent().filter((r) => r.path !== this.store.workspace()?.root),
  );
  protected readonly faChevronDown = faChevronDown;
  protected readonly faGear = faGear;
  protected readonly faMoon = faMoon;
  protected readonly faCircleArrowUp = faCircleArrowUp;
  protected readonly faPaperPlane = faPaperPlane;
  protected readonly faLayerGroup = faLayerGroup;
  protected readonly faFolderOpen = faFolderOpen;
  protected readonly faXmark = faXmark;

  protected toggleRecent(): void {
    const open = !this.recentOpen();
    this.recentOpen.set(open);
    this.menuOpen.set(false);
    // Folders may have been removed since the list was read.
    if (open) void this.store.loadRecent();
  }

  protected openRecent(path: string): void {
    this.recentOpen.set(false);
    void this.store.openRecent(path);
  }

  protected onDocumentClick(event: MouseEvent): void {
    const target = event.target as Node;
    if (!this.host.nativeElement.contains(target)) this.menuOpen.set(false);
    if (!this.recentMenu().nativeElement.contains(target)) this.recentOpen.set(false);
  }

  protected onEscape(): void {
    if (this.recentOpen()) {
      this.recentOpen.set(false);
      this.host.nativeElement
        .querySelector<HTMLButtonElement>('[aria-label="Recent workspaces"][aria-controls]')
        ?.focus();
      return;
    }
    if (!this.menuOpen()) return;
    this.menuOpen.set(false);
    this.host.nativeElement.querySelector<HTMLButtonElement>('[aria-label="Settings"]')?.focus();
  }
}
