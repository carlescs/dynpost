import {
  Component,
  ElementRef,
  HostListener,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faGaugeHigh,
  faLayerGroup,
  faPlus,
  faSliders,
  faXmark,
} from '@fortawesome/free-solid-svg-icons';
import { EnvironmentDraft, EnvironmentStore } from '../../services/environment-store';
import { METHOD_COLORS, RequestDocument } from '../../models/workspace';
import { WorkspaceStore } from '../../services/workspace-store';
import { RunPlanDraft, RunStore, isRunPlanDirty, isRunPlanRunning } from '../../services/run-store';
import { FolderDraft, FolderSettingsStore } from '../../services/folder-settings-store';
import { DISCARD, Dialogs } from '../../services/dialogs';

/** The tabs a "Close …" command of the context menu closes, relative to the tab it was opened on. */
export type CloseScope = 'this' | 'others' | 'right' | 'saved' | 'all';

/** The tab context menu: the tab it was opened on and where it is shown. */
interface TabMenu {
  key: string;
  x: number;
  y: number;
}

/** The width the context menu is given room for when it is opened near the window's right edge. */
const MENU_WIDTH = 200;

type Tab =
  | { kind: 'request'; key: string; doc: RequestDocument }
  | { kind: 'environment'; key: string; draft: EnvironmentDraft }
  | { kind: 'run'; key: string; plan: RunPlanDraft }
  | { kind: 'folder'; key: string; folder: FolderDraft };

/**
 * The editor's tabs: open requests, environments, run plans and folder settings, in the order they
 * were opened. The strip scrolls sideways (also with the mouse wheel) under a thin overlay
 * scrollbar that takes no room from the tabs, and a right click opens a menu to close tabs in bulk.
 */
@Component({
  selector: 'app-document-tabs',
  imports: [FaIconComponent],
  host: { class: 'flex h-9 shrink-0 items-stretch border-b border-slate-200 bg-slate-100' },
  templateUrl: './document-tabs.html',
  styleUrl: './document-tabs.scss',
})
export class DocumentTabs {
  protected readonly store = inject(WorkspaceStore);
  private readonly environments = inject(EnvironmentStore);
  private readonly runs = inject(RunStore);
  private readonly folders = inject(FolderSettingsStore);
  private readonly dialogs = inject(Dialogs);
  protected readonly methodColors = METHOD_COLORS;
  protected readonly faLayerGroup = faLayerGroup;
  protected readonly faGaugeHigh = faGaugeHigh;
  protected readonly faSliders = faSliders;
  protected readonly isRunning = isRunPlanRunning;
  protected readonly faPlus = faPlus;
  protected readonly faXmark = faXmark;

  protected readonly menuItems: { scope: CloseScope; label: string; separator?: boolean }[] = [
    { scope: 'this', label: 'Close' },
    { scope: 'others', label: 'Close others' },
    { scope: 'right', label: 'Close to the right' },
    { scope: 'saved', label: 'Close saved', separator: true },
    { scope: 'all', label: 'Close all' },
  ];

  /** The open context menu, if any. */
  protected readonly menu = signal<TabMenu | null>(null);

  private readonly strip = viewChild.required<ElementRef<HTMLElement>>('strip');
  /** The strip's scroll position and sizes, which place the overlay scrollbar's thumb. */
  private readonly scroll = signal({ left: 0, width: 0, client: 0 });
  /** Where a drag of the scrollbar thumb started: the pointer's x and the strip's scroll offset. */
  private drag: { x: number; left: number } | null = null;

  /** The thumb's offset and width in pixels, or `null` when every tab fits. */
  protected readonly thumb = computed(() => {
    const { left, width, client } = this.scroll();
    if (width <= client + 1 || client === 0) return null;
    const size = Math.max((client / width) * client, 24);
    const offset = (left / (width - client)) * (client - size);
    return { offset, size };
  });

  constructor() {
    // Keep the strip's measurements current as tabs open and close or the window resizes.
    effect((onCleanup) => {
      const element = this.strip().nativeElement;
      if (typeof ResizeObserver === 'undefined') return;
      const observer = new ResizeObserver(() => this.measure());
      observer.observe(element);
      onCleanup(() => observer.disconnect());
    });
    // Bring the active tab into view and re-measure whenever the tabs change.
    effect(() => {
      const active = this.store.activeTab();
      this.tabs();
      queueMicrotask(() => {
        this.measure();
        if (!active) return;
        const element = this.strip().nativeElement.querySelector(`[data-tab-key="${active}"]`);
        element?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
      });
    });
  }

  protected readonly tabs = computed(() => {
    const docs = new Map(this.store.documents().map((d) => [d.key, d]));
    const drafts = new Map(this.environments.drafts().map((d) => [d.key, d]));
    const plans = new Map(this.runs.drafts().map((d) => [d.key, d]));
    const folders = new Map(this.folders.drafts().map((d) => [d.key, d]));
    return this.store.tabs().flatMap((key): Tab[] => {
      const doc = docs.get(key);
      if (doc) return [{ kind: 'request', key, doc }];
      const draft = drafts.get(key);
      if (draft) return [{ kind: 'environment', key, draft }];
      const plan = plans.get(key);
      if (plan) return [{ kind: 'run', key, plan }];
      const folder = folders.get(key);
      return folder ? [{ kind: 'folder', key, folder }] : [];
    });
  });

