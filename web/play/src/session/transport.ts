import { HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";
import { backoffDelays } from "./backoff";

/** The slice of a SignalR connection the session and dev host use; faked in tests. */
export interface Transport {
  start(): Promise<void>;
  stop(): Promise<void>;
  invoke<T>(method: string, ...args: unknown[]): Promise<T>;
  /** Handlers get the server's raw arguments; receivers validate them. */
  on(method: string, handler: (...args: any[]) => void): void;
  onReconnected(fn: () => void): void;
  onClose(fn: (err?: Error) => void): void;
  readonly connected: boolean;
}

/** A SignalR hub connection with the ArcBox backoff and stateful reconnect. */
export function signalrTransport(url: string): Transport {
  const connection = new HubConnectionBuilder()
    .withUrl(url)
    .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (ctx) => backoffDelays(ctx.previousRetryCount) })
    .withStatefulReconnect()
    .configureLogging(LogLevel.Warning)
    .build();

  return {
    async start() {
      // A connection stuck in Reconnecting (e.g. a phone that slept) is restarted from scratch.
      if (connection.state === HubConnectionState.Connected) return;
      if (connection.state !== HubConnectionState.Disconnected) await connection.stop();
      await connection.start();
    },
    stop: () => connection.stop(),
    invoke: <T>(method: string, ...args: unknown[]) => connection.invoke<T>(method, ...args),
    on: (method, handler) => connection.on(method, handler),
    onReconnected: (fn) => connection.onreconnected(() => fn()),
    onClose: (fn) => connection.onclose((err) => fn(err)),
    get connected() {
      return connection.state === HubConnectionState.Connected;
    },
  };
}
