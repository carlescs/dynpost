import { Injectable } from '@angular/core';
import {
  BridgeErrorShape,
  BridgeMessageType,
  BridgeMessages,
  BridgeRequestEnvelope,
  BridgeResponseEnvelope,
} from './messages';
import { MockBackend } from './mock-backend';

/** The messaging API Photino injects into the webview. */
interface PhotinoExternal {
  sendMessage(message: string): void;
  receiveMessage(callback: (message: string) => void): void;
}

/** A failure reported by the host (or by the bridge itself). */
export class BridgeError extends Error implements BridgeErrorShape {
  constructor(
    readonly code: string,
    message: string,
  ) {
    super(message);
    this.name = 'BridgeError';
  }
}

interface Pending {
  resolve(value: unknown): void;
  reject(error: BridgeError): void;
}

/**
 * Typed request/response channel to the .NET host. Each request carries a correlation id that
 * the host echoes in its response.
 *
 * Outside the desktop host (the `npm start` dev server) requests go to an in-memory
 * {@link MockBackend} with sample data, so the UI can still be developed in a browser.
 */
@Injectable({ providedIn: 'root' })
export class Bridge {
  private readonly external = Bridge.findHost();
  private readonly mock = this.external ? null : new MockBackend();
  private readonly pending = new Map<string, Pending>();
  private nextId = 1;

  /** Whether the app runs inside the desktop host rather than a plain browser. */
  readonly isDesktop = this.external !== null;

  constructor() {
    this.external?.receiveMessage((message) => this.receive(message));
  }

  send<K extends BridgeMessageType>(
    type: K,
    ...payload: BridgeMessages[K]['payload'] extends void ? [] : [BridgeMessages[K]['payload']]
  ): Promise<BridgeMessages[K]['result']> {
    const envelope: BridgeRequestEnvelope = {
      id: String(this.nextId++),
      type,
      payload: payload[0],
    };

    if (this.mock) {
      return this.mock.handle(envelope).then((response) => this.unwrap(response)) as Promise<
        BridgeMessages[K]['result']
      >;
    }

    return new Promise((resolve, reject) => {
      this.pending.set(envelope.id, { resolve: resolve as (value: unknown) => void, reject });
      this.external!.sendMessage(JSON.stringify(envelope));
    });
  }

  private receive(message: string): void {
    let response: BridgeResponseEnvelope;
    try {
      response = JSON.parse(message);
    } catch {
      console.error('Ignoring malformed bridge message', message);
      return;
    }

    const pending = this.pending.get(response.id);
    if (!pending) return;
    this.pending.delete(response.id);

    try {
      pending.resolve(this.unwrap(response));
    } catch (error) {
      pending.reject(error as BridgeError);
    }
  }

  private unwrap(response: BridgeResponseEnvelope): unknown {
    if (response.ok) return response.payload;
    throw new BridgeError(response.error.code, response.error.message);
  }

  private static findHost(): PhotinoExternal | null {
    const external = (window as unknown as { external?: Partial<PhotinoExternal> }).external;
    return typeof external?.sendMessage === 'function' &&
      typeof external.receiveMessage === 'function'
      ? (external as PhotinoExternal)
      : null;
  }
}
