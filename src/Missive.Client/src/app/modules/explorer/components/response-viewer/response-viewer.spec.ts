import { TestBed } from '@angular/core/testing';
import { ResponseDto } from '../../../../models/messages';
import { RequestDocument } from '../../../../models/workspace';
import { WorkspaceStore } from '../../../../services/workspace-store';
import { copyText, ResponseViewer, timingPhases } from './response-viewer';

const response = (patch: Partial<ResponseDto> = {}): ResponseDto => ({
  status: 200,
  statusText: 'OK',
  headers: [{ key: 'Content-Type', value: 'application/octet-stream' }],
  contentType: 'application/octet-stream',
  body: 'AAEC',
  bodyEncoding: 'base64',
  size: 3,
  truncated: false,
  durationMs: 120,
  fileName: 'data.bin',
  timings: { dns: 10, connect: 20, tls: null, wait: 60, download: 10 },
  redirects: [
    {
      method: 'GET',
      url: 'https://a.test/old',
      status: 301,
      statusText: 'Moved Permanently',
      location: 'https://a.test/new',
      durationMs: 15,
    },
  ],
  sent: {
    method: 'GET',
    url: 'https://a.test/new',
    headers: [{ key: 'Authorization', value: 'Bearer ••••••' }],
  },
  cookies: [{ name: 'session', value: 'abc', path: '/', secure: true, httpOnly: true }],
  ...patch,
});

describe('timingPhases', () => {
  it('lays the measured phases out one after another', () => {
    const phases = timingPhases(response());
    expect(phases.map((p) => p.label)).toEqual([
      'DNS lookup',
      'TCP connect',
      'Waiting (TTFB)',
      'Download',
    ]);
    expect(phases.map((p) => [p.offset, p.width])).toEqual([
      [0, 10],
      [10, 20],
      [30, 60],
      [90, 10],
    ]);
  });

  it('is empty without timings', () => {
    expect(timingPhases(null)).toEqual([]);
    expect(timingPhases(response({ timings: null }))).toEqual([]);
  });
});

describe('copyText', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('uses the clipboard API when it is available', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal('navigator', { clipboard: { writeText } });
    expect(await copyText('hello')).toBe(true);
    expect(writeText).toHaveBeenCalledWith('hello');
  });

  it('falls back to copying a hidden text area', async () => {
    vi.stubGlobal('navigator', {});
    const exec = vi.fn().mockReturnValue(true);
    document.execCommand = exec;
    expect(await copyText('hello')).toBe(true);
    expect(exec).toHaveBeenCalledWith('copy');
    expect(document.querySelector('textarea')).toBeNull();
  });
});

describe('ResponseViewer', () => {
  const saveResponseBody = vi.fn();

  function render(r: ResponseDto) {
    TestBed.configureTestingModule({
      providers: [{ provide: WorkspaceStore, useValue: { saveResponseBody, cancelSend: vi.fn() } }],
    });
    const fixture = TestBed.createComponent(ResponseViewer);
    fixture.componentRef.setInput('document', {
      id: 'doc-1',
      sendId: null,
      response: { kind: 'response', response: r, sendId: 'send-1' },
    } as unknown as RequestDocument);
    fixture.detectChanges();
    return fixture;
  }

  function click(fixture: ReturnType<typeof render>, text: string): void {
    const button = [...fixture.nativeElement.querySelectorAll('button')].find((b: HTMLElement) =>
      b.textContent?.trim().startsWith(text),
    ) as HTMLButtonElement | undefined;
    if (!button) throw new Error(`No button '${text}'`);
    button.click();
    fixture.detectChanges();
  }

  beforeEach(() => saveResponseBody.mockReset());

  it('saves the body and says where', async () => {
    saveResponseBody.mockResolvedValue('/home/me/data.bin');
    const fixture = render(response());

    click(fixture, 'Save');
    await fixture.whenStable();
    fixture.detectChanges();

    expect(saveResponseBody).toHaveBeenCalledWith('doc-1');
    expect(fixture.nativeElement.querySelector('[role=status]').textContent).toContain(
      'Saved to /home/me/data.bin',
    );
  });

  it('offers copying only for text bodies', () => {
    const fixture = render(response());
    const labels = [...fixture.nativeElement.querySelectorAll('button')].map((b: HTMLElement) =>
      b.textContent?.trim(),
    );
    expect(labels).not.toContain('Copy');
  });

  it('shows the cookies, the timing breakdown with redirects, and the request as sent', () => {
    const fixture = render(response());
    const text = () => fixture.nativeElement.textContent as string;

    click(fixture, 'Cookies');
    expect(text()).toContain('session');
    expect(text()).toContain('Secure HttpOnly');

    click(fixture, 'Timing');
    expect(text()).toContain('DNS lookup');
    expect(text()).not.toContain('TLS handshake');
    expect(text()).toContain('https://a.test/old');
    expect(text()).toContain('301');

    click(fixture, 'Request');
    expect(text()).toContain('https://a.test/new');
    expect(text()).toContain('Bearer ••••••');
  });
});
