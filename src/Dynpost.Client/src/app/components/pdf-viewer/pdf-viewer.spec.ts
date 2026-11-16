import { TestBed } from '@angular/core/testing';
import { PdfViewer } from './pdf-viewer';

// The bytes aren't a valid PDF: these tests only cover the toolbar.
describe('PdfViewer', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        disconnect() {}
      },
    );
  });

  afterEach(() => vi.unstubAllGlobals());

  async function create() {
    const fixture = TestBed.createComponent(PdfViewer);
    fixture.componentRef.setInput('data', new Uint8Array([0x25, 0x50, 0x44, 0x46]));
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  it('shows pages at 100% by default', async () => {
    const fixture = await create();
    const host = fixture.nativeElement as HTMLElement;
    const actual = host.querySelector('[aria-label="100% zoom"]') as HTMLButtonElement;
    const width = host.querySelector('[aria-label="Fit to width"]') as HTMLButtonElement;
    expect(actual.getAttribute('aria-pressed')).toBe('true');
    expect(width.getAttribute('aria-pressed')).toBe('false');
  });

  it('maximizes to fill the window and restores with the button or Escape', async () => {
    const fixture = await create();
    const host = fixture.nativeElement as HTMLElement;
    const button = host.querySelector('[data-pdf-maximize]') as HTMLButtonElement;
    expect(host.classList).not.toContain('pdf-viewer-maximized');

    button.click();
    fixture.detectChanges();
    expect(host.classList).toContain('pdf-viewer-maximized');
    expect(button.getAttribute('aria-label')).toBe('Restore');

    button.click();
    fixture.detectChanges();
    expect(host.classList).not.toContain('pdf-viewer-maximized');

    button.click();
    fixture.detectChanges();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(host.classList).not.toContain('pdf-viewer-maximized');
  });
});
