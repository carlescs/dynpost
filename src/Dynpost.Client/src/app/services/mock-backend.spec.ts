import {
  AuthDto,
  BridgeMessageType,
  DateFormat,
  KeyValueDto,
  QuickOpenItemDto,
  ResponseDto,
  RunComparisonDto,
  WorkspaceSearchDto,
} from '../models/messages';
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
      { key: 'User-Agent', value: 'Dynpost' },
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

describe('MockBackend locale.dateFormat', () => {
  it('reports the en-US patterns with twelve months and seven days', async () => {
    const reply = await new MockBackend().handle({ id: '1', type: 'locale.dateFormat' });

    if (!reply.ok) throw new Error(reply.error.message);
    const format = reply.payload as DateFormat;
    expect(format.shortDatePattern).toBe('M/d/yyyy');
    expect(format.monthNames).toHaveLength(12);
    expect(format.abbreviatedMonthNames[8]).toBe('Sep');
    expect(format.dayNames).toHaveLength(7);
    expect(format.abbreviatedDayNames[0]).toBe('Sun');
  });
});

describe('MockBackend about.openLink', () => {
  afterEach(() => vi.restoreAllMocks());

  it('opens the named page in a new browser tab', async () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null);

    const reply = await new MockBackend().handle({
      id: '1',
      type: 'about.openLink',
      payload: { link: 'repository' },
    });

    expect(reply.ok).toBe(true);
    expect(open).toHaveBeenCalledWith('https://github.com/carlescs/dynpost', '_blank', 'noopener');
  });

  it('rejects links it does not know', async () => {
    const open = vi.spyOn(window, 'open').mockReturnValue(null);

    const reply = await new MockBackend().handle({
      id: '1',
      type: 'about.openLink',
      payload: { link: 'toString' },
    });

    expect(reply.ok).toBe(false);
    expect(open).not.toHaveBeenCalled();
  });
});

describe('MockBackend search', () => {
  async function call(type: BridgeMessageType, payload: Record<string, unknown>) {
    const backend = new MockBackend();
    await backend.handle({ id: '1', type: 'workspace.open', payload: { path: '/demo' } });
    const result = await backend.handle({ id: '2', type, payload });
    if (!result.ok) throw new Error(result.error.message);
    return result.payload as never;
  }

  it('finds requests by name, method or URL words with typos for quick open', async () => {
    const items: QuickOpenItemDto[] = await call('workspace.quickOpen', { query: 'post /usars' });

    expect(items.map((i) => i.name)).toContain('Create user');
    expect(items.every((i) => i.method === 'POST')).toBe(true);
  });

  it('lists every request for an empty quick open query', async () => {
    const items: QuickOpenItemDto[] = await call('workspace.quickOpen', { query: '' });

    expect(items.length).toBeGreaterThan(3);
  });

  it('groups matching lines by file for a workspace search', async () => {
    const result: WorkspaceSearchDto = await call('workspace.search', { query: '/ORDERS' });

    expect(result.files.map((f) => f.path)).toEqual(['requests/orders/list-orders.yaml']);
    const line = result.files[0].lines[0];
    expect(line.text.substr(line.ranges[0].start, line.ranges[0].length)).toBe('/orders');
    expect(result.matchCount).toBe(1);

    const cased: WorkspaceSearchDto = await call('workspace.search', {
      query: '/ORDERS',
      matchCase: true,
    });
    expect(cased.files).toEqual([]);
  });

  it('forgives typos with a fuzzy search', async () => {
    const plain: WorkspaceSearchDto = await call('workspace.search', { query: '/ordars' });
    const fuzzy: WorkspaceSearchDto = await call('workspace.search', {
      query: '/ordars',
      fuzzy: true,
    });

    expect(plain.files).toEqual([]);
    expect(fuzzy.files.map((f) => f.path)).toContain('requests/orders/list-orders.yaml');
  });
});

describe('MockBackend run.compare', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('compares two finished runs and refuses a running one', async () => {
    const backend = new MockBackend();
    const call = async (type: string, payload?: unknown) => {
      const reply = backend.handle({ id: '1', type, payload } as never);
      await vi.advanceTimersByTimeAsync(1000);
      return reply;
    };
    const path = 'plans/perf/users-smoke.run.yaml';
    const start = async () => {
      const reply = await call('run.start', { path });
      if (!reply.ok) throw new Error(reply.error.message);
      return (reply.payload as { runId: string }).runId;
    };
    const first = await start();
    await vi.advanceTimersByTimeAsync(7000);
    const second = await start();

    const running = await call('run.compare', { baselineRunId: first, runId: second });
    expect(running.ok).toBe(false);

    await vi.advanceTimersByTimeAsync(7000);
    const reply = await call('run.compare', { baselineRunId: first, runId: second });
    if (!reply.ok) throw new Error(reply.error.message);
    const comparison = reply.payload as RunComparisonDto;
    expect(comparison.baseline.runId).toBe(first);
    expect(comparison.total.metrics.map((m) => m.metric)).toContain('p95');
    expect(comparison.requests).toHaveLength(2);
  });
});
