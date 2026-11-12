import { AuthDto, KeyValueDto, ResponseDto } from '../models/messages';
import { MockBackend } from './mock-backend';

describe('MockBackend request.send', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  const row = (key: string, value: string): KeyValueDto => ({ key, value, enabled: true });

  async function send(
    url: string,
    headers: KeyValueDto[],
    auth: AuthDto = { type: 'none' },
  ): Promise<NonNullable<ResponseDto['sent']>> {
    const reply = new MockBackend().handle({
      id: '1',
      type: 'request.send',
      payload: {
        id: 'send-1',
        request: {
          name: 'Test',
          method: 'GET',
          url,
          params: [],
          headers,
          auth,
          body: { type: 'none', content: '' },
        },
      },
    });
    await vi.advanceTimersByTimeAsync(1000);
    const result = await reply;
    if (!result.ok) throw new Error(result.error.message);
    return (result.payload as ResponseDto).sent!;
  }

  it('reports Host first and adds User-Agent, Accept-Encoding and auth, hidden', async () => {
    const sent = await send('https://a.test/x', [], { type: 'bearer', token: 'abc' });

    expect(sent.headers).toEqual([
      { key: 'Host', value: 'a.test' },
      { key: 'Authorization', value: 'Bearer ••••••' },
      { key: 'User-Agent', value: 'Missive' },
      { key: 'Accept-Encoding', value: 'gzip, deflate, br' },
    ]);
  });

  it('keeps the Host, User-Agent and Accept-Encoding the request sets, once each', async () => {
    const sent = await send('https://127.0.0.1/x', [
      row('Host', 'api.example.com'),
      row('User-Agent', 'curl'),
      row('Accept-Encoding', 'gzip'),
    ]);

    expect(sent.headers).toEqual([
      { key: 'Host', value: 'api.example.com' },
      { key: 'User-Agent', value: 'curl' },
      { key: 'Accept-Encoding', value: 'gzip, deflate, br' },
    ]);
  });

  it('hides an API key the request sets itself, in a header or in the query', async () => {
    const inHeader = await send('https://a.test/x', [row('x-api-key', 'explicit-secret')], {
      type: 'apiKey',
      key: 'X-Api-Key',
      value: 'configured',
      in: 'header',
    });
    expect(inHeader.headers).toContainEqual({ key: 'x-api-key', value: '••••••' });
    expect(JSON.stringify(inHeader)).not.toContain('secret');

    const inQuery = await send('https://a.test/x?api_key=actual-secret&api_key=other', [], {
      type: 'apiKey',
      key: 'api_key',
      value: 'configured',
      in: 'query',
    });
    expect(new URL(inQuery.url).searchParams.getAll('api_key')).toEqual(['••••••', '••••••']);
  });
});

describe('MockBackend response history', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('keeps the responses of saved requests and follows them when they move', async () => {
    const backend = new MockBackend();
    const call = async (type: string, payload?: unknown) => {
      const reply = backend.handle({ id: '1', type, payload } as never);
      await vi.advanceTimersByTimeAsync(1000);
      const result = await reply;
      if (!result.ok) throw new Error(result.error.message);
      return result.payload as never;
    };
    await call('workspace.open', { path: '/preview' });
    const request = await call('request.load', { path: 'requests/health.yaml' });

    const sent: ResponseDto = await call('request.send', {
      id: 's1',
      request,
      path: 'requests/health.yaml',
    });
    const unsaved: ResponseDto = await call('request.send', { id: 's2', request });
    await call('request.move', { path: 'requests/health.yaml', folder: 'requests/orders' });
    const listed: { id: string }[] = await call('history.list', {
      path: 'requests/orders/health.yaml',
    });

    expect(sent.historyId).toBeTruthy();
    expect(unsaved.historyId).toBeFalsy();
    expect(listed.map((e) => e.id)).toEqual([sent.historyId]);
    const kept: { response: ResponseDto } = await call('history.get', {
      path: 'requests/orders/health.yaml',
      id: sent.historyId,
    });
    expect(kept.response.cookies?.[0].value).toBe('••••••');
  });
});
