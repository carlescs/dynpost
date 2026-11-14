import { TestBed } from '@angular/core/testing';
import { InheritedSettingsDto } from '../../../../models/messages';
import { EMPTY_AUTH } from '../../../../models/request-mapping';
import { RequestAuth } from '../../../../models/workspace';
import { FolderSettingsStore } from '../../../../services/folder-settings-store';
import { AuthEditor } from './auth-editor';

describe('AuthEditor', () => {
  let open: ReturnType<typeof vi.fn>;

  function render(
    auth: RequestAuth,
    inherited: InheritedSettingsDto | null,
    subject: 'request' | 'folder' = 'request',
  ) {
    open = vi.fn();
    TestBed.configureTestingModule({
      providers: [{ provide: FolderSettingsStore, useValue: { open } }],
    });
    const fixture = TestBed.createComponent(AuthEditor);
    fixture.componentRef.setInput('auth', auth);
    fixture.componentRef.setInput('inherited', inherited);
    fixture.componentRef.setInput('subject', subject);
    fixture.detectChanges();
    return { fixture, text: () => (fixture.nativeElement as HTMLElement).textContent ?? '' };
  }

  it('shows the inherited auth and the folder it comes from', () => {
    const { fixture, text } = render(EMPTY_AUTH, {
      headers: [],
      auth: { type: 'bearer', token: '{{token}}' },
      authFolder: 'requests/users',
      variables: [],
    });

    expect(text()).toContain('Uses Bearer token auth from');
    const link = (fixture.nativeElement as HTMLElement).querySelector(
      'button[title="Open the folder\'s settings"]',
    ) as HTMLButtonElement;
    expect(link.textContent).toContain('requests/users');
    link.click();
    expect(open).toHaveBeenCalledWith('requests/users');
  });

  it('says when no folder sets auth', () => {
    const { text } = render(EMPTY_AUTH, {
      headers: [],
      auth: { type: 'none' },
      authFolder: null,
      variables: [],
    });
    expect(text()).toContain('No folder above sets auth');
  });

  it('explains what no auth means for a folder', () => {
    const { text } = render({ ...EMPTY_AUTH, type: 'none' }, null, 'folder');
    expect(text()).toContain('sent without authorization, whatever the folders above set');
  });

  it('emits changes to the type and fields', () => {
    const { fixture } = render({ ...EMPTY_AUTH, type: 'bearer' }, null);
    const changes: Partial<RequestAuth>[] = [];
    fixture.componentInstance.authChange.subscribe((c) => changes.push(c));
    const host = fixture.nativeElement as HTMLElement;

    const token = host.querySelector('#auth-token') as HTMLInputElement;
    token.value = 'abc';
    token.dispatchEvent(new Event('input'));
    const select = host.querySelector('select') as HTMLSelectElement;
    select.value = 'inherit';
    select.dispatchEvent(new Event('change'));

    expect(changes).toEqual([{ token: 'abc' }, { type: 'inherit' }]);
    expect([...select.options].map((o) => o.textContent?.trim())[0]).toBe('Inherit from folder');
  });
});
