import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { TestBed } from '@angular/core/testing';
import { RequestDto } from '../../../../models/messages';
import { Bridge } from '../../../../services/bridge';
import { RequestFileData, RequestFileDialog } from './request-file-dialog';

const REQUEST: RequestDto = {
  name: 'Get user',
  method: 'GET',
  url: '/users/1',
  params: [],
  headers: [],
  auth: { type: 'inherit' },
  body: { type: 'none', content: '' },
};

describe('RequestFileDialog', () => {
  async function create(data: RequestFileData, send: ReturnType<typeof vi.fn>) {
    TestBed.configureTestingModule({
      imports: [RequestFileDialog],
      providers: [
        { provide: Bridge, useValue: { send } },
        { provide: DialogRef, useValue: { close: vi.fn() } },
        { provide: DIALOG_DATA, useValue: data },
      ],
    });
    const fixture = TestBed.createComponent(RequestFileDialog);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('shows the file of a saved request', async () => {
    const send = vi.fn().mockResolvedValue('name: Get user\n');
    const path = 'requests/get-user.yaml';
    const fixture = await create(
      { name: 'Get user', request: REQUEST, path, location: path },
      send,
    );

    expect(send).toHaveBeenCalledWith('request.viewFile', { request: REQUEST, path });
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('name: Get user');
    expect(text).toContain('requests/get-user.yaml');
    expect(text).not.toContain('unsaved changes');
  });

  it('says when it shows unsaved changes', async () => {
    const send = vi.fn().mockResolvedValue('name: Get user\n');
    const fixture = await create(
      { name: 'Get user', request: REQUEST, path: null, location: 'requests/get-user.yaml' },
      send,
    );

    expect(send).toHaveBeenCalledWith('request.viewFile', { request: REQUEST, path: null });
    expect(fixture.nativeElement.textContent).toContain('(unsaved changes)');
  });

  it('shows why the file could not be read', async () => {
    const send = vi.fn().mockRejectedValue(new Error('The request file does not exist.'));
    const fixture = await create(
      {
        name: 'Gone',
        request: REQUEST,
        path: 'requests/gone.yaml',
        location: 'requests/gone.yaml',
      },
      send,
    );

    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'does not exist',
    );
  });
});
