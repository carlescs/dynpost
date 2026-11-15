import { TestBed } from '@angular/core/testing';
import { Mock } from 'vitest';
import { EnvironmentDto, WorkspaceDto } from '../models/messages';
import { Bridge } from './bridge';
import { Dialogs } from './dialogs';
import { EnvironmentStore } from './environment-store';
import { WorkspaceStore } from './workspace-store';

const workspace = { name: 'api', root: '/home/me/api', nodes: [] } as unknown as WorkspaceDto;

const dev: EnvironmentDto = {
  name: 'Dev',
  variables: [{ key: 'host', value: 'localhost', secret: false, enabled: true }] as never,
};

describe('EnvironmentStore', () => {
  let send: Mock;
  let dialogs: { confirm: Mock };
  let store: EnvironmentStore;
  let onDisk: EnvironmentDto;

  beforeEach(async () => {
    onDisk = dev;
    send = vi.fn(async (type: string, payload: any) => {
      switch (type) {
        case 'workspace.openLast':
          return workspace;
        case 'workspace.recent':
          return [];
        case 'environment.list':
          return { environments: [{ id: 'dev.yaml', name: 'Dev' }], active: null };
        case 'environment.load':
          return onDisk;
        case 'environment.save':
          return { id: payload.id, environment: payload.environment };
        default:
          return null;
      }
    });
    dialogs = { confirm: vi.fn().mockResolvedValue(true) };
    TestBed.configureTestingModule({
      providers: [
        { provide: Bridge, useValue: { isDesktop: true, send } },
        { provide: Dialogs, useValue: dialogs },
      ],
    });
    store = TestBed.inject(EnvironmentStore);
    await TestBed.inject(WorkspaceStore).restore();
    TestBed.tick();
    await store.edit('dev.yaml');
  });

  const draft = () => store.drafts()[0];

  it('reloads a draft without unsaved edits silently when its file changed', async () => {
    onDisk = { ...dev, name: 'Development' };

    await store.refresh(['environments/dev.yaml']);

    expect(draft()).toMatchObject({ name: 'Development', dirty: false });
    expect(dialogs.confirm).not.toHaveBeenCalled();
  });

  it('leaves a draft whose file did not change alone, such as after its own save', async () => {
    store.update(draft().key, { name: 'Saved' });
    // The host writes the file before the save resolves.
    onDisk = { ...dev, name: 'Saved' };
    await store.save(draft().key);
    store.update(draft().key, { name: 'Typing' });

    await store.refresh(['environments/dev.yaml']);

    expect(dialogs.confirm).not.toHaveBeenCalled();
    expect(draft()).toMatchObject({ name: 'Typing', dirty: true });
  });

  it('asks before replacing unsaved edits, and keeps them when the user chooses to', async () => {
    dialogs.confirm.mockResolvedValue(false);
    store.update(draft().key, { name: 'Mine' });
    onDisk = { ...dev, name: 'Theirs' };

    await store.refresh(['environments']);
    await store.refresh(['environments/dev.yaml']);

    expect(dialogs.confirm).toHaveBeenCalledTimes(1);
    expect(draft()).toMatchObject({ name: 'Mine', dirty: true });
  });

  it('reloads a draft with unsaved edits when the user chooses to', async () => {
    store.update(draft().key, { name: 'Mine' });
    onDisk = { ...dev, name: 'Theirs' };

    await store.refresh(['environments/dev.yaml']);

    expect(draft()).toMatchObject({ name: 'Theirs', dirty: false });
  });
});
