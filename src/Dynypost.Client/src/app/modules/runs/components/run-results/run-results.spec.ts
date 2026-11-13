import { TestBed } from '@angular/core/testing';
import { RunResultDto, RunStatsDto } from '../../../../models/messages';
import { RunResults } from './run-results';
import { testDateTime, useTestDateFormat } from '../../../../testing/date-format';

const stats = (patch: Partial<RunStatsDto> = {}): RunStatsDto => ({
  samples: 200,
  errors: 4,
  errorPercent: 2,
  min: 10,
  average: 80,
  median: 50,
  p90: 150,
  p95: 200,
  p99: 300,
  max: 400,
  throughput: 40,
  bytesSent: 2048,
  bytesReceived: 4096,
  statusCodes: { '200': 196, '500': 4 },
  errorReasons: { 'HTTP 500': 3, Timeout: 1 },
  ...patch,
});

const run = (patch: Partial<RunResultDto> = {}): RunResultDto => ({
  runId: 'r1',
  planPath: 'a.run.yaml',
  planName: 'Smoke',
  state: 'completed',
  startedAt: '2026-01-02T03:04:05Z',
  elapsedMs: 5000,
  threads: 10,
  activeThreads: 0,
  total: stats(),
  requests: [
    { request: 'a.yaml', stats: stats({ samples: 100, errors: 0, max: 200 }) },
    { request: 'b.yaml', stats: stats({ samples: 100 }) },
  ],
  timeline: [{ second: 0, samples: 40, errors: 1, averageMs: 80 }],
  thresholds: [
    { name: 'p95', limit: '500ms', actual: '200ms', passed: true },
    { name: 'errorRate', limit: '1%', actual: '2%', passed: false },
  ],
  passed: false,
  failedSamples: [
    {
      request: 'b.yaml',
      thread: 3,
      iteration: 4,
      atMs: 2500,
      latencyMs: 450,
      status: 500,
      error: 'HTTP 500',
      method: 'POST',
      url: 'https://api.example.com/users',
      dataRow: { user: 'ada' },
      responsePreview: '{"error":"boom"}',
    },
  ],
  slowestSamples: [],
  message: null,
  ...patch,
});

describe('RunResults', () => {
  function create(result: RunResultDto) {
    const fixture = TestBed.createComponent(RunResults);
    fixture.componentRef.setInput('run', result);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }
  const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim();

  it('shows how the run ended and whether its thresholds held', () => {
    const element = create(run());
    expect(text(element.querySelector('[data-state]'))).toBe('Completed');
    expect(text(element.querySelector('[data-verdict]'))).toBe('Thresholds failed');
    const thresholds = [...element.querySelectorAll('[data-threshold]')].map(text);
    expect(thresholds).toEqual([
      'p95: 200ms Within the limit of 500ms',
      'Error rate: 2% Outside the limit of 1%',
    ]);
  });

  it('gives no verdict while the run is going', () => {
    const element = create(run({ state: 'running', passed: null, activeThreads: 7 }));
    expect(text(element.querySelector('[data-state]'))).toBe('Running');
    expect(element.querySelector('[data-verdict]')).toBeNull();
    expect(text(element)).toContain('Users 7/10');
  });

  it('shows the headline numbers', () => {
    const tiles = [...create(run()).querySelectorAll('dl')[1].children].map(text);
    expect(tiles).toEqual([
      'Requests 200 4.0 KB received',
      'Throughput 40.0 req/s2.0 KB sent',
      'Errors 2% 4 failed',
      'Average 80 ms median 50 ms',
      'p95 200 ms p90 150 ms',
      'p99 300 ms max 400 ms',
    ]);
  });

  it('lists each request with its failures', () => {
    const requests = [...create(run()).querySelectorAll('[data-request]')].map(text);
    expect(requests[0]).toContain('a.yaml 100 requests Average');
    expect(requests[1]).toContain('b.yaml 100 requests 4 failed (2%)');
  });

  it('breaks down status codes and error reasons', () => {
    const element = create(run());
    expect([...element.querySelectorAll('[data-status]')].map(text)).toEqual([
      '200 196 (98%)',
      '500 4 (2%)',
    ]);
    expect([...element.querySelectorAll('[data-error]')].map(text)).toEqual([
      'HTTP 5003',
      'Timeout1',
    ]);
  });

  it('details the samples that were kept', () => {
    const sample = create(run()).querySelector('[data-sample]')!;
    expect(text(sample.querySelector('summary'))).toBe(
      'POST https://api.example.com/users 500 450 ms',
    );
    expect(text(sample)).toContain('2.5 s into the run, by user 3 (iteration 4)');
    expect(text(sample)).toContain('user=ada');
    expect(text(sample)).toContain('{"error":"boom"}');
  });

  it('asks to show every request and response', () => {
    const fixture = TestBed.createComponent(RunResults);
    fixture.componentRef.setInput('run', run());
    let asked = 0;
    fixture.componentInstance.showRequests.subscribe(() => asked++);
    fixture.detectChanges();
    const links = (fixture.nativeElement as HTMLElement).querySelectorAll('[data-show-requests]');
    expect(links.length).toBe(2);
    (links[0] as HTMLElement).click();
    (links[1] as HTMLElement).click();
    expect(asked).toBe(2);
  });

  it('reports why a run failed', () => {
    const element = create(run({ state: 'failed', passed: null, message: 'No requests to send.' }));
    expect(text(element.querySelector('[role="alert"]'))).toBe('No requests to send.');
  });

  it('shows when the run started in the system format', () => {
    useTestDateFormat();
    expect(text(create(run()))).toContain(testDateTime('2026-01-02T03:04:05Z'));
  });
});
