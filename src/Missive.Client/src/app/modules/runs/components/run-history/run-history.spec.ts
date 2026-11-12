import { TestBed } from '@angular/core/testing';
import { RunSummaryDto } from '../../../../models/messages';
import { RunHistory } from './run-history';
import { testDateTime, useTestDateFormat } from '../../../../testing/date-format';

const summary = (patch: Partial<RunSummaryDto>): RunSummaryDto => ({
  runId: 'r1',
  planPath: 'a.run.yaml',
  planName: 'Smoke',
  state: 'completed',
  startedAt: '2026-01-02T03:04:05Z',
  elapsedMs: 6000,
  passed: true,
  samples: 240,
  errors: 0,
  errorPercent: 0,
  p95: 100,
  throughput: 40,
  ...patch,
});

describe('RunHistory', () => {
  function create() {
    const fixture = TestBed.createComponent(RunHistory);
    fixture.componentRef.setInput('runs', [
      summary({ runId: 'r2', passed: false, p95: 400, errorPercent: 2.5 }),
      summary({ runId: 'r1' }),
      summary({ runId: 'r0', state: 'cancelled', passed: null }),
    ]);
    fixture.componentRef.setInput('selected', 'r1');
    const selected: string[] = [];
    const removed: string[][] = [];
    let cleared = 0;
    fixture.componentInstance.select.subscribe((id) => selected.push(id));
    fixture.componentInstance.remove.subscribe((id) => removed.push(id));
    fixture.componentInstance.clear.subscribe(() => cleared++);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const buttons = [...element.querySelectorAll<HTMLElement>('button[data-select]')];
    const deletes = [...element.querySelectorAll<HTMLElement>('button[data-delete]')];
    const clear = element.querySelector<HTMLElement>('button[data-clear]')!;
    const checks = [...element.querySelectorAll<HTMLInputElement>('input[data-check]')];
    const checkAll = element.querySelector<HTMLInputElement>('input[data-check-all]')!;
    const query = (selector: string) => element.querySelector<HTMLElement>(selector);
    const header = () => element.querySelector('h3')!.textContent!.trim();
    return {
      fixture,
      buttons,
      deletes,
      clear,
      checks,
      checkAll,
      query,
      header,
      selected,
      removed,
      cleared: () => cleared,
    };
  }

  it('says how each run went', () => {
    const { buttons } = create();
    const lines = buttons.map((button) => button.textContent!.replace(/\s+/g, ' '));
    expect(lines[0]).toContain('Thresholds failed · 240 requests in 6.0 s');
    expect(lines[0]).toContain('2.5%errors');
    expect(lines[1]).toContain('Thresholds passed');
    expect(lines[2]).toContain('Stopped');
  });

  it('draws p95 bars on one scale', () => {
    const { buttons } = create();
    const widths = buttons.map(
      (button) => button.querySelector<HTMLElement>('.bg-sky-600')!.style.width,
    );
    expect(widths).toEqual(['100%', '25%', '25%']);
  });

  it('marks the shown run and selects another on click', () => {
    const { buttons, selected } = create();
    expect(buttons.map((button) => button.getAttribute('aria-current'))).toEqual([
      null,
      'true',
      null,
    ]);
    buttons[0].click();
    expect(selected).toEqual(['r2']);
  });

  it('asks to delete a run or clear the history', () => {
    const { deletes, clear, selected, removed, cleared } = create();
    deletes[2].click();
    clear.click();
    expect(removed).toEqual([['r0']]);
    expect(selected).toEqual([]);
    expect(cleared()).toBe(1);
  });

  it('deletes the checked runs together', () => {
    const { fixture, checks, query, header, removed, selected } = create();
    checks[0].click();
    checks[2].click();
    fixture.detectChanges();

    expect(header()).toBe('2 selected');
    expect(query('button[data-clear]')).toBeNull();
    query('button[data-delete-selected]')!.click();

    expect(removed).toEqual([['r2', 'r0']]);
    expect(selected).toEqual([]);
  });

  it('checks a range with shift-click and every run from the header', () => {
    const { fixture, checks, checkAll, query, header, removed } = create();
    checks[0].click();
    checks[2].dispatchEvent(new MouseEvent('click', { shiftKey: true }));
    fixture.detectChanges();
    expect(header()).toBe('3 selected');
    expect(checkAll.checked).toBe(true);

    checkAll.click();
    fixture.detectChanges();
    expect(header()).toBe('History');

    checkAll.click();
    fixture.detectChanges();
    query('button[data-delete-selected]')!.click();
    expect(removed).toEqual([['r2', 'r1', 'r0']]);

    query('button[data-deselect]')!.click();
    fixture.detectChanges();
    expect(header()).toBe('History');
  });

  it('forgets checked runs that are no longer listed', () => {
    const { fixture, checks, header } = create();
    checks[0].click();
    checks[1].click();
    fixture.componentRef.setInput('runs', [summary({ runId: 'r1' })]);
    fixture.detectChanges();

    expect(header()).toBe('1 selected');
  });

  it('shows when each run started in the system format', () => {
    useTestDateFormat();
    const { buttons, checks } = create();
    const started = testDateTime('2026-01-02T03:04:05Z');

    expect(buttons[0].textContent).toContain(started);
    expect(checks[0].getAttribute('aria-label')).toBe(`Select the run from ${started}`);
  });
});
