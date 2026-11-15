import { TestBed } from '@angular/core/testing';
import { Mock } from 'vitest';
import { WorkspaceDto } from '../models/messages';
import { Bridge } from './bridge';
import { InterchangeStore } from './interchange-store';
import { WorkspaceStore } from './workspace-store';

const workspace = { name: 'api', root: '/home/me/api', nodes: [] } as unknown as WorkspaceDto;

describe('InterchangeStore cURL', () => {
  let send: Mock;
  let store: InterchangeStore;
  let workspaceStore: WorkspaceStore;

  function respond(handlers: Record<string, (payload: any) => unknown>): void {
    send.mockImplementation(async (type: string, payload: unknown) => {
      const handler = handlers[type];
      if (!handler) throw new Error(`Unexpected message ${type}`);
      return handler(payload);
    });
  }

  beforeEach(async () => {
    send = vi.fn();
    TestBed.configureTestingModule({
      providers: [{ provide: Bridge, useValue: { isDesktop: true, send, on: vi.fn() } }],
    });
    store = TestBed.inject(InterchangeStore);
    workspaceStore = TestBed.inject(WorkspaceStore);
    respond({
      'workspace.openLast': () => workspace,
      'workspace.recent': () => [],
      'environment.list': () => ({ active: null, environments: [] }),
      'globals.load': () => ({ variables: [] }),
    });
    await workspaceStore.restore();
    TestBed.tick();
  });

  it('opens a new request filled in from a cURL command, listing what was skipped', async () => {
    respond({
      'request.parseCurl': ({ command }) => {
        expect(command).toBe('curl -X POST https://api.test/users');
        return {
          request: {
            name: 'POST /users',
            method: 'POST',
            url: 'https://api.test/users',
            params: [],
            headers: [{ key: 'Accept', value: '*/*', enabled: true }],
            auth: { type: 'bearer', token: 't' },
            body: { type: 'json', content: '{}' },
          },
          warnings: ['Skipped --proxy'],
        };
      },
    });

    expect(await store.newRequestFromCurl('curl -X POST https://api.test/users')).toBe(true);

    const doc = workspaceStore.documents().at(-1)!;
    expect(doc.method).toBe('POST');
    expect(doc.url).toBe('https://api.test/users');
    expect(doc.auth.type).toBe('bearer');
    expect(doc.bodyType).toBe('json');
    expect(doc.name).toMatch(/New request/);
    expect(store.notice()?.warnings).toEqual(['Skipped --proxy']);
  });

  it('copies the request as a cURL command for the chosen shell', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    workspaceStore.newRequest();
    const doc = workspaceStore.documents().at(-1)!;
    respond({ 'request.copyAsCurl': () => 'curl https://api.test' });
    store.curlShell.set('cmd');

    expect(await store.copyAsCurl(doc, { resolve: false, includeSecrets: false })).toBe(true);

    expect(send).toHaveBeenCalledWith(
      'request.copyAsCurl',
      expect.objectContaining({ resolve: false, includeSecrets: false, shell: 'cmd' }),
    );
    expect(writeText).toHaveBeenCalledWith('curl https://api.test');
    expect(store.notice()?.message).toContain('as written');
  });
});
