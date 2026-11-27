import { TestBed } from '@angular/core/testing';
import { Bridge } from './bridge';
import { WindowFrame } from './window-frame';
import { WindowStateDto } from '../models/messages';

describe('WindowFrame', () => {
  function setup(state: WindowStateDto, nativeTitleBar = false) {
    let stateChanged: ((state: WindowStateDto) => void) | undefined;
    const send = vi.fn(async (type: string, ...args: unknown[]) => {
      if (type === 'window.get') return state;
      if (type === 'settings.get') return { nativeTitleBar };
      if (type === 'settings.setTitleBar') return { nativeTitleBar: true };
      if (type === 'settings.setTray') return { nativeTitleBar, tray: args[0] };
      return null;
    });
    const on = vi.fn((_: string, listener: (state: WindowStateDto) => void) => {
      stateChanged = listener;
      return () => {};
    });
    TestBed.configureTestingModule({
      providers: [{ provide: Bridge, useValue: { isDesktop: true, send, on } }],
    });
    const frame = TestBed.inject(WindowFrame);
    return { frame, send, emit: (next: WindowStateDto) => stateChanged?.(next) };
  }

  function mouseDownOn(frame: WindowFrame, target: Element, screenX = 10): MouseEvent {
    const event = new MouseEvent('mousedown', { button: 0, detail: 1, screenX, cancelable: true });
    Object.defineProperty(event, 'target', { value: target });
    frame.titleBarMouseDown(event);
    return event;
  }

  it('draws window buttons and the top resize edges for its own title bar on Windows', async () => {
    const { frame } = setup({
      frame: 'custom',
      platform: 'windows',
      maximized: false,
      tray: false,
    });
    await frame.load();

    expect(frame.custom()).toBe(true);
    expect(frame.showButtons()).toBe(true);
    expect(frame.resizeEdges()).toEqual(['top', 'topLeft', 'topRight']);
  });

  it('offers every edge on Linux, and none while maximized', async () => {
    const { frame, emit } = setup({
      frame: 'custom',
      platform: 'linux',
      maximized: false,
      tray: false,
    });
    await frame.load();
    expect(frame.resizeEdges()).toHaveLength(8);

    emit({ frame: 'custom', platform: 'linux', maximized: true, tray: false });

    expect(frame.resizeEdges()).toEqual([]);
  });

  it('keeps the traffic lights on macOS instead of drawing buttons', async () => {
    const { frame } = setup({ frame: 'custom', platform: 'macos', maximized: false, tray: false });
    await frame.load();

    expect(frame.showButtons()).toBe(false);
    expect(frame.trafficLights()).toBe(true);
    expect(frame.resizeEdges()).toEqual([]);
  });

  it('moves the window from a press on the bar and maximizes it on a double click', async () => {
    const { frame, send } = setup({
      frame: 'custom',
      platform: 'windows',
      maximized: false,
      tray: false,
    });
    await frame.load();
    const bar = document.createElement('header');

    expect(mouseDownOn(frame, bar).defaultPrevented).toBe(true);
    expect(send).toHaveBeenCalledWith('window.startDrag');

    // The system's move eats the first release, so the page counts both presses as single clicks.
    mouseDownOn(frame, bar);
    expect(send).toHaveBeenCalledWith('window.toggleMaximize');
    expect(send.mock.calls.filter(([type]) => type === 'window.startDrag')).toHaveLength(1);
  });

  it('takes presses far apart in time or place as separate drags', async () => {
    vi.useFakeTimers();
    try {
      const { frame, send } = setup({
        frame: 'custom',
        platform: 'linux',
        maximized: false,
        tray: false,
      });
      await frame.load();
      const bar = document.createElement('header');

      mouseDownOn(frame, bar);
      vi.advanceTimersByTime(800);
      mouseDownOn(frame, bar);
      mouseDownOn(frame, bar, 200);

      expect(send).not.toHaveBeenCalledWith('window.toggleMaximize');
      expect(send.mock.calls.filter(([type]) => type === 'window.startDrag')).toHaveLength(3);
    } finally {
      vi.useRealTimers();
    }
  });

  it('leaves presses on the header’s controls to them', async () => {
    const { frame, send } = setup({
      frame: 'custom',
      platform: 'windows',
      maximized: false,
      tray: false,
    });
    await frame.load();
    const button = document.createElement('button');
    const icon = button.appendChild(document.createElement('span'));

    expect(mouseDownOn(frame, icon).defaultPrevented).toBe(false);
    expect(send).not.toHaveBeenCalledWith('window.startDrag');
  });

  it('does nothing with the system title bar', async () => {
    const { frame, send } = setup({
      frame: 'native',
      platform: 'windows',
      maximized: false,
      tray: false,
    });
    await frame.load();

    mouseDownOn(frame, document.createElement('header'));

    expect(frame.showButtons()).toBe(false);
    expect(frame.resizeEdges()).toEqual([]);
    expect(send).not.toHaveBeenCalledWith('window.startDrag');
  });

  it('saves the title bar choice and says it applies after a restart', async () => {
    const { frame, send } = setup({
      frame: 'custom',
      platform: 'linux',
      maximized: false,
      tray: false,
    });
    await frame.load();
    expect(frame.restartNeeded()).toBe(false);

    await frame.setNativeTitleBar(true);

    expect(send).toHaveBeenCalledWith('settings.setTitleBar', { native: true });
    expect(frame.nativeTitleBar()).toBe(true);
    expect(frame.restartNeeded()).toBe(true);
  });

  it('offers no title bar setting in the browser preview', async () => {
    const { frame } = setup({ frame: 'native', platform: 'web', maximized: false, tray: false });
    await frame.load();

    expect(frame.configurable()).toBe(false);
    expect(frame.restartNeeded()).toBe(false);
  });

  it('saves the tray settings and closes to the tray only where the system has one', async () => {
    const { frame, send } = setup({
      frame: 'custom',
      platform: 'windows',
      maximized: false,
      tray: true,
    });
    await frame.load();
    expect(frame.trayAvailable()).toBe(true);
    expect(frame.closesToTray()).toBe(false);

    await frame.setTray({ closeToTray: true });

    expect(send).toHaveBeenCalledWith('settings.setTray', {
      minimizeToTray: false,
      closeToTray: true,
    });
    expect(frame.closesToTray()).toBe(true);
    frame.hideToTray();
    expect(send).toHaveBeenCalledWith('window.hide');
  });

  it('offers no tray where the system has none', async () => {
    const { frame } = setup({ frame: 'custom', platform: 'linux', maximized: false, tray: false });
    await frame.load();
    frame.tray.set({ minimizeToTray: false, closeToTray: true });

    expect(frame.trayAvailable()).toBe(false);
    expect(frame.closesToTray()).toBe(false);
  });
});
