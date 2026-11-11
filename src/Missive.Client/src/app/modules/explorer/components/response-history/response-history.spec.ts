import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CodeDiffEditor } from '../../../../components/code-diff-editor/code-diff-editor';
import { CodeEditor } from '../../../../components/code-editor/code-editor';
import { HistoryEntryDto, ResponseDto } from '../../../../models/messages';
import { Bridge } from '../../../../services/bridge';
import { Dialogs } from '../../../../services/dialogs';
import { ResponseHistory } from './response-history';

const response = (body: string, historyId: string | null = null): ResponseDto => ({
  status: 200,
  statusText: 'OK',
  headers: [{ key: 'Content-Type', value: 'application/json' }],
  contentType: 'application/json',
  body,
  bodyEncoding: 'text',
  size: body.length,
  truncated: false,
  durationMs: 12,
  historyId,
});

const entry = (id: string, status = 200): HistoryEntryDto => ({
  id,
  sentAt: '2026-11-07T12:00:00Z',
  method: 'GET',
  url: 'https://api.test/users',
  status,
  statusText: 'OK',
  durationMs: 12,
  size: 7,
});

/** Stands in for the editors, which would load Monaco after the test is over. */
@Component({ selector: 'app-code-editor', template: '' })
class FakeCodeEditor {
  readonly value = input('');
  readonly language = input('');
  readonly readOnly = input(false);
  readonly wordWrap = input(false);
  readonly ariaLabel = input('');
}

@Component({ selector: 'app-code-diff-editor', template: '' })
class FakeCodeDiffEditor {
  readonly original = input('');
  readonly modified = input('');
  readonly language = input('');
  readonly wordWrap = input(false);
  readonly ariaLabel = input('');
}

describe('ResponseHistory', () => {
  function setup(entries: HistoryEntryDto[], current: ResponseDto | null) {
    const bodies: Record<string, string> = { new: '{"v":2}', old: '{"v":1}' };
    const send = vi.fn(async (type: string, payload: { id?: string }) => {
      if (type === 'history.list') return entries;
      if (type === 'history.get') {
        return { entry: entry(payload.id!), response: response(bodies[payload.id!]) };
      }
      if (type === 'history.delete')
        return payload.id ? entries.filter((e) => e.id !== payload.id) : [];
      throw new Error(type);
    });
    const confirm = vi.fn().mockResolvedValue(true);
    TestBed.configureTestingModule({
      imports: [ResponseHistory],
      providers: [
        { provide: Bridge, useValue: { isDesktop: true, send } },
        { provide: Dialogs, useValue: { confirm } },
      ],
    });
    TestBed.overrideComponent(ResponseHistory, {
      remove: { imports: [CodeEditor, CodeDiffEditor] },
      add: { imports: [FakeCodeEditor, FakeCodeDiffEditor] },
    });
    const fixture = TestBed.createComponent(ResponseHistory);
    fixture.componentRef.setInput('path', 'users/get-user.yaml');
    fixture.componentRef.setInput('current', current);
    return { fixture, send, confirm };
  }

  /** Lets the chained bridge calls (list, then get) finish and renders. */
  async function settle(fixture: ReturnType<typeof setup>['fixture']) {
    for (let i = 0; i < 5; i++) {
      fixture.detectChanges();
      await new Promise((resolve) => setTimeout(resolve));
    }
    fixture.detectChanges();
  }

  it('lists the kept responses and compares the newest other one with the current response', async () => {
    const { fixture, send } = setup([entry('new'), entry('old')], response('{"v":2}', 'new'));
    await settle(fixture);
    const root: HTMLElement = fixture.nativeElement;

    expect(send).toHaveBeenCalledWith('history.list', { path: 'users/get-user.yaml' });
    expect(send).toHaveBeenCalledWith('history.get', { path: 'users/get-user.yaml', id: 'old' });
    expect(root.querySelectorAll('li')).toHaveLength(2);
    expect(root.querySelector('li')?.textContent).toContain('current');
    expect(root.querySelector('app-code-diff-editor')).not.toBeNull();
  });

  it('shows the selected response alone when there is nothing to compare with', async () => {
    const { fixture } = setup([entry('old')], null);
    await settle(fixture);
    const root: HTMLElement = fixture.nativeElement;

    expect(root.querySelector('app-code-diff-editor')).toBeNull();
    expect(root.querySelector('app-code-editor')).not.toBeNull();
    expect(root.querySelector<HTMLInputElement>('input[type="checkbox"]')!.disabled).toBe(true);
  });

  it('deletes one response or, once confirmed, all of them', async () => {
    const { fixture, send, confirm } = setup([entry('new'), entry('old')], null);
    await settle(fixture);
    const root: HTMLElement = fixture.nativeElement;

    root.querySelector<HTMLButtonElement>('button[aria-label^="Delete the response"]')!.click();
    await settle(fixture);
    expect(send).toHaveBeenCalledWith('history.delete', { path: 'users/get-user.yaml', id: 'new' });
    expect(root.querySelectorAll('li')).toHaveLength(1);

    [...root.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Clear')!.click();
    await settle(fixture);
    expect(confirm).toHaveBeenCalled();
    expect(send).toHaveBeenLastCalledWith('history.delete', { path: 'users/get-user.yaml' });
    expect(root.textContent).toContain('No responses are kept for this request yet.');
  });
});
