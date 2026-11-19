import { TestBed } from '@angular/core/testing';
import { Bridge } from './bridge';
import { UpdateStore } from './update-store';

describe('UpdateStore', () => {
  function setup(isDesktop = true) {
    const send = vi.fn();
    TestBed.configureTestingModule({
      providers: [{ provide: Bridge, useValue: { isDesktop, send } }],
    });
    return { store: TestBed.inject(UpdateStore), send };
  }

  it('shares a check in progress instead of starting another', async () => {
    const { store, send } = setup();
    let answer!: (status: unknown) => void;
    send.mockReturnValue(new Promise((resolve) => (answer = resolve)));

    const startup = store.check();
    const manual = store.checkNow();
    expect(store.checking()).toBe(true);
    answer({ state: 'ready', version: '2.0.0' });
    await Promise.all([startup, manual]);

    expect(send).toHaveBeenCalledTimes(1);
    expect(store.checking()).toBe(false);
    expect(store.readyVersion()).toBe('2.0.0');
    expect(store.lastCheck()).toBeNull();
  });

  it('keeps startup failures out of the menu but reports failures on demand', async () => {
    const { store, send } = setup();
    send.mockRejectedValue(new Error('rate limited'));
    vi.spyOn(console, 'warn').mockImplementation(() => {});

    await store.check();
    expect(store.lastCheck()).toBeNull();

    await store.checkNow();
    expect(store.lastCheck()).toEqual({ ok: false, message: 'rate limited' });
  });

  it('records whether this copy can update itself', async () => {
    const { store, send } = setup();
    send.mockResolvedValue({ state: 'unsupported' });

    await store.checkNow();

    expect(store.supported()).toBe(false);
    expect(store.lastCheck()).toBeNull();
  });

  it('does nothing outside the desktop host', async () => {
    const { store, send } = setup(false);

    await store.checkNow();

    expect(store.canCheck).toBe(false);
    expect(send).not.toHaveBeenCalled();
  });
});
