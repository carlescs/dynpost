import { Injectable, computed, inject, signal } from '@angular/core';
import { Bridge } from './bridge';
import { WindowEdge, WindowStateDto } from '../models/messages';

/** How close in time and place two presses on the title bar must be to count as a double click. */
const DOUBLE_CLICK_MS = 500;
const DOUBLE_CLICK_DISTANCE = 4;

/** The edges the app offers for resizing, per platform; the others are the system's. */
const RESIZE_EDGES: Record<WindowStateDto['platform'], readonly WindowEdge[]> = {
  // Windows keeps its own side and bottom borders; only the caption's top edge is gone.
  windows: ['top', 'topLeft', 'topRight'],
  // An undecorated GTK window has no borders at all.
  linux: ['top', 'bottom', 'left', 'right', 'topLeft', 'topRight', 'bottomLeft', 'bottomRight'],
  // macOS keeps its native edges.
  macos: [],
  web: [],
};

/**
 * The desktop window. Unless the user chose the system's title bar, the app's header is the
 * title bar: it moves the window, and draws the window buttons where the platform has none.
 * The choice is read when the window opens, so changing it applies after a restart.
 */
@Injectable({ providedIn: 'root' })
export class WindowFrame {
  private readonly bridge = inject(Bridge);

  readonly state = signal<WindowStateDto>({ frame: 'native', platform: 'web', maximized: false });
  /** The saved choice, which the window follows from the next start. */
  readonly nativeTitleBar = signal(false);

  /** Whether the app's header is the window's title bar. */
  readonly custom = computed(() => this.state().frame === 'custom');
  /** Whether the header draws minimize, maximize and close (macOS keeps its traffic lights). */
  readonly showButtons = computed(() => this.custom() && this.state().platform !== 'macos');
  /** Whether the header leaves room for macOS's traffic lights in its top-left corner. */
  readonly trafficLights = computed(() => this.custom() && this.state().platform === 'macos');
  /** Whether the setting can be changed: only in the desktop app. */
  readonly configurable = computed(() => this.state().platform !== 'web');
  /** Whether the saved choice differs from the window's, so a restart is needed. */
  readonly restartNeeded = computed(
    () => this.configurable() && this.nativeTitleBar() !== !this.custom(),
  );
  /** How close in time and place two presses on the title bar must be to count as a double click. */
  DOUBLE_CLICK_MS = 500;
  DOUBLE_CLICK_DISTANCE = 4;

  /** The edges the app offers for resizing; none while maximized. */
  readonly resizeEdges = computed(() =>
    this.custom() && !this.state().maximized ? RESIZE_EDGES[this.state().platform] : [],
  );

  private listening = false;
  private lastPress: { time: number; x: number; y: number } | null = null;

  /** Reads the window's title bar and state, and follows it as it is maximized and restored. */
  async load(): Promise<void> {
    if (!this.listening) {
      this.listening = true;
      this.bridge.on('window.stateChanged', (state) => this.state.set(state));
    }
    try {
      const [state, settings] = await Promise.all([
        this.bridge.send('window.get'),
        this.bridge.send('settings.get'),
      ]);
      if (state) this.state.set(state);
      this.nativeTitleBar.set(settings?.nativeTitleBar ?? false);
    } catch (error) {
      console.warn('Could not read the window state', error);
    }
  }

  async setNativeTitleBar(native: boolean): Promise<void> {
    const previous = this.nativeTitleBar();
    this.nativeTitleBar.set(native);
    try {
      await this.bridge.send('settings.setTitleBar', { native });
    } catch (error) {
      this.nativeTitleBar.set(previous);
      console.warn('Could not save the title bar setting', error);
    }
  }

  minimize(): void {
    this.call(() => this.bridge.send('window.minimize'));
  }

  toggleMaximize(): void {
    this.call(() => this.bridge.send('window.toggleMaximize'));
  }

  /**
   * Starts moving the window from a press on the title bar; a double click maximizes or restores
   * it instead, as on a system title bar. Presses on the header's controls are theirs.
   */
  titleBarMouseDown(event: MouseEvent): void {
    if (!this.custom() || event.button !== 0 || isControl(event.target)) return;
    // Keep the press from selecting text or focusing anything while the system moves the window.
    event.preventDefault();
    // The system's move takes the mouse, so the page never sees the first click's release and
    // counts the second press as a new click (no dblclick, no detail of 2): time presses instead.
    const previous = this.lastPress;
    const press = { time: Date.now(), x: event.screenX, y: event.screenY };
    const double =
      !!previous &&
      press.time - previous.time <= DOUBLE_CLICK_MS &&
      Math.abs(press.x - previous.x) <= DOUBLE_CLICK_DISTANCE &&
      Math.abs(press.y - previous.y) <= DOUBLE_CLICK_DISTANCE;
    this.lastPress = double ? null : press;
    if (double) this.toggleMaximize();
    else this.call(() => this.bridge.send('window.startDrag'));
  }

  /** Starts resizing the window from a press on one of the resize edges. */
  resizeMouseDown(event: MouseEvent, edge: WindowEdge): void {
    if (event.button !== 0) return;
    event.preventDefault();
    this.call(() => this.bridge.send('window.startResize', { edge }));
  }

  private call(send: () => Promise<unknown>): void {
    send().catch((error) => console.warn('Window call failed', error));
  }
}

/** Whether a press on `target` belongs to a control in the title bar rather than to the bar. */
function isControl(target: EventTarget | null): boolean {
  return (
    target instanceof Element &&
    !!target.closest(
      'button, a, input, select, textarea, label, summary, [role="button"], [tabindex], [data-no-drag]',
    )
  );
}
