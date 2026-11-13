import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NameInput } from './name-input';

describe('NameInput', () => {
  let fixture: ComponentFixture<NameInput>;
  let committed: { name: string; viaBlur: boolean }[];
  let cancelled: number;

  beforeEach(async () => {
    fixture = TestBed.createComponent(NameInput);
    fixture.componentRef.setInput('label', 'Folder name');
    fixture.componentRef.setInput('initial', 'users');
    committed = [];
    cancelled = 0;
    fixture.componentInstance.committed.subscribe((e) => committed.push(e));
    fixture.componentInstance.cancelled.subscribe(() => cancelled++);
    await fixture.whenStable();
  });

  const box = () => (fixture.nativeElement as HTMLElement).querySelector('input')!;
  const key = (key: string) => box().dispatchEvent(new KeyboardEvent('keydown', { key }));

  it('shows the initial name, focused', () => {
    expect(box().value).toBe('users');
    expect(box().getAttribute('aria-label')).toBe('Folder name');
    expect(document.activeElement).toBe(box());
  });

  it('commits the trimmed name on Enter', () => {
    box().value = '  people ';
    key('Enter');
    expect(committed).toEqual([{ name: 'people', viaBlur: false }]);
  });

  it('cancels on Escape and on an empty name', () => {
    key('Escape');
    box().value = '   ';
    key('Enter');
    expect(cancelled).toBe(2);
    expect(committed).toEqual([]);
  });

  it('commits a changed name on blur, but cancels an unchanged one', () => {
    box().dispatchEvent(new Event('blur'));
    expect(cancelled).toBe(1);
    box().value = 'people';
    box().dispatchEvent(new Event('blur'));
    expect(committed).toEqual([{ name: 'people', viaBlur: true }]);
  });

  it('ignores input while busy', async () => {
    fixture.componentRef.setInput('busy', true);
    await fixture.whenStable();
    expect(box().disabled).toBe(true);
    box().value = 'people';
    key('Enter');
    expect(committed).toEqual([]);
  });
});
