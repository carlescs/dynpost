import { TestBed } from '@angular/core/testing';
import { FolderSettingsStore } from '../../../../services/folder-settings-store';
import { InheritedVariables } from './inherited-variables';

describe('InheritedVariables', () => {
  it('lists inherited variables, strikes those set here and opens their folder', () => {
    const open = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        {
          provide: FolderSettingsStore,
          useValue: { open, nameOf: (p: string) => p.slice(p.lastIndexOf('/') + 1) },
        },
      ],
    });
    const fixture = TestBed.createComponent(InheritedVariables);
    fixture.componentRef.setInput('variables', [
      { name: 'usersUrl', value: '{{baseUrl}}/users', folder: 'requests/users' },
      { name: 'page', value: '1', folder: 'requests' },
    ]);
    fixture.componentRef.setInput('own', [
      { key: 'page', value: '2', enabled: true },
      { key: 'usersUrl', value: 'x', enabled: false },
    ]);
    fixture.detectChanges();

    const rows = [...(fixture.nativeElement as HTMLElement).querySelectorAll('tr')];
    expect(rows.map((r) => r.classList.contains('line-through'))).toEqual([false, true]);
    expect(rows[0].textContent).toContain('{{baseUrl}}/users');
    rows[0].querySelector('button')!.click();
    expect(open).toHaveBeenCalledWith('requests/users');
  });

  it('renders nothing without inherited variables', () => {
    TestBed.configureTestingModule({
      providers: [{ provide: FolderSettingsStore, useValue: { open: vi.fn() } }],
    });
    const fixture = TestBed.createComponent(InheritedVariables);
    fixture.componentRef.setInput('variables', []);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('section')).toBeNull();
  });
});
