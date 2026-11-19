import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CodeEditor } from './code-editor';

// Monaco doesn't run in jsdom: the editor is never rendered, so it works on its input value.
describe('CodeEditor', () => {
  let fixture: ComponentFixture<CodeEditor>;
  let changes: string[];

  function create(value: string, language: string, highlightVariables = false): CodeEditor {
    fixture = TestBed.createComponent(CodeEditor);
    fixture.componentRef.setInput('value', value);
    fixture.componentRef.setInput('language', language);
    fixture.componentRef.setInput('highlightVariables', highlightVariables);
    changes = [];
    fixture.componentInstance.valueChange.subscribe((v) => changes.push(v));
    return fixture.componentInstance;
  }

  it('prettifies JSON and XML, keeping {{variables}} when it highlights them', () => {
    expect(create('{"id":{{id}}}', 'json', true).prettify()).toBeNull();
    expect(changes).toEqual(['{\n  "id": {{id}}\n}']);

    expect(create('<a><b/></a>', 'xml').prettify()).toBeNull();
    expect(changes).toEqual(['<a>\n  <b/>\n</a>']);
  });

  it('says why text cannot be prettified and leaves it as it is', () => {
    expect(create('{"id":{{id}}}', 'json').prettify()).toContain('not valid JSON');
    expect(create('a: 1', 'yaml').prettify()).toContain('Only JSON and XML');
    expect(changes).toEqual([]);
  });

  it('does not emit a change when the text is already pretty', () => {
    expect(create('[\n  1\n]', 'json').prettify()).toBeNull();
    expect(changes).toEqual([]);
  });
});
