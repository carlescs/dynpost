import { TestBed } from '@angular/core/testing';
import { RunResultDto } from '../../../../models/messages';
import { Bridge } from '../../../../services/bridge';
import { RunPlanDraft, RunStore } from '../../../../services/run-store';
import { RunEditor } from './run-editor';

describe('RunEditor', () => {
  function create(run: RunResultDto | null = null) {
    const draft: RunPlanDraft = {
      key: 'run:a.run.yaml',
      path: 'a.run.yaml',
      yaml: 'name: A\n',
      savedYaml: 'name: A\n',
      error: null,
      run,
      history: [],
      saving: false,
      starting: false,
      confirmation: null,
      failure: null,
    };
    TestBed.configureTestingModule({
      providers: [
        { provide: RunStore, useValue: {} },
        { provide: Bridge, useValue: { send: vi.fn().mockReturnValue(new Promise(() => {})) } },
      ],
    });
    const fixture = TestBed.createComponent(RunEditor);
    fixture.componentRef.setInput('draft', draft);
    fixture.detectChanges();
    return fixture;
  }
  const zones = (element: HTMLElement) =>
    [...element.querySelectorAll('[data-zone]')].map((zone) => zone.getAttribute('data-zone'));

  it('maximizes either zone to the full width and restores it', () => {
    const fixture = create();
    const element = fixture.nativeElement as HTMLElement;
    expect(zones(element)).toEqual(['plan', 'results']);

    (element.querySelector('[data-maximize="results"]') as HTMLElement).click();
    fixture.detectChanges();
    expect(zones(element)).toEqual(['results']);
    expect(element.querySelector('[data-maximize="results"]')!.getAttribute('title')).toBe(
      'Restore',
    );

    (element.querySelector('[data-maximize="results"]') as HTMLElement).click();
    fixture.detectChanges();
    expect(zones(element)).toEqual(['plan', 'results']);

    (element.querySelector('[data-maximize="plan"]') as HTMLElement).click();
    fixture.detectChanges();
    expect(zones(element)).toEqual(['plan']);
  });

  it('switches the results between the dashboard and the requests table', () => {
    const fixture = create();
    const element = fixture.nativeElement as HTMLElement;
    const tab = (view: string) => element.querySelector(`[data-view="${view}"]`)!;
    expect(tab('dashboard').getAttribute('aria-selected')).toBe('true');

    (tab('requests') as HTMLElement).click();
    fixture.detectChanges();

    expect(tab('requests').getAttribute('aria-selected')).toBe('true');
    expect(tab('dashboard').getAttribute('aria-selected')).toBe('false');
  });
});
