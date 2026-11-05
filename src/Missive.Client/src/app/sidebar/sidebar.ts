import { Component, inject } from '@angular/core';
import {
  SIDEBAR_DEFAULT_WIDTH,
  SIDEBAR_MAX_WIDTH,
  SIDEBAR_MIN_WIDTH,
  WorkspaceStore,
} from '../workspace/workspace-store';
import { EnvironmentsView } from './environments-view';
import { ExplorerView } from './explorer-view';
import { GitView } from './git-view';

const KEYBOARD_STEP = 16;

@Component({
  selector: 'app-sidebar',
  imports: [ExplorerView, EnvironmentsView, GitView],
  host: {
    class: 'relative flex shrink-0 flex-col border-r border-slate-200 bg-slate-50',
    role: 'complementary',
    '[style.width.px]': 'store.sidebarWidth()',
  },
  template: `
    @switch (store.sidebarView()) {
      @case ('explorer') {
        <app-explorer-view />
      }
      @case ('environments') {
        <app-environments-view />
      }
      @case ('git') {
        <app-git-view />
      }
    }
    <div
      class="absolute inset-y-0 -right-1 z-10 w-2 cursor-col-resize touch-none select-none focus:outline-none"
      [class.resizing]="dragging"
      role="separator"
      aria-orientation="vertical"
      aria-label="Resize sidebar"
      tabindex="0"
      [attr.aria-valuemin]="minWidth"
      [attr.aria-valuemax]="maxWidth"
      [attr.aria-valuenow]="store.sidebarWidth()"
      title="Drag to resize, double-click to reset"
      (pointerdown)="startResize($event)"
      (pointermove)="resize($event)"
      (pointerup)="endResize($event)"
      (pointercancel)="endResize($event)"
      (lostpointercapture)="dragging = false"
      (dblclick)="store.setSidebarWidth(defaultWidth)"
      (keydown)="onKeydown($event)"
    ></div>
  `,
  styles: `
    [role='separator'] {
      &::after {
        content: '';
        position: absolute;
        inset-block: 0;
        left: 50%;
        width: 2px;
        transform: translateX(-50%);
        background: transparent;
        transition: background-color 0.15s;
      }

      &:hover::after,
      &:focus-visible::after,
      &.resizing::after {
        background: var(--color-sky-500, #0ea5e9);
      }
    }
  `,
})
export class Sidebar {
  protected readonly store = inject(WorkspaceStore);
  protected readonly minWidth = SIDEBAR_MIN_WIDTH;
  protected readonly maxWidth = SIDEBAR_MAX_WIDTH;
  protected readonly defaultWidth = SIDEBAR_DEFAULT_WIDTH;
  protected dragging = false;

  private startX = 0;
  private startWidth = 0;

  protected startResize(event: PointerEvent): void {
    if (event.button !== 0) {
      return;
    }
    event.preventDefault();
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    this.dragging = true;
    this.startX = event.clientX;
    this.startWidth = this.store.sidebarWidth();
  }

  protected resize(event: PointerEvent): void {
    if (this.dragging) {
      this.store.setSidebarWidth(this.startWidth + event.clientX - this.startX);
    }
  }

  protected endResize(event: PointerEvent): void {
    const handle = event.currentTarget as HTMLElement;
    if (handle.hasPointerCapture(event.pointerId)) {
      handle.releasePointerCapture(event.pointerId);
    }
    this.dragging = false;
  }

  protected onKeydown(event: KeyboardEvent): void {
    const width = this.store.sidebarWidth();
    const next: Record<string, number> = {
      ArrowLeft: width - KEYBOARD_STEP,
      ArrowRight: width + KEYBOARD_STEP,
      Home: this.minWidth,
      End: this.maxWidth,
    };
    if (event.key in next) {
      event.preventDefault();
      this.store.setSidebarWidth(next[event.key]);
    }
  }
}
