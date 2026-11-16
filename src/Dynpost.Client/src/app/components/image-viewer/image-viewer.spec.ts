import { TestBed } from '@angular/core/testing';
import { ImageViewer } from './image-viewer';

describe('ImageViewer', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'ResizeObserver',
      class {
        observe() {}
        disconnect() {}
      },
    );
    URL.createObjectURL ??= () => '';
    URL.revokeObjectURL ??= () => {};
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:image');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  async function create() {
    const fixture = TestBed.createComponent(ImageViewer);
    fixture.componentRef.setInput('data', new Uint8Array([0x89, 0x50, 0x4e, 0x47]));
    fixture.componentRef.setInput('contentType', 'image/png');
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  /** Makes the image report it has loaded, with the given natural size. */
  function load(host: HTMLElement, width: number, height: number) {
    const image = host.querySelector('img') as HTMLImageElement;
    Object.defineProperty(image, 'naturalWidth', { value: width });
    Object.defineProperty(image, 'naturalHeight', { value: height });
    image.dispatchEvent(new Event('load'));
    return image;
  }

  it('shows the image from an object URL at 100% with its size', async () => {
    const fixture = await create();
    const host = fixture.nativeElement as HTMLElement;
    const image = load(host, 640, 480);
    fixture.detectChanges();

    expect(image.getAttribute('src')).toBe('blob:image');
    expect(image.style.width).toBe('640px');
    expect(host.textContent).toContain('640 × 480 px');
    const actual = host.querySelector('[aria-label="100% zoom"]') as HTMLButtonElement;
    expect(actual.getAttribute('aria-pressed')).toBe('true');
  });

  it('zooms in and out by steps', async () => {
    const fixture = await create();
    const host = fixture.nativeElement as HTMLElement;
    const image = load(host, 100, 50);
    fixture.detectChanges();

    (host.querySelector('[aria-label="Zoom in"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(image.style.width).toBe('125px');
    expect(host.textContent).toContain('125%');

    (host.querySelector('[aria-label="Zoom out"]') as HTMLButtonElement).click();
    (host.querySelector('[aria-label="Zoom out"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(image.style.width).toBe('75px');
  });

  it('reports an image that cannot be decoded', async () => {
    const fixture = await create();
    const host = fixture.nativeElement as HTMLElement;
    (host.querySelector('img') as HTMLImageElement).dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('could not be shown');
  });

  it('revokes the object URL when destroyed', async () => {
    const fixture = await create();
    fixture.destroy();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:image');
  });

  it('maximizes to fill the window and restores with Escape', async () => {
    const fixture = await create();
    const host = fixture.nativeElement as HTMLElement;
    (host.querySelector('[data-image-maximize]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(host.classList).toContain('image-viewer-maximized');

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(host.classList).not.toContain('image-viewer-maximized');
  });
});
