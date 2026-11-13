import { TestBed } from '@angular/core/testing';
import { Bridge, BridgeError } from './bridge';

describe('Bridge', () => {
  afterEach(() => {
    delete (window as unknown as { external?: unknown }).external;
  });

  describe('in the desktop host', () => {
    let sent: string[];
    let reply: (message: string) => void;
    let bridge: Bridge;

    beforeEach(() => {
      sent = [];
      (window as unknown as { external: unknown }).external = {
        sendMessage: (message: string) => sent.push(message),
        receiveMessage: (callback: (message: string) => void) => (reply = callback),
      };
      bridge = TestBed.inject(Bridge);
    });

    it('sends an envelope with a correlation id and resolves with the matching response', async () => {
      const first = bridge.send('request.load', { path: 'a.yaml' });
      const second = bridge.send('workspace.tree');
      const [a, b] = sent.map((m) => JSON.parse(m));
      expect(a).toEqual({ id: a.id, type: 'request.load', payload: { path: 'a.yaml' } });
      expect(b.type).toBe('workspace.tree');
      expect(a.id).not.toBe(b.id);

      reply(JSON.stringify({ id: b.id, ok: true, payload: { name: 'ws' } }));
      reply(JSON.stringify({ id: a.id, ok: true, payload: { name: 'A' } }));
      expect(await first).toEqual({ name: 'A' });
      expect(await second).toEqual({ name: 'ws' });
      expect(bridge.isDesktop).toBe(true);
    });

    it('rejects with a BridgeError carrying the host error', async () => {
      const result = bridge.send('workspace.tree');
      const { id } = JSON.parse(sent[0]);
      reply(JSON.stringify({ id, ok: false, error: { code: 'not_found', message: 'Gone' } }));
      const error = await result.catch((e: unknown) => e);
      expect(error).toBeInstanceOf(BridgeError);
      expect(error).toMatchObject({ code: 'not_found', message: 'Gone' });
    });

    it('ignores malformed and unknown messages', async () => {
      const errors = vi.spyOn(console, 'error').mockImplementation(() => undefined);
      const result = bridge.send('workspace.tree');
      const { id } = JSON.parse(sent[0]);
      reply('not json');
      reply(JSON.stringify({ id: 'other', ok: true, payload: 1 }));
      reply(JSON.stringify({ id, ok: true, payload: 2 }));
      expect(await result).toBe(2);
      expect(errors).toHaveBeenCalledOnce();
    });

    it('passes events from the host to their listeners until they stop listening', () => {
      const changed = vi.fn();
      const stop = bridge.on('workspace.changed', changed);

      reply(
        JSON.stringify({ event: 'workspace.changed', payload: { paths: ['requests/a.yaml'] } }),
      );
      stop();
      reply(JSON.stringify({ event: 'workspace.changed', payload: { paths: [] } }));

      expect(changed).toHaveBeenCalledExactlyOnceWith({ paths: ['requests/a.yaml'] });
    });
  });

  it('falls back to the in-memory mock backend in a browser', async () => {
    const bridge = TestBed.inject(Bridge);
    expect(bridge.isDesktop).toBe(false);
    expect(await bridge.send('workspace.openLast')).toBeNull();
  });
});
