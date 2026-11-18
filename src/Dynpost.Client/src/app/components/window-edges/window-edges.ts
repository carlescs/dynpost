import { Component, inject } from '@angular/core';
import { WindowEdge } from '../../models/messages';
import { WindowFrame } from '../../services/window-frame';

/** Where each resize handle sits and the cursor it shows. */
const HANDLES: Record<WindowEdge, string> = {
  top: 'top-0 inset-x-2 h-1 cursor-ns-resize',
  bottom: 'bottom-0 inset-x-2 h-1 cursor-ns-resize',
  left: 'left-0 inset-y-2 w-1 cursor-ew-resize',
  right: 'right-0 inset-y-2 w-1 cursor-ew-resize',
  topLeft: 'top-0 left-0 size-2 cursor-nwse-resize',
  topRight: 'top-0 right-0 size-2 cursor-nesw-resize',
  bottomLeft: 'bottom-0 left-0 size-2 cursor-nesw-resize',
  bottomRight: 'bottom-0 right-0 size-2 cursor-nwse-resize',
};

/**
 * Invisible handles along the window's edges that resize it, where the app draws its own title
 * bar and the platform left no border to grab (see `WindowFrame.resizeEdges`).
 */
@Component({
  selector: 'app-window-edges',
  templateUrl: './window-edges.html',
  host: { 'aria-hidden': 'true' },
})
export class WindowEdges {
  protected readonly frame = inject(WindowFrame);
  protected readonly handles = HANDLES;
}
