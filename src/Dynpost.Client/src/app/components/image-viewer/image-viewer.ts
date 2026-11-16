import {
  afterNextRender,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { FaIconComponent } from '@fortawesome/angular-fontawesome';
import {
  faCircleExclamation,
  faDownLeftAndUpRightToCenter,
  faExpand,
  faLeftRight,
  faMagnifyingGlassMinus,
  faMagnifyingGlassPlus,
  faSpinner,
  faUpRightAndDownLeftFromCenter,
} from '@fortawesome/free-solid-svg-icons';

/** Zoom steps, as a fraction of the image's natural size. */
const ZOOM_STEPS = [0.1, 0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 3, 4, 8];
/** Space around the image inside the scroll area, in CSS px (matches `p-4`). */
const GUTTER = 16;
/** The size an SVG without its own dimensions is drawn at, in CSS px. */
const FALLBACK_SIZE = 300;

type Zoom = number | 'page' | 'width';

/**
 * Shows an image response with the same toolbar as the PDF viewer: its size, zoom steps, 100%,
 * fit to the view or to its width, and a maximized mode that fills the window (Escape restores
 * it). Transparent areas show over a checkerboard. The bytes are shown from a `blob:` URL in an
 * `<img>`, so an SVG can't run scripts or load anything.
 */
@Component({
  selector: 'app-image-viewer',
  imports: [FaIconComponent],
  host: {
    // An opaque background, so that maximized it hides what is beneath.
    class: 'flex min-h-0 flex-1 flex-col bg-white',
    '[class.image-viewer-maximized]': 'maximized()',
    '(document:keydown.escape)': 'maximized.set(false)',
  },
  styleUrl: './image-viewer.scss',
  templateUrl: './image-viewer.html',
})
export class ImageViewer {
  /** The image file's bytes. */
  readonly data = input.required<Uint8Array<ArrayBuffer>>();
  /** The image's media type (`image/png`, `image/svg+xml`, ...). */
  readonly contentType = input.required<string>();

  protected readonly zoom = signal<Zoom>(1);
  /** Whether the viewer fills the whole window instead of its place in the page. */
  protected readonly maximized = signal(false);
  protected readonly url = signal<string | null>(null);
  /** The image's natural size once it has loaded. */
  protected readonly natural = signal<{ width: number; height: number } | null>(null);
  protected readonly error = signal<string | null>(null);

  /** The scale the image is drawn at, resolved from `zoom` and the view's size. */
  protected readonly scale = computed(() => {
    const zoom = this.zoom();
    const size = this.natural();
    if (typeof zoom === 'number' || !size) return typeof zoom === 'number' ? zoom : 1;
    const fitWidth = (this.width() - 2 * GUTTER) / size.width;
    const fit =
      zoom === 'width' ? fitWidth : Math.min(fitWidth, (this.height() - 2 * GUTTER) / size.height);
    return Math.max(0.01, fit);
  });
  protected readonly percent = computed(() => `${Math.round(this.scale() * 100)}%`);
  /** The drawn size, in CSS px. */
  protected readonly drawn = computed(() => {
    const size = this.natural();
    const scale = this.scale();
    return size
      ? { width: Math.max(1, size.width * scale), height: Math.max(1, size.height * scale) }
      : null;
  });

  /** Raster images zoomed well past their size show crisp pixels rather than a blur. */
  protected readonly pixelated = computed(
    () => this.scale() > 2 && this.contentType() !== 'image/svg+xml',
  );

  protected readonly zoomSteps = ZOOM_STEPS;
  protected readonly faMagnifyingGlassMinus = faMagnifyingGlassMinus;
  protected readonly faMagnifyingGlassPlus = faMagnifyingGlassPlus;
  protected readonly faLeftRight = faLeftRight;
  protected readonly faExpand = faExpand;
  protected readonly faSpinner = faSpinner;
  protected readonly faMaximize = faUpRightAndDownLeftFromCenter;
  protected readonly faRestore = faDownLeftAndUpRightToCenter;
  protected readonly faCircleExclamation = faCircleExclamation;

  private readonly destroyRef = inject(DestroyRef);
  private readonly scroller = viewChild.required<ElementRef<HTMLElement>>('scroller');
  /** The scroll area's inner width, updated as it resizes. */
  private readonly width = signal(0);
  /** The scroll area's inner height, updated as it resizes. */
  private readonly height = signal(0);

  constructor() {
    afterNextRender(() => {
      const element = this.scroller().nativeElement;
      const resize = new ResizeObserver(() => {
        this.width.set(element.clientWidth);
        this.height.set(element.clientHeight);
      });
      resize.observe(element);
      this.destroyRef.onDestroy(() => resize.disconnect());
    });

    // A new object URL per image; the cleanup revokes it (on change or when destroyed).
    effect((onCleanup) => {
      const blob = new Blob([this.data()], { type: this.contentType() });
      const url = URL.createObjectURL(blob);
      this.natural.set(null);
      this.error.set(null);
      this.url.set(url);
      onCleanup(() => URL.revokeObjectURL(url));
    });
  }

  protected loaded(image: HTMLImageElement): void {
    // An SVG without width and height has no natural size: it gets a default one.
    this.natural.set({
      width: image.naturalWidth || FALLBACK_SIZE,
      height: image.naturalHeight || FALLBACK_SIZE,
    });
  }

  protected failed(): void {
    this.error.set(`The image (${this.contentType()}) could not be shown.`);
  }

  protected zoomBy(direction: 1 | -1): void {
    const current = this.scale();
    const next =
      direction > 0
        ? ZOOM_STEPS.find((step) => step > current + 0.001)
        : [...ZOOM_STEPS].reverse().find((step) => step < current - 0.001);
    if (next) this.zoom.set(next);
  }
}
