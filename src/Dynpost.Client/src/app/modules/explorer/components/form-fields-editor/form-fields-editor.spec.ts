import { Component, input, output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CdkDragDrop } from '@angular/cdk/drag-drop';
import { emptyField } from '../../../../models/form-fields';
import { FormField, KeyValue } from '../../../../models/workspace';
import { Bridge } from '../../../../services/bridge';
import { CodeEditor } from '../../../../components/code-editor/code-editor';
import { VariableLookup } from '../../../../models/variables';
import { FormFieldsEditor } from './form-fields-editor';

/** Stands in for the code editor, which would load Monaco after the test is over. */
@Component({ selector: 'app-code-editor', template: '' })
class FakeCodeEditor {
  readonly value = input('');
  readonly language = input('');
  readonly ariaLabel = input('');
  readonly highlightVariables = input(false);
  readonly variables = input<VariableLookup | null>(null);
  readonly valueChange = output<string>();
  readonly submit = output<void>();
}

describe('FormFieldsEditor', () => {
  let fixture: ComponentFixture<FormFieldsEditor>;
  let send: ReturnType<typeof vi.fn>;
  let emitted: FormField[][];

  const text = emptyField({ name: 'title', value: 'Report' });
  const file = emptyField({ name: 'file', value: 'docs/a.pdf', kind: 'file' });

  async function render(
    fields: FormField[],
    type: 'form' | 'urlencoded' = 'form',
    headers: KeyValue[] = [],
  ) {
    TestBed.configureTestingModule({ providers: [{ provide: Bridge, useValue: { send } }] });
    TestBed.overrideComponent(FormFieldsEditor, {
      remove: { imports: [CodeEditor] },
      add: { imports: [FakeCodeEditor] },
    });
    fixture = TestBed.createComponent(FormFieldsEditor);
    fixture.componentRef.setInput('fields', fields);
    fixture.componentRef.setInput('type', type);
    fixture.componentRef.setInput('headers', headers);
    emitted = [];
    fixture.componentInstance.fieldsChange.subscribe((f) => emitted.push(f));
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(400);
    fixture.detectChanges();
  }

  const element = () => fixture.nativeElement as HTMLElement;
  const rows = () => [...element().querySelectorAll<HTMLElement>('[data-field-row]')];
  const button = (label: string) =>
    [...element().querySelectorAll('button')].find((b) => b.textContent?.trim() === label)!;
  const typeIn = (input: HTMLInputElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };

  beforeEach(() => {
    vi.useFakeTimers();
    send = vi.fn(async (type: string, payload: { paths?: string[] }) => {
      if (type === 'request.inspectFiles') {
        return (payload.paths ?? []).map((path) => ({
          path,
          isAbsolute: false,
          exists: path !== 'missing.png',
          size: path === 'missing.png' ? null : 2048,
        }));
      }
      if (type === 'request.pickFiles') {
        return [
          { path: 'img/a.png', isAbsolute: false },
          { path: 'img/b.png', isAbsolute: false },
        ];
      }
      return null;
    });
  });
  afterEach(() => vi.useRealTimers());

  it('shows the fields plus a blank row, and adds a field when typing in it', async () => {
    await render([text]);
    expect(rows()).toHaveLength(2);
    const name = rows()[1].querySelector<HTMLInputElement>('[aria-label="Field name"]')!;
    typeIn(name, 'note');
    expect(emitted).toEqual([[text, emptyField({ name: 'note' })]]);
  });

  it('switches a field between text and file', async () => {
    await render([text]);
    const kind = rows()[0].querySelector<HTMLSelectElement>('[aria-label="Field kind"]')!;
    kind.value = 'file';
    kind.dispatchEvent(new Event('change'));
    expect(emitted[0][0]).toMatchObject({ name: 'title', kind: 'file' });
  });

  it('shows the size of file fields and warns about missing ones', async () => {
    await render([file, emptyField({ name: 'img', value: 'missing.png', kind: 'file' })]);
    expect(send).toHaveBeenCalledWith('request.inspectFiles', {
      paths: ['docs/a.pdf', 'missing.png'],
    });
    const status = [...element().querySelectorAll('[data-file-status]')].map((s) =>
      s.textContent?.trim(),
    );
    expect(status[0]).toBe('a.pdf · 2.0 KB');
    expect(status[1]).toContain('missing.png was not found.');
  });

  it('puts picked files in the row and adds the others under the same name', async () => {
    await render([file, text]);
    rows()[0]
      .querySelector<HTMLButtonElement>('button[title^="Choose one or more files"]')!
      .click();
    await vi.advanceTimersByTimeAsync(0);
    expect(send).toHaveBeenCalledWith('request.pickFiles', { multiple: true });
    expect(emitted[0]).toEqual([
      { ...file, value: 'img/a.png' },
      emptyField({ name: 'file', kind: 'file', value: 'img/b.png' }),
      text,
    ]);
  });

  it('reorders fields by dragging', async () => {
    await render([text, file]);
    const drop = (fixture.componentInstance as unknown as { drop(e: unknown): void }).drop;
    drop.call(fixture.componentInstance, {
      previousIndex: 1,
      currentIndex: 0,
    } as CdkDragDrop<unknown>);
    expect(emitted).toEqual([[file, text]]);
  });

  it('removes and disables fields without changing the input', async () => {
    const fields = [text, file];
    await render(fields);
    const checkbox = rows()[0].querySelector<HTMLInputElement>('input[type=checkbox]')!;
    checkbox.checked = false;
    checkbox.dispatchEvent(new Event('change'));
    rows()[1].querySelector<HTMLButtonElement>('[aria-label="Remove field"]')!.click();
    expect(emitted).toEqual([[{ ...text, enabled: false }, file], [text]]);
    expect(fields).toEqual([text, file]);
  });

  it('switches to bulk edit and back, emitting the parsed fields', async () => {
    await render([text, file]);
    button('Bulk edit').click();
    fixture.detectChanges();
    expect(element().querySelector('app-code-editor')).not.toBeNull();
    expect(rows()).toHaveLength(0);

    const editor = fixture.componentInstance as unknown as {
      bulkText(): string | null;
      bulkChanged(text: string): void;
    };
    expect(editor.bulkText()).toBe('title=Report\nfile=@docs/a.pdf');
    editor.bulkChanged('title=Report\n~file=@docs/b.pdf;filename=c.pdf');
    expect(emitted).toEqual([
      [
        text,
        emptyField({
          name: 'file',
          value: 'docs/b.pdf',
          kind: 'file',
          enabled: false,
          fileName: 'c.pdf',
        }),
      ],
    ]);

    button('Table').click();
    fixture.detectChanges();
    expect(rows().length).toBeGreaterThan(0);
  });

  it('disables bulk edit when the text could not hold the fields', async () => {
    await render([emptyField({ name: 'note', value: 'line 1\nline 2' })]);
    expect(button('Bulk edit').disabled).toBe(true);
    expect(rows()[0].querySelector('textarea')?.value).toBe('line 1\nline 2');
  });

  it('shows the generated multipart Content-Type and warns about a conflicting header', async () => {
    await render([text]);
    const hint = () => element().querySelector('[data-content-type-hint]')!.textContent!.trim();
    expect(hint()).toContain('multipart/form-data; boundary=…');

    fixture.componentRef.setInput('headers', [
      { key: 'Content-Type', value: 'application/json', enabled: true },
    ]);
    fixture.detectChanges();
    expect(hint()).toContain('The Content-Type header (application/json) is replaced');
  });

  it('hides file columns and offers encoding for urlencoded bodies', async () => {
    await render([emptyField({ name: 'q', value: 'a b' })], 'urlencoded');
    expect(element().querySelector('[aria-label="Field kind"]')).toBeNull();
    expect(element().querySelector('[aria-label="Content type"]')).toBeNull();
    const encode = rows()[0].querySelector<HTMLInputElement>('[aria-label="Percent-encode"]')!;
    encode.checked = true;
    encode.dispatchEvent(new Event('change'));
    expect(emitted).toEqual([[emptyField({ name: 'q', value: 'a b', encode: true })]]);
  });
});
