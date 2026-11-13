import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { PdfViewer } from '../../../../components/pdf-viewer/pdf-viewer';
import { RunResultDto, RunSampleDto, RunSamplesPageDto } from '../../../../models/messages';
import { Bridge } from '../../../../services/bridge';
import { PAGE_SIZE, RunSamples } from './run-samples';

const sample = (patch: Partial<RunSampleDto> = {}): RunSampleDto => ({
  request: 'users/get.yaml',
  thread: 2,
  iteration: 3,
  atMs: 1500,
  latencyMs: 42,
  status: 200,
  error: null,
  method: 'GET',
  url: 'https://api.example.com/users/1',
  dataRow: { user: 'ada' },
  responsePreview: '{"id":1}',
  ...patch,
});

/** Stands in for the PDF viewer, which needs pdf.js and a real layout. */
@Component({ selector: 'app-pdf-viewer', template: '{{ data().length }} bytes' })
class FakePdfViewer {
  readonly data = input.required<Uint8Array>();
}

const run = (patch: Partial<RunResultDto> = {}) => ({ runId: 'r1', ...patch }) as RunResultDto;

describe('RunSamples', () => {
  let send: ReturnType<typeof vi.fn>;

  function page(patch: Partial<RunSamplesPageDto> = {}): RunSamplesPageDto {
    return {
      offset: 0,
      matching: 2,
      logged: 2,
      sent: 2,
      samples: [
        sample(),
        sample({
          status: 500,
          error: 'HTTP 500',
          method: 'POST',
          responsePreview: 'boom',
          bodyFile: 'bodies/create/002.json',
        }),
      ],
      ...patch,
    };
  }

  async function create(result: RunResultDto = run()) {
    TestBed.configureTestingModule({ providers: [{ provide: Bridge, useValue: { send } }] });
    TestBed.overrideComponent(RunSamples, {
      remove: { imports: [PdfViewer] },
      add: { imports: [FakePdfViewer] },
    });
    const fixture = TestBed.createComponent(RunSamples);
    fixture.componentRef.setInput('run', result);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }
  const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim();

  beforeEach(() => {
    send = vi.fn().mockResolvedValue(page());
  });

  it('lists every request with its response status, latency and result', async () => {
    const element = (await create()).nativeElement as HTMLElement;
    expect(send).toHaveBeenCalledWith('run.samples', {
      runId: 'r1',
      offset: 0,
      limit: PAGE_SIZE,
      failedOnly: false,
    });
    const rows = [...element.querySelectorAll('[data-sample]')].map((row) =>
      [...row.querySelectorAll('td')].map(text),
    );
    expect(rows).toEqual([
      ['1', '1.5 s', '2', '3', 'GET https://api.example.com/users/1', '200', '42 ms', 'OK'],
      ['2', '1.5 s', '2', '3', 'POST https://api.example.com/users/1', '500', '42 ms', 'HTTP 500'],
    ]);
    expect(text(element.querySelector('[data-range]'))).toBe('2 requests');
  });

  it('shows the response of a row when it is clicked', async () => {
    const fixture = await create();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[data-detail]')).toBeNull();

    (element.querySelectorAll('[data-sample]')[1] as HTMLElement).click();
    fixture.detectChanges();

    const detail = element.querySelector('[data-detail]')!;
    expect(text(detail)).toContain('FailureHTTP 500');
    expect(text(detail)).toContain('user=ada');
    expect(text(detail.querySelector('[data-body-file]'))).toBe('bodies/create/002.json');
    expect(text(detail.querySelector('[data-response]'))).toBe('boom');
  });

  /** Answers `run.samples` from a log of `count` samples, numbered by their `thread`. */
  function log(count: number) {
    send.mockImplementation(async (_: string, { offset, limit }: Record<string, number>) => ({
      offset,
      matching: count,
      logged: count,
      sent: count,
      samples: Array.from({ length: Math.max(0, Math.min(limit, count - offset)) }, (_, i) =>
        sample({ thread: offset + i }),
      ),
    }));
  }

  async function scrollTo(fixture: Awaited<ReturnType<typeof create>>, top: number) {
    const scroller = (fixture.nativeElement as HTMLElement).querySelector(
      '.overflow-auto',
    ) as HTMLElement;
    Object.defineProperty(scroller, 'scrollTop', {
      value: top,
      writable: true,
      configurable: true,
    });
    scroller.dispatchEvent(new Event('scroll'));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('renders only the rows in view and reads the log a page at a time as they scroll in', async () => {
    log(5000);
    const fixture = await create();
    const element = fixture.nativeElement as HTMLElement;
    expect(send).toHaveBeenCalledTimes(1);
    expect(text(element.querySelector('[data-range]'))).toBe('5,000 requests');
    const rendered = element.querySelectorAll('[data-sample]');
    expect(rendered.length).toBeGreaterThan(0);
    expect(rendered.length).toBeLessThan(PAGE_SIZE);

    // Row 2,000 is on the 21st page.
    await scrollTo(fixture, 2000 * 25);
    expect(send).toHaveBeenCalledWith('run.samples', {
      runId: 'r1',
      offset: 20 * PAGE_SIZE,
      limit: PAGE_SIZE,
      failedOnly: false,
    });
    expect(send).not.toHaveBeenCalledWith(
      'run.samples',
      expect.objectContaining({ offset: 10 * PAGE_SIZE }),
    );
    const numbers = [...element.querySelectorAll('[data-sample]')].map((row) =>
      Number(text(row.querySelector('td'))),
    );
    expect(numbers).toContain(2001);
    expect(numbers.length).toBeLessThan(PAGE_SIZE);

    // Pages already read are not read again.
    const calls = send.mock.calls.length;
    await scrollTo(fixture, 2001 * 25);
    expect(send).toHaveBeenCalledTimes(calls);
  });

  it('shows placeholders for the rows of a page still being read', async () => {
    log(5000);
    const fixture = await create();
    send.mockReturnValue(new Promise(() => {}));
    await scrollTo(fixture, 3000 * 25);
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[data-sample]')).toBeNull();
    expect(element.querySelectorAll('[data-placeholder]').length).toBeGreaterThan(0);
  });

  it('starts over from the top when filtering the failed requests', async () => {
    log(5000);
    const fixture = await create();
    const element = fixture.nativeElement as HTMLElement;
    await scrollTo(fixture, 2000 * 25);

    const failed = [...element.querySelectorAll('[role="radio"]')].find(
      (b) => text(b) === 'Failed',
    ) as HTMLElement;
    failed.click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(send).toHaveBeenLastCalledWith('run.samples', {
      runId: 'r1',
      offset: 0,
      limit: PAGE_SIZE,
      failedOnly: true,
    });
    expect((element.querySelector('.overflow-auto') as HTMLElement).scrollTop).toBe(0);
  });

  it('reads the pages in view again when the run is updated', async () => {
    const fixture = await create(run({ state: 'running' }));
    expect(send).toHaveBeenCalledTimes(1);

    fixture.componentRef.setInput('run', run({ state: 'completed' }));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(send).toHaveBeenCalledTimes(2);
  });

  it('says when the log could not keep every request', async () => {
    send.mockResolvedValue(page({ logged: 10_000, sent: 25_000, matching: 10_000 }));
    const element = (await create()).nativeElement as HTMLElement;
    expect(text(element.querySelector('[data-truncated]'))).toBe(
      'Only the first 10,000 of the 25,000 requests sent are kept.',
    );
  });

  it('opens a saved PDF response in the previewer', async () => {
    send.mockImplementation(async (type: string) =>
      type === 'run.response'
        ? { body: 'JVBERg==' }
        : page({
            matching: 1,
            logged: 1,
            sent: 1,
            samples: [
              sample({
                contentType: 'application/pdf',
                responsePreview: null,
                responseFile: '1.pdf',
              }),
            ],
          }),
    );
    const fixture = await create();
    const element = fixture.nativeElement as HTMLElement;
    (element.querySelector('[data-sample]') as HTMLElement).click();
    fixture.detectChanges();
    expect(text(element.querySelector('[data-saved]'))).toBe('.dynypost/runs/responses/r1/1.pdf');

    (element.querySelector('[data-open-pdf]') as HTMLElement).click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(send).toHaveBeenLastCalledWith('run.response', { runId: 'r1', file: '1.pdf' });
    expect(text(element.querySelector('[data-pdf]'))).toBe('4 bytes');

    (element.querySelector('[data-open-pdf]') as HTMLElement).click();
    fixture.detectChanges();
    expect(element.querySelector('[data-pdf]')).toBeNull();
  });

  it('explains how to preview a PDF response that was not saved', async () => {
    send.mockResolvedValue(
      page({
        samples: [sample({ contentType: 'application/pdf', responsePreview: null })],
      }),
    );
    const fixture = await create();
    const element = fixture.nativeElement as HTMLElement;
    (element.querySelector('[data-sample]') as HTMLElement).click();
    fixture.detectChanges();

    expect(element.querySelector('[data-open-pdf]')).toBeNull();
    expect(text(element.querySelector('[data-pdf-not-saved]'))).toContain('Save whole responses');
  });
});
