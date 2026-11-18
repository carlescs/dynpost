import { Component, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faChevronDown,
  faCircleArrowUp,
  faCircleInfo,
  faClockRotateLeft,
  faCodeBranch,
  faKey,
  faGear,
  faKeyboard,
  faMoon,
  faFolderOpen,
  faLayerGroup,
  faMagnifyingGlass,
  faPaperPlane,
  faRobot,
  faWindowMaximize,
  faWindowMinimize,
  faWindowRestore,
  faXmark,
} from '@fortawesome/free-solid-svg-icons';
import { AboutDialog } from '../about-dialog/about-dialog';
import { openCloneDialog } from '../clone-dialog/open-clone-dialog';
import { HistorySettingsDialog } from '../history-settings-dialog/history-settings-dialog';
import { SecretSettingsDialog } from '../secret-settings-dialog/secret-settings-dialog';
import { McpSettingsDialog } from '../mcp-settings-dialog/mcp-settings-dialog';
import { ShortcutsDialog } from '../shortcuts-dialog/shortcuts-dialog';
import { Dialogs } from '../../services/dialogs';
import { EnvironmentStore } from '../../services/environment-store';
import { environmentColor } from '../../models/environment-colors';
import { Shortcuts } from '../../services/shortcuts';
import { formatCombo } from '../../models/shortcuts';
import { ThemeStore } from '../../services/theme-store';
import { UpdateStore } from '../../services/update-store';
import { WindowFrame } from '../../services/window-frame';
import { WorkspaceStore } from '../../services/workspace-store';

/**
 * Main application header: app name, open workspace and global options. Unless the user chose
 * the system's title bar, it is also the window's title bar (see `WindowFrame`).
 */
@Component({
  selector: 'app-header',
  imports: [FaIconComponent],
  host: {
    class:
      'always-dark relative flex h-10 shrink-0 items-center gap-2 border-b border-slate-700 bg-slate-900 px-3',
    role: 'banner',
    '[class.pl-20]': 'frame.trafficLights()',
    '[class.pr-0]': 'frame.showButtons()',
    '(mousedown)': 'frame.titleBarMouseDown($event)',
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
  protected readonly frame = inject(WindowFrame);
  private readonly dialogs = inject(Dialogs);
  protected readonly shortcuts = inject(Shortcuts);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  protected readonly menuOpen = signal(false);
  protected readonly recentOpen = signal(false);
  /** Always rendered, unlike the panel, so it contains the click that opens the panel. */
  private readonly recentMenu = viewChild.required<ElementRef<HTMLElement>>('recentMenu');
  /** The selected environment's color, shown on the picker and along the header's bottom edge. */
  protected readonly activeColor = computed(() =>
    environmentColor(this.environments.active()?.color),
  );
  /** Recent workspaces other than the open one. */
  protected readonly otherRecent = computed(() =>
    this.store.recent().filter((r) => r.path !== this.store.workspace()?.root),
  );
  protected readonly faChevronDown = faChevronDown;
  protected readonly faGear = faGear;
  protected readonly faCircleInfo = faCircleInfo;
  protected readonly faClockRotateLeft = faClockRotateLeft;
  protected readonly faKey = faKey;
  protected readonly faMoon = faMoon;
  protected readonly faKeyboard = faKeyboard;
  protected readonly faCircleArrowUp = faCircleArrowUp;
  protected readonly faPaperPlane = faPaperPlane;
  protected readonly faLayerGroup = faLayerGroup;
  protected readonly faMagnifyingGlass = faMagnifyingGlass;
  protected readonly quickOpenKeys = formatCombo({ key: 'p', mod: true });
  protected readonly faFolderOpen = faFolderOpen;
  protected readonly faCodeBranch = faCodeBranch;
  protected readonly faXmark = faXmark;
  protected readonly faRobot = faRobot;
  protected readonly faWindowMinimize = faWindowMinimize;
  protected readonly faWindowMaximize = faWindowMaximize;
  protected readonly faWindowRestore = faWindowRestore;

  constructor() {
    // Removing the last other recent workspace (or a reload finding none) leaves nothing to
    // show, and the button that toggles the panel disappears with it: close the panel rather
    // than leave an empty box open, and keep focus in the header.
    effect(() => {
      if (!this.recentOpen() || this.otherRecent().length) return;
      this.recentOpen.set(false);
      this.host.nativeElement
        .querySelector<HTMLButtonElement>('[aria-label="Open workspace folder"]')
        ?.focus();
    });
  }

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

  protected cloneRepository(): void {
    this.recentOpen.set(false);
    void openCloneDialog(this.dialogs);
  }

  protected showHistorySettings(): void {
    this.menuOpen.set(false);
    this.dialogs.open(HistorySettingsDialog, { ariaLabelledBy: 'history-settings-title' });
  }

  protected showSecretSettings(): void {
    this.menuOpen.set(false);
    this.dialogs.open(SecretSettingsDialog, { ariaLabelledBy: 'secret-settings-title' });
  }

  protected showMcpSettings(): void {
    this.menuOpen.set(false);
    this.dialogs.open(McpSettingsDialog, { ariaLabelledBy: 'mcp-settings-title' });
  }

  protected showAbout(): void {
    this.menuOpen.set(false);
    this.dialogs.open(AboutDialog, { ariaLabelledBy: 'about-title' });
  }

  protected showShortcuts(): void {
    this.menuOpen.set(false);
    this.dialogs.open(ShortcutsDialog, { ariaLabelledBy: 'shortcuts-title' });
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
