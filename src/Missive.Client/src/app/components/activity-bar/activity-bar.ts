import { CdkDrag, CdkDragDrop, CdkDropList, moveItemInArray } from '@angular/cdk/drag-drop';
import { Component, ElementRef, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faCodeBranch,
  faFolderTree,
  faGaugeHigh,
  faLayerGroup,
  faRotateLeft,
} from '@fortawesome/free-solid-svg-icons';
import { Bridge } from '../../services/bridge';
import { GitStore } from '../../services/git-store';
import { filter, map } from 'rxjs';
import { WorkspaceStore } from '../../services/workspace-store';

/** The routed modules, one per activity: `#/explorer`, `#/environments`, `#/runs` and `#/git`. */
type Activity = 'explorer' | 'environments' | 'runs' | 'git';

interface ActivityItem {
  view: Activity;
  label: string;
  icon: typeof faFolderTree;
}

/** The activities in their default order. */
const ACTIVITIES: readonly ActivityItem[] = [
  { view: 'explorer', label: 'Explorer', icon: faFolderTree },
  { view: 'environments', label: 'Environments', icon: faLayerGroup },
  { view: 'runs', label: 'Load runs', icon: faGaugeHigh },
  { view: 'git', label: 'Source control', icon: faCodeBranch },
];
const DEFAULT_ORDER: readonly Activity[] = ACTIVITIES.map((item) => item.view);

/**
 * Puts the activities in a saved order: ids that are no longer activities are ignored, and
 * activities the order doesn't list (added since it was saved) keep their default place at the end.
 */
export function orderActivities(order: readonly string[]): Activity[] {
  const known = ACTIVITIES.map((item) => item.view);
  const listed = order.filter(
    (id, i): id is Activity => (known as string[]).includes(id) && order.indexOf(id) === i,
  );
  return [...listed, ...known.filter((view) => !listed.includes(view))];
}

@Component({
  selector: 'app-activity-bar',
  imports: [FaIconComponent, CdkDropList, CdkDrag],
  host: { class: 'always-dark flex w-12 shrink-0 flex-col items-center bg-slate-800 py-2' },
  templateUrl: './activity-bar.html',
})
export class ActivityBar {
  protected readonly store = inject(WorkspaceStore);
  protected readonly git = inject(GitStore);
  private readonly router = inject(Router);
  private readonly bridge = inject(Bridge);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  /** The activity whose module is routed, from the first segment of the URL. */
  private readonly current = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.activeRoute()),
    ),
    { initialValue: this.activeRoute() },
  );

  /** The order the user arranged the activities in, remembered in the user's settings. */
  private readonly order = signal<Activity[]>([...DEFAULT_ORDER]);
  protected readonly items = computed(() =>
    this.order().map((view) => ACTIVITIES.find((item) => item.view === view)!),
  );
  /** Whether the user moved the activities away from their default order. */
  protected readonly reordered = computed(() =>
    this.order().some((view, i) => view !== DEFAULT_ORDER[i]),
  );
  protected readonly faRotateLeft = faRotateLeft;
  /** The dragged copy is outside the bar, so it carries the bar's colors itself. */
  protected readonly previewClasses = ['always-dark', 'bg-slate-700', 'text-white', 'shadow-lg'];
  /** Bumped by each reorder, so a slow settings load doesn't undo it. */
  private reorders = 0;
  private saveQueue: Promise<void> = Promise.resolve();
  /** Set while a drag ends, so the click that may follow the drop doesn't switch activities. */
  private justDragged = false;

  constructor() {
    void this.load();
  }

  protected isActive(view: Activity): boolean {
    return this.store.sidebarOpen() && this.current() === view;
  }

  /** Routes to the activity's module, or toggles the sidebar when it is already routed. */
  protected show(view: Activity): void {
    if (this.justDragged) return;
    if (this.current() === view) {
      this.store.toggleSidebar();
    } else {
      this.store.toggleSidebar(true);
      void this.router.navigate(['/', view]);
    }
  }

  protected dragEnded(): void {
    this.justDragged = true;
    setTimeout(() => (this.justDragged = false));
  }

  protected drop(event: CdkDragDrop<unknown>): void {
    this.move(event.previousIndex, event.currentIndex);
  }

  /** Alt+Up and Alt+Down move the focused activity, the keyboard's way of dragging it. */
  protected onKeydown(event: KeyboardEvent, index: number): void {
    if (!event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
    const target =
      event.key === 'ArrowUp' ? index - 1 : event.key === 'ArrowDown' ? index + 1 : null;
    if (target === null) return;
    event.preventDefault();
    event.stopPropagation();
    if (target < 0 || target >= this.order().length) return;
    const view = this.order()[index];
    this.move(index, target);
    // The button is re-rendered at its new place; keep the focus on it.
    queueMicrotask(() =>
      this.host.nativeElement.querySelector<HTMLElement>(`[data-activity="${view}"]`)?.focus(),
    );
  }

  /** Puts the activities back in their default order. */
  protected resetOrder(): void {
    if (!this.reordered()) return;
    this.order.set([...DEFAULT_ORDER]);
    // An empty order is the default one, so views added later keep their default place.
    this.save([]);
  }

  private move(from: number, to: number): void {
    if (from === to) return;
    const order = [...this.order()];
    moveItemInArray(order, from, to);
    this.order.set(order);
    this.save(order);
  }

  private save(order: Activity[]): void {
    this.reorders++;
    this.saveQueue = this.saveQueue
      .then(() => this.bridge.send('settings.setActivityOrder', { order }))
      .then(
        () => undefined,
        (error) => console.warn('Could not save the order of the activities', error),
      );
  }

  private async load(): Promise<void> {
    const reorders = this.reorders;
    try {
      const settings = await this.bridge.send('settings.get');
      if (reorders === this.reorders) this.order.set(orderActivities(settings.activityOrder ?? []));
    } catch (error) {
      console.warn('Could not load the order of the activities', error);
    }
  }

  private activeRoute(): string {
    return this.router.parseUrl(this.router.url).root.children['primary']?.segments[0]?.path ?? '';
  }
}
