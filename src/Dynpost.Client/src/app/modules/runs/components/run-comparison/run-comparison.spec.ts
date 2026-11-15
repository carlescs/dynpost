import { TestBed } from '@angular/core/testing';
import { ComparisonRowDto, MetricDeltaDto, RunComparisonDto } from '../../../../models/messages';
import { RunComparison } from './run-comparison';
import { testDateTime, useTestDateFormat } from '../../../../testing/date-format';

const delta = (patch: Partial<MetricDeltaDto>): MetricDeltaDto => ({
  metric: 'p95',
  baseline: 100,
  current: 100,
  change: 0,
  changePercent: 0,
  verdict: 'same',
  ...patch,
});

const row = (patch: Partial<ComparisonRowDto>): ComparisonRowDto => ({
  key: 'requests/users.yaml',
  name: 'requests/users.yaml',
  inBaseline: true,
  inCurrent: true,
  metrics: [],
  ...patch,
});

const comparison = (): RunComparisonDto => ({
  baseline: { runId: 'old', startedAt: '2026-01-02T03:04:05Z' } as RunComparisonDto['baseline'],
  current: { runId: 'new', startedAt: '2026-01-03T03:04:05Z' } as RunComparisonDto['current'],
  total: row({
    key: '',
    name: 'Total',
    metrics: [
      delta({
        metric: 'p95',
        baseline: 200,
        current: 150,
        change: -50,
        changePercent: -25,
        verdict: 'better',
      }),
      delta({
        metric: 'errorPercent',
        baseline: 0,
        current: 2,
        change: 2,
        changePercent: null,
        verdict: 'worse',
      }),
      delta({ metric: 'samples', baseline: 100, current: 100 }),
    ],
  }),
  requests: [
    row({
      metrics: [
        delta({ metric: 'p95', current: 300, change: 200, changePercent: 200, verdict: 'worse' }),
      ],
    }),
    row({ key: 'requests/new.yaml', name: 'requests/new.yaml', inBaseline: false }),
  ],
  environments: [],
});

describe('RunComparison', () => {
  useTestDateFormat();

  function create() {
    const fixture = TestBed.createComponent(RunComparison);
    fixture.componentRef.setInput('comparison', comparison());
    let closed = 0;
    fixture.componentInstance.close.subscribe(() => closed++);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    return { element, closed: () => closed };
  }

  it('shows both runs and how many metrics got better or worse', () => {
    const { element } = create();
    expect(element.querySelector('[data-baseline]')!.textContent!.trim()).toBe(
      testDateTime('2026-01-02T03:04:05Z'),
    );
    expect(element.querySelector('[data-tally]')!.textContent!.replace(/\s+/g, ' ').trim()).toBe(
      '1 better · 1 worse',
    );
  });

  it('colors each overall change by its verdict', () => {
    const { element } = create();
    const p95 = element.querySelector<HTMLElement>('[data-total] tr[data-metric="p95"]')!;
    expect(p95.textContent!.replace(/\s+/g, ' ')).toContain('200 ms 150 ms −25%');
    expect(p95.querySelector('.text-emerald-700')).not.toBeNull();
    const errors = element.querySelector<HTMLElement>('tr[data-metric="errorPercent"]')!;
    expect(errors.textContent).toContain('+2 pts');
    expect(errors.querySelector('.text-rose-700')).not.toBeNull();
  });

  it('lists the requests, marking those in one run only', () => {
    const { element } = create();
    const rows = [
      ...element.querySelectorAll<HTMLElement>('[data-section="request"] tr[data-row]'),
    ];
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('+200%');
    expect(rows[1].querySelector('[data-missing]')!.textContent!.trim()).toBe(
      'Not in the baseline run',
    );
    expect(element.querySelector('[data-section="environment"]')).toBeNull();
  });

  it('asks to close', () => {
    const { element, closed } = create();
    element.querySelector<HTMLElement>('[data-close-comparison]')!.click();
    expect(closed()).toBe(1);
  });
});
