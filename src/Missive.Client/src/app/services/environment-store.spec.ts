import { TestBed } from '@angular/core/testing';
import { Mock } from 'vitest';
import { EnvironmentDto } from '../models/messages';
import { Bridge, BridgeError } from './bridge';
import { EnvironmentStore } from './environment-store';
import { WorkspaceStore } from './workspace-store';

describe('EnvironmentStore', () => {
  let send: Mock;
  let store: EnvironmentStore;
  let files: Map<string, EnvironmentDto>;
  let listError: BridgeError | null;

  beforeEach(async () => {
    files = new Map([
      [
        'dev.yaml',
        {
          name: 'Development',
          variables: [{ name: 'baseUrl', value: 'http://localhost', secret: false }],
        },
      ],
    ]);
    listError = null;
    send = vi.fn(async (type: string, payload: any) => {
      switch (type) {
        case 'environment.list':
          if (listError) throw listError;
          return {
            active: 'dev.yaml',
            environments: [...files].map(([id, env]) => ({ id, name: env.name })),
          };
        case 'environment.load':
          return structuredClone(files.get(payload.id));
        case 'environment.save':
          files.set(payload.id, structuredClone(payload.environment));
          return { id: payload.id, environment: structuredClone(payload.environment) };
        default:
          throw new Error(`Unexpected message ${type}`);
      }
    });
    TestBed.configureTestingModule({
      providers: [{ provide: Bridge, useValue: { isDesktop: true, send } }],
    });
    store = TestBed.inject(EnvironmentStore);
    TestBed.inject(WorkspaceStore).workspace.set({ name: 'api', root: '/home/me/api' });
    TestBed.tick();
    await settle();
  });

  /** Lets pending bridge calls and their follow-ups finish. */
  async function settle(): Promise<void> {
    for (let i = 0; i < 10; i++) await Promise.resolve();
  }

  /** Changes the environment file as another tool would. */
  function editOnDisk(value: string): void {
    files.set('dev.yaml', {
      name: 'Development',
      variables: [{ name: 'baseUrl', value, secret: false }],
    });
  }

  it('loads the selected environment when a workspace opens', () => {
    expect(store.activeId()).toBe('dev.yaml');
    expect(store.variables().get('baseUrl')?.value).toBe('http://localhost');
  });

  it('reloads the selected variables after saving the selected environment', async () => {
    await store.edit('dev.yaml');
    const key = store.drafts()[0].key;
    store.update(key, { variables: [{ name: 'baseUrl', value: 'https://saved', secret: false }] });

    await store.save(key);

    expect(store.drafts()[0].dirty).toBe(false);
    expect(store.variables().get('baseUrl')?.value).toBe('https://saved');
  });

  it('reloads the selected variables and clean tabs when the window regains focus', async () => {
    await store.edit('dev.yaml');
    editOnDisk('https://api.example.com');

    window.dispatchEvent(new Event('focus'));
    await settle();

    expect(store.variables().get('baseUrl')?.value).toBe('https://api.example.com');
    expect(store.drafts()[0].variables[0].value).toBe('https://api.example.com');
  });

  it('keeps unsaved edits in an open tab when the window regains focus', async () => {
    await store.edit('dev.yaml');
    const key = store.drafts()[0].key;
    store.update(key, { variables: [{ name: 'baseUrl', value: 'typed', secret: false }] });
    editOnDisk('https://api.example.com');

    window.dispatchEvent(new Event('focus'));
    await settle();

    expect(store.drafts()[0].variables[0].value).toBe('typed');
    expect(store.variables().get('baseUrl')?.value).toBe('https://api.example.com');
  });

  it("doesn't report failures of the refresh on focus", async () => {
    listError = new BridgeError('io', 'The environments folder can’t be read.');

    window.dispatchEvent(new Event('focus'));
    await settle();

    expect(TestBed.inject(WorkspaceStore).error()).toBeNull();
  });
});
