import { ComponentFixture, TestBed } from '@angular/core/testing';
import { KeyValue } from '../../models/workspace';
import { KeyValueTable } from './key-value-table';

describe('KeyValueTable', () => {
  let fixture: ComponentFixture<KeyValueTable>;
  let emitted: KeyValue[][];
  const rows: KeyValue[] = [{ key: 'page', value: '1', enabled: true }];

  beforeEach(async () => {
    fixture = TestBed.createComponent(KeyValueTable);
    fixture.componentRef.setInput('rows', rows);
    fixture.componentRef.setInput('keyLabel', 'Header');
    emitted = [];
    fixture.componentInstance.rowsChange.subscribe((r) => emitted.push(r));
    await fixture.whenStable();
  });

  const element = () => fixture.nativeElement as HTMLElement;
  const textBoxes = () =>
    Array.from(element().querySelectorAll<HTMLInputElement>('input:not([type=checkbox])'));
  const type = (input: HTMLInputElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };

  it('shows the rows plus one blank row for new entries', () => {
    expect(element().querySelectorAll('tbody tr')).toHaveLength(2);
    expect(textBoxes().map((i) => i.value)).toEqual(['page', '1', '', '']);
    expect(textBoxes()[2].placeholder).toBe('Header');
    expect(element().querySelectorAll('[aria-label="Remove row"]')).toHaveLength(1);
  });

  it('adds a row when typing in the blank one', () => {
    type(textBoxes()[2], 'limit');
    expect(emitted).toEqual([[...rows, { key: 'limit', value: '', enabled: true }]]);
  });

  it('edits, disables and removes rows without changing the input', () => {
    type(textBoxes()[1], '2');
    const checkbox = element().querySelector<HTMLInputElement>('input[type=checkbox]')!;
    checkbox.checked = false;
    checkbox.dispatchEvent(new Event('change'));
    element().querySelector<HTMLButtonElement>('[aria-label="Remove row"]')!.click();

    expect(emitted).toEqual([
      [{ key: 'page', value: '2', enabled: true }],
      [{ key: 'page', value: '1', enabled: false }],
      [],
    ]);
    expect(rows).toEqual([{ key: 'page', value: '1', enabled: true }]);
  });
});
