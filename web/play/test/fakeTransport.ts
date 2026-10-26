import type { Transport } from "../src/session/transport";

type Handler = (...args: unknown[]) => void;

/** Records invokes and lets a test fire server → client messages. */
export class FakeTransport implements Transport {
  connected = false;
  starts = 0;
  stops = 0;
  invokes: { method: string; args: unknown[] }[] = [];
  /** Per-method responders; return a value or throw to simulate a server error. */
  responders: Record<string, (...args: unknown[]) => unknown> = {};
  failStart: Error | null = null;
  private handlers = new Map<string, Handler[]>();
  private reconnected: (() => void)[] = [];
  private closed: ((err?: Error) => void)[] = [];

  async start(): Promise<void> {
    this.starts++;
    if (this.failStart) throw this.failStart;
    this.connected = true;
  }
  async stop(): Promise<void> {
    this.stops++;
    this.connected = false;
  }
  async invoke<T>(method: string, ...args: unknown[]): Promise<T> {
    this.invokes.push({ method, args });
    const responder = this.responders[method];
    return (responder ? responder(...args) : undefined) as T;
  }
  on(method: string, handler: Handler): void {
    this.handlers.set(method, [...(this.handlers.get(method) ?? []), handler]);
  }
  onReconnected(fn: () => void): void {
    this.reconnected.push(fn);
  }
  onClose(fn: (err?: Error) => void): void {
    this.closed.push(fn);
  }

  // ------------------------------------------------ test helpers
  fire(method: string, ...args: unknown[]): void {
    for (const h of this.handlers.get(method) ?? []) h(...args);
  }
  fireReconnected(): void {
    this.connected = true;
    for (const f of this.reconnected) f();
  }
  fireClose(err?: Error): void {
    this.connected = false;
    for (const f of this.closed) f(err);
  }
  methods(): string[] {
    return this.invokes.map((i) => i.method);
  }
}

/** The text SignalR's JS client puts in the rejected Error for a server-side HubException. */
export const hubError = (method: string, message: string) =>
  new Error(`An unexpected error occurred invoking '${method}' on the server. HubException: ${message}`);

export const flush = () => new Promise((r) => setTimeout(r, 0));
