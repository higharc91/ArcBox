import {
  ClientMethods,
  isGameResults,
  isLobbyState,
  parseControllerView,
  type ControllerView,
  type GameResults,
  type JoinResult,
  type LobbyState,
  type PlayerId,
} from "../protocol";
import { loadModule } from "../games/registry";
import { backoffDelays } from "./backoff";
import { clearSeat, loadSeat, saveSeat, type KeyValueStore } from "./storage";
import type { Transport } from "./transport";

export type { Transport } from "./transport";

export type SessionState =
  | { screen: "join"; code?: string; name?: string; error?: string; busy?: boolean }
  | { screen: "connecting" }
  | { screen: "lobby"; lobby: LobbyState; me: PlayerId; error?: string }
  | { screen: "game"; view: ControllerView; lobby: LobbyState; me: PlayerId }
  | { screen: "results"; results: GameResults; lobby: LobbyState; me: PlayerId }
  | { screen: "closed"; reason: string };

const OFFLINE = "Can't reach the server. Check your connection and try again.";
const KICKED = "You were removed from the room.";

/** SignalR wraps a server HubException as "An unexpected error occurred invoking 'X' on the server. HubException: <message>". */
const HUB_PREFIX = "HubException: ";

/** True when the server answered with an error, as opposed to the network failing. */
export function isServerError(err: unknown): boolean {
  return err instanceof Error && err.message.includes("on the server");
}

/** The text to show for a failed hub call: the server's own message when it sent one. */
export function serverMessage(err: unknown): string {
  const message = err instanceof Error ? err.message : String(err);
  const at = message.indexOf(HUB_PREFIX);
  if (at >= 0) return message.slice(at + HUB_PREFIX.length).trim();
  return isServerError(err) ? "Something went wrong on the server." : OFFLINE;
}

/**
 * Owns the phone's hub connection: join, rejoin after reloads and network drops, and the screen to show.
 * Correctness never depends on buffered messages: every (re)join gets a fresh lobby and view snapshot from the server.
 */
export class Session {
  state: SessionState = { screen: "join" };

  private listeners = new Set<(s: SessionState) => void>();
  private code: string | null = null;
  private me: PlayerId | null = null;
  private lobby: LobbyState | null = null;
  private view: ControllerView | null = null;
  private results: GameResults | null = null;
  private closedReason: string | null = null;
  private lobbyError: string | undefined;
  private rejoining: Promise<boolean> | null = null;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;
  private retryAttempt = 0;

  constructor(
    private readonly transport: Transport,
    private readonly store: KeyValueStore,
    private readonly slot?: string,
  ) {
    transport.on(ClientMethods.LobbyChanged, (lobby: unknown) => this.onLobby(lobby));
    transport.on(ClientMethods.ControllerView, (view: unknown) => this.onView(view));
    transport.on(ClientMethods.GameEnded, (results: unknown) => this.onGameEnded(results));
    transport.on(ClientMethods.RoomClosed, (reason: unknown) => this.close(typeof reason === "string" && reason ? reason : "The room has closed."));
    transport.on(ClientMethods.Kicked, () => this.close(KICKED));
    transport.onReconnected(() => void this.rejoin());
    transport.onClose(() => this.onConnectionLost());
  }

  subscribe(fn: (s: SessionState) => void): () => void {
    this.listeners.add(fn);
    return () => this.listeners.delete(fn);
  }

  /** Join a room by code with a display name; on failure the join form keeps its values and shows the server's message. */
  async join(code: string, name: string): Promise<void> {
    code = code.trim().toUpperCase();
    name = name.trim();
    this.reset();
    this.set({ screen: "join", code, name, busy: true });
    try {
      if (!this.transport.connected) await this.transport.start();
      const result = await this.transport.invoke<JoinResult>("Join", code, name);
      this.code = result.code;
      this.me = result.playerId;
      saveSeat(this.store, result.code, this.slot, { playerId: result.playerId, secret: result.secret, name });
      if (this.lobby) this.render();
      else this.set({ screen: "connecting" });
    } catch (err) {
      this.set({ screen: "join", code, name, error: serverMessage(err) });
    }
  }

  /**
   * Rejoin a room this browser already has a seat in. Returns false (and shows Join with the code) when there is
   * no seat or the server no longer knows it; true while the seat is still live, even if the network is down for now.
   */
  async resume(code: string): Promise<boolean> {
    code = code.trim().toUpperCase();
    if (!loadSeat(this.store, code, this.slot)) {
      this.set({ screen: "join", code });
      return false;
    }
    this.reset();
    this.code = code;
    this.set({ screen: "connecting" });
    return this.rejoin();
  }

