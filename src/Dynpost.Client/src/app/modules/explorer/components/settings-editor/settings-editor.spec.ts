import { TestBed } from '@angular/core/testing';
import { RequestSettingsDto } from '../../../../models/messages';
import { SettingsEditor, describeTimeout, settingsCount } from './settings-editor';

describe('SettingsEditor', () => {
  function create(settings: RequestSettingsDto, inherited: RequestSettingsDto | null = null) {
    const fixture = TestBed.createComponent(SettingsEditor);
    fixture.componentRef.setInput('settings', settings);
    fixture.componentRef.setInput('inherited', inherited);
    const changes: RequestSettingsDto[] = [];
    fixture.componentInstance.settingsChange.subscribe((s) => changes.push(s));
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement, changes };
  }

  it('shows the inherited values and the defaults next to empty settings', () => {
    const { element } = create({}, { timeoutMs: 5000, verifyTls: false });

    expect(element.textContent).toContain('Inherited: 5 s');
    expect(element.textContent).toContain('Default: 20');
    expect(element.textContent).toContain('Certificates are not checked');
  });

  it('turns typed seconds into milliseconds and an emptied field into inherit', () => {
    const { element, changes, fixture } = create({ maxRedirects: 3 });
    const timeout = element.querySelector<HTMLInputElement>('#setting-timeout')!;

    timeout.value = '1.5';
    timeout.dispatchEvent(new Event('change'));
    expect(changes.at(-1)).toEqual({ maxRedirects: 3, timeoutMs: 1500 });

    fixture.componentRef.setInput('settings', { timeoutMs: 1500 });
    fixture.detectChanges();
    timeout.value = '';
    timeout.dispatchEvent(new Event('change'));
    expect(changes.at(-1)).toEqual({});
  });

  it('sets and clears yes/no settings', () => {
    const { element, changes } = create({});
    const follow = element.querySelector<HTMLSelectElement>('#setting-follow')!;

    follow.value = 'false';
    follow.dispatchEvent(new Event('change'));
    expect(changes.at(-1)).toEqual({ followRedirects: false });
  });

  it('counts and describes settings', () => {
    expect(settingsCount({ timeoutMs: 0, verifyTls: null })).toBe(1);
    expect(describeTimeout(0)).toBe('no timeout');
    expect(describeTimeout(500)).toBe('500 ms');
    expect(describeTimeout(60_000)).toBe('60 s');
  });
});
