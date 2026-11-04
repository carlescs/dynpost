import { Injectable } from '@angular/core';
import {
  BRIDGE_UNAVAILABLE,
  BridgeRequestEnvelope,
  BridgeResponseEnvelope,
  BridgeResult,
  MessageType,
  RequestOf,
  ResponseOf,
} from './messages';

/** The message API Photino injects into the webview as `window.external`. */
interface PhotinoExternal {
  sendMessage(message: string): void;
  receiveMessage(callback: (message: string) => void): void;
}

function photinoExternal(): PhotinoExternal | null {
  const external = (globalThis as { external?: Partial<PhotinoExternal> }).external;
  return typeof external?.sendMessage === 'function' &&
    typeof external.receiveMessage === 'function'
    ? (external as PhotinoExternal)
    : null;
}

/**
 * Typed request/response calls to the .NET host over Photino's message bridge. Every call
 * resolves to a {@link BridgeResult}; it never rejects.
 */
@Injectable({ providedIn: 'root' })
export class Bridge {
  private readonly external = photinoExternal();
  private readonly pending = new Map<string, (response: BridgeResponseEnvelope) => void>();
  private nextId = 0;

  /** Whether the app runs inside the desktop host (not, for example, on the dev server). */
  readonly available = this.external !== null;

  constructor() {
    this.external?.receiveMessage((message) => this.receive(message));
  }

  request<T extends MessageType>(
    type: T,
    payload: RequestOf<T>,
  ): Promise<BridgeResult<ResponseOf<T>>> {
    if (!this.external) {
      return Promise.resolve({
        ok: false,
        error: {
          code: BRIDGE_UNAVAILABLE,
          message: 'This needs the Missive desktop app.',
        },
      });
    }

    const id = String(++this.nextId);
    const envelope: BridgeRequestEnvelope = { id, type, payload };
    return new Promise((resolve) => {
      this.pending.set(id, (response) =>
        resolve(
          response.ok
            ? { ok: true, value: response.payload as ResponseOf<T> }
            : { ok: false, error: response.error },
        ),
      );
      this.external!.sendMessage(JSON.stringify(envelope));
    });
  }

  private receive(message: string): void {
    let response: BridgeResponseEnvelope;
    try {
      response = JSON.parse(message) as BridgeResponseEnvelope;
    } catch {
      console.error('Ignored a bridge message that is not JSON.', message);
      return;
    }

    const resolve = this.pending.get(response.id);
    if (!resolve) {
      console.error('Ignored a bridge response with an unknown id.', response);
      return;
    }
    this.pending.delete(response.id);
    resolve(response);
  }
}
