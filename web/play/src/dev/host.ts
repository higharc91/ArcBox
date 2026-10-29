import { ClientMethods, isGameResults, isLobbyState, type CreateRoomResult, type GameResults, type LobbyState } from "../protocol";
import { isServerError, serverMessage } from "../session/session";
import type { KeyValueStore } from "../session/storage";
import type { Transport } from "../session/transport";

export interface HostState {
  code: string | null;
  lobby: LobbyState | null;
  /** The latest HostView: the hub sends (gameId, view) as two arguments. */
  hostView: { gameId: string; view: unknown } | null;
  results: GameResults | null;
  error: string | null;
  closed: string | null;
}

export const hostKey = (code: string) => `arcbox-host:${code}`;

/** A browser stand-in for the TV host app: creates or resumes a room and calls the host hub methods. */
export class HostClient {
  state: HostState = { code: null, lobby: null, hostView: null, results: null, error: null, closed: null };
  private listeners = new Set<(s: HostState) => void>();
  private token: string | null = null;

  constructor(
    private readonly transport: Transport,
    private readonly store: KeyValueStore,
  ) {
    transport.on(ClientMethods.LobbyChanged, (lobby: unknown) => {
      if (!isLobbyState(lobby)) return;
      this.update({ lobby, ...(lobby.inGame ? { results: null } : { hostView: null }) });
    });
    transport.on(ClientMethods.HostView, (gameId: unknown, view: unknown) => {
      if (typeof gameId === "string") this.update({ hostView: { gameId, view } });
    });
    transport.on(ClientMethods.GameEnded, (results: unknown) => {
      if (isGameResults(results)) this.update({ results, hostView: null });
    });
    transport.on(ClientMethods.RoomClosed, (reason: unknown) => this.update({ closed: String(reason ?? "Room closed") }));
    transport.onReconnected(() => void this.resume());
  }

  subscribe(fn: (s: HostState) => void): () => void {
    this.listeners.add(fn);
    return () => this.listeners.delete(fn);
  }

  /** Resumes `code` when a host token for it is stored, else (or when the room is gone) creates a new room. Returns the code. */
  async connect(code: string | null): Promise<string | null> {
    try {
      if (!this.transport.connected) await this.transport.start();
      const token = code ? this.store.getItem(hostKey(code)) : null;
      if (code && token) {
        try {
          await this.transport.invoke("ResumeHost", code, token);
          this.token = token;
          this.update({ code, error: null });
          return code;
        } catch (err) {
          if (!isServerError(err)) throw err;
          this.store.removeItem(hostKey(code));
        }
      }
      const room = await this.transport.invoke<CreateRoomResult>("CreateRoom");
      this.token = room.hostToken;
      this.store.setItem(hostKey(room.code), room.hostToken);
      this.update({ code: room.code, error: null });
      return room.code;
    } catch (err) {
      this.update({ error: serverMessage(err) });
      return null;
    }
  }

  selectGame = (gameId: string) => this.call("SelectGame", gameId);
  startGame = () => this.call("StartGame");
  returnToLobby = () => this.call("ReturnToLobby");
  kick = (playerId: string) => this.call("Kick", playerId);
  dismissResults = () => this.update({ results: null });

  async onVisible(): Promise<void> {
    if (!this.transport.connected) await this.resume();
  }

  private async resume(): Promise<void> {
    const { code } = this.state;
    if (!code || !this.token) return;
    try {
      if (!this.transport.connected) await this.transport.start();
      await this.transport.invoke("ResumeHost", code, this.token);
    } catch (err) {
      if (!isServerError(err)) {
        this.update({ error: serverMessage(err) });
        return;
      }
      // The server no longer knows the room (it restarted, or swept the room): host a fresh one.
      // The dev page follows `code`, so its phones re-mount and join the new room.
      this.store.removeItem(hostKey(code));
      this.token = null;
      this.update({ lobby: null, hostView: null, results: null, closed: null });
      await this.connect(null);
    }
  }

  private async call(method: string, ...args: unknown[]): Promise<void> {
    try {
      this.update({ error: null });
      await this.transport.invoke(method, ...args);
    } catch (err) {
      this.update({ error: serverMessage(err) });
    }
  }

  private update(patch: Partial<HostState>): void {
    this.state = { ...this.state, ...patch };
    for (const fn of this.listeners) fn(this.state);
  }
}
