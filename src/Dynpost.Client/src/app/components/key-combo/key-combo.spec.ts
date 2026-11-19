import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { KeyCombo } from '../../models/shortcuts';
import { KeyComboView } from './key-combo';

@Component({
  imports: [KeyComboView],
  template: `<app-key-combo [combo]="combo" [mac]="mac" />`,
})
class Host {
  combo: KeyCombo = { key: 'Tab', mod: true, shift: true };
  mac = false;
}

function render(combo: KeyCombo, mac: boolean): HTMLElement {
  const fixture = TestBed.createComponent(Host);
  fixture.componentInstance.combo = combo;
  fixture.componentInstance.mac = mac;
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

function caps(element: HTMLElement): string[] {
  return [...element.querySelectorAll('kbd kbd')].map((key) => key.textContent!.trim());
}

describe('KeyComboView', () => {
  it('draws one keycap per key, joined by plus signs on Windows and Linux', () => {
    const element = render({ key: 'Tab', mod: true, shift: true }, false);

    expect(caps(element)).toEqual(['Ctrl', 'Shift', 'Tab']);
    expect(element.textContent!.replace(/\s/g, '')).toBe('Ctrl+Shift+Tab');
    expect(element.querySelector('kbd')!.getAttribute('aria-label')).toBe('Ctrl+Shift+Tab');
  });

  it('uses the macOS symbols without plus signs', () => {
    const element = render({ key: 'PageDown', mod: true }, true);

    expect(caps(element)).toEqual(['⌘', 'PgDn']);
    expect(element.textContent).not.toContain('+');
  });
});