  protected tabName(tab: Tab): string {
    switch (tab.kind) {
      case 'request':
        return tab.doc.name;
      case 'environment':
        return tab.draft.name || 'Untitled';
      case 'run':
        return this.runs.nameOf(tab.plan.path);
      case 'folder':
        return this.folders.nameOf(tab.folder.path);
    }
  }

  protected tabTitle(tab: Tab): string {
    switch (tab.kind) {
      case 'request':
        return tab.doc.path ?? 'Unsaved request';
      case 'environment':
        return tab.draft.id ? `environments/${tab.draft.id}` : 'Unsaved environment';
      case 'run':
        return isRunPlanRunning(tab.plan) ? `${tab.plan.path} (running)` : tab.plan.path;
      case 'folder':
        return `Settings of ${tab.folder.path}`;
    }
  }

  protected isDirty(tab: Tab): boolean {
    switch (tab.kind) {
      case 'request':
        return tab.doc.dirty;
      case 'environment':
        return tab.draft.dirty;
      case 'run':
        return isRunPlanDirty(tab.plan);
      case 'folder':
        return tab.folder.dirty;
    }
  }

  protected measure(): void {
    const element = this.strip().nativeElement;
    this.scroll.set({
      left: element.scrollLeft,
      width: element.scrollWidth,
      client: element.clientWidth,
    });
  }

  /** Scrolls the strip sideways with a vertical mouse wheel, which otherwise does nothing here. */
  protected wheel(event: WheelEvent): void {
    if (event.deltaX !== 0 || event.deltaY === 0) return;
    const element = this.strip().nativeElement;
    if (element.scrollWidth <= element.clientWidth) return;
    event.preventDefault();
    element.scrollLeft += event.deltaY;
  }

  protected startDrag(event: PointerEvent): void {
    event.preventDefault();
    (event.target as HTMLElement).setPointerCapture?.(event.pointerId);
    this.drag = { x: event.clientX, left: this.strip().nativeElement.scrollLeft };
  }

  protected moveDrag(event: PointerEvent): void {
    const thumb = this.thumb();
    if (!this.drag || !thumb) return;
    const { width, client } = this.scroll();
    const ratio = (width - client) / (client - thumb.size);
    this.strip().nativeElement.scrollLeft = this.drag.left + (event.clientX - this.drag.x) * ratio;
  }

  protected endDrag(): void {
    this.drag = null;
  }

  /** Closes a tab with a middle click, as browsers and editors do. */
  protected auxClick(event: MouseEvent, tab: Tab): void {
    if (event.button !== 1) return;
    event.preventDefault();
    this.close(tab);
  }

  protected openMenu(event: MouseEvent, tab: Tab): void {
    event.preventDefault();
    event.stopPropagation();
    const x = Math.max(0, Math.min(event.clientX, window.innerWidth - MENU_WIDTH));
    this.menu.set({ key: tab.key, x, y: event.clientY });
  }

  @HostListener('document:click')
  @HostListener('document:contextmenu')
  @HostListener('document:keydown.escape')
  @HostListener('window:blur')
  protected closeMenu(): void {
    this.menu.set(null);
  }

  /** The tabs a "Close …" command of the menu opened on `key` would close. */
  protected tabsToClose(key: string, scope: CloseScope): Tab[] {
    const tabs = this.tabs();
    const index = tabs.findIndex((t) => t.key === key);
    switch (scope) {
      case 'this':
        return tabs.filter((t) => t.key === key);
      case 'others':
        return tabs.filter((t) => t.key !== key);
      case 'right':
        return index < 0 ? [] : tabs.slice(index + 1);
      case 'saved':
        return tabs.filter(
          (t) => !this.isDirty(t) && !(t.kind === 'run' && isRunPlanRunning(t.plan)),
        );
      case 'all':
        return tabs;
    }
  }

  /**
   * Closes the tabs of a menu command. Unsaved changes are discarded after a single question that
   * covers every such tab (cancelling it closes nothing), and a running plan stays open. The tab the
   * menu was opened on is shown afterwards if it is still open.
   */
  protected async closeTabs(key: string, scope: CloseScope): Promise<void> {
    this.closeMenu();
    const closing = this.tabsToClose(key, scope);
    const unsaved = closing.filter((t) => this.isDirty(t));
    if (unsaved.length) {
      const what =
        unsaved.length === 1 ? `"${this.tabName(unsaved[0])}"` : `${unsaved.length} tabs`;
      if (!(await this.dialogs.confirm(`Discard unsaved changes to ${what}?`, DISCARD))) return;
    }
    for (const tab of closing) await this.closeAsync(tab, false);
    if (scope !== 'this' && scope !== 'all' && this.store.tabs().includes(key)) {
      this.store.activateTab(key);
    }
  }

  protected close(tab: Tab): void {
    void this.closeAsync(tab);
  }

  /** Closes a tab; `ask` is whether to ask before discarding its unsaved changes. */
  private closeAsync(tab: Tab, ask = true): Promise<void> {
    switch (tab.kind) {
      case 'request':
        return this.store.close(tab.doc.id, ask);
      case 'environment':
        return this.environments.close(tab.key, ask);
      case 'folder':
        return this.folders.close(tab.key, ask);
      case 'run':
        return this.runs.close(tab.key, ask);
    }
  }
}