  /** Call on visibilitychange → visible and on pageshow: a phone that slept may have a dead socket. */
  async onVisible(): Promise<void> {
    if (this.transport.connected || this.closedReason !== null || !this.code) return;
    if (!loadSeat(this.store, this.code, this.slot)) return;
    await this.rejoin();
  }

  async send(viewId: string, payload: object): Promise<void> {
    try {
      await this.transport.invoke("SendInput", viewId, payload);
    } catch {
      // A dropped input is harmless: the server re-sends the current view after a rejoin.
    }
  }

  selectGame(gameId: string): Promise<void> {
    return this.lobbyCall("SelectGame", gameId);
  }

  startGame(): Promise<void> {
    return this.lobbyCall("StartGame");
  }

  dismissResults(): void {
    this.results = null;
    this.render();
  }

  /** From the closed screen: back to an empty Join form. */
  leaveClosed(): void {
    this.reset();
    this.code = null;
    this.set({ screen: "join" });
  }

  dispose(): void {
    this.clearRetry();
    this.listeners.clear();
  }

  // ---------------------------------------------------------------- internals

  private rejoin(): Promise<boolean> {
    this.rejoining ??= this.doRejoin().finally(() => (this.rejoining = null));
    return this.rejoining;
  }

  private async doRejoin(): Promise<boolean> {
    const code = this.code;
    const seat = code ? loadSeat(this.store, code, this.slot) : null;
    if (!code || !seat || this.closedReason !== null) return false;
    this.clearRetry();
    try {
      if (!this.transport.connected) await this.transport.start();
      await this.transport.invoke("Rejoin", code, seat.playerId, seat.secret);
      this.me = seat.playerId;
      this.retryAttempt = 0;
      if (this.lobby) this.render();
      return true;
    } catch (err) {
      if (isServerError(err)) {
        // The room is gone or no longer knows this seat: start over from the Join form.
        clearSeat(this.store, code, this.slot);
        this.reset();
        this.set({ screen: "join", code, name: seat.name, error: serverMessage(err) });
        return false;
      }
      this.set({ screen: "connecting" });
      this.scheduleRetry();
      return true;
    }
  }

  private onConnectionLost(): void {
    if (this.closedReason !== null || !this.code || !loadSeat(this.store, this.code, this.slot)) return;
    this.set({ screen: "connecting" });
    this.scheduleRetry();
  }

  private scheduleRetry(): void {
    this.clearRetry();
    this.retryAttempt++;
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      if (!this.transport.connected) void this.rejoin();
    }, backoffDelays(this.retryAttempt));
  }

  private clearRetry(): void {
    if (this.retryTimer !== null) clearTimeout(this.retryTimer);
    this.retryTimer = null;
  }

  private async lobbyCall(method: string, ...args: unknown[]): Promise<void> {
    try {
      this.lobbyError = undefined;
      await this.transport.invoke(method, ...args);
    } catch (err) {
      this.lobbyError = serverMessage(err);
      this.render();
    }
  }

  private onLobby(json: unknown): void {
    if (!isLobbyState(json)) return;
    if (json.selectedGameId && json.selectedGameId !== this.lobby?.selectedGameId) void loadModule(json.selectedGameId);
    this.lobby = json;
    this.lobbyError = undefined;
    if (json.inGame) this.results = null; // a new game started
    else this.view = null; // the game is over; its last view is stale
    this.render();
  }

  private onView(json: unknown): void {
    try {
      this.view = parseControllerView(json);
    } catch (err) {
      console.warn("Ignoring a view this app can't show", err);
      return;
    }
    this.render();
  }

  private onGameEnded(json: unknown): void {
    if (!isGameResults(json)) return;
    this.results = json;
    this.view = null;
    this.render();
  }

  private close(reason: string): void {
    if (this.code) clearSeat(this.store, this.code, this.slot);
    this.reset();
    this.closedReason = reason;
    this.set({ screen: "closed", reason });
    void this.transport.stop().catch(() => {});
  }

  private reset(): void {
    this.clearRetry();
    this.me = null;
    this.lobby = null;
    this.view = null;
    this.results = null;
    this.closedReason = null;
    this.lobbyError = undefined;
    this.retryAttempt = 0;
  }

  private render(): void {
    if (this.closedReason !== null) return this.set({ screen: "closed", reason: this.closedReason });
    const { me, lobby } = this;
    if (!me || !lobby) return; // still joining
    if (this.results) return this.set({ screen: "results", results: this.results, lobby, me });
    if (lobby.inGame && this.view) return this.set({ screen: "game", view: this.view, lobby, me });
    this.set(this.lobbyError ? { screen: "lobby", lobby, me, error: this.lobbyError } : { screen: "lobby", lobby, me });
  }

  private set(state: SessionState): void {
    this.state = state;
    for (const fn of this.listeners) fn(state);
  }
}
