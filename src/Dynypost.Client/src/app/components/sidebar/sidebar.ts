import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import {
  SIDEBAR_DEFAULT_WIDTH,
  SIDEBAR_MAX_WIDTH,
  SIDEBAR_MIN_WIDTH,
  WorkspaceStore,
} from '../../services/workspace-store';

const KEYBOARD_STEP = 16;

@Component({
  selector: 'app-sidebar',
  imports: [RouterOutlet],
  host: {
    class: 'relative flex shrink-0 flex-col border-r border-slate-200 bg-slate-50',
    role: 'complementary',
    '[style.width.px]': 'store.sidebarWidth()',
  },
  templateUrl: './sidebar.html',
  styleUrl: './sidebar.scss',
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
