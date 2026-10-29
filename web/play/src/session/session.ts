import {
  ClientMethods,
  isGameResults,
  isLobbyState,
  isRoomPeek,
  parseControllerView,
  type ControllerView,
  type GameResults,
  type JoinResult,
  type LobbyState,
  type PlayerId,
  type ProfileKey,
  type RoomPeek,
  type SeatOffer,
  type SeatRef,
  type TakeSeatRequest,
} from "../protocol";
import { loadModule } from "../games/registry";
import { backoffDelays } from "./backoff";
import { clearSeat, loadProfileKeys, loadSeat, saveProfileKey, saveSeat, type KeyValueStore, type StoredProfile } from "./storage";
import type { Transport } from "./transport";

export type { Transport } from "./transport";

export type SessionState =
  | { screen: "join"; code?: string; name?: string; error?: string; busy?: boolean }
  | { screen: "connecting" }
  | { screen: "pick"; peek: RoomPeek; showAll?: boolean; error?: string; busy?: boolean }
  | { screen: "lobby"; lobby: LobbyState; me: PlayerId; error?: string }
  | { screen: "game"; view: ControllerView; lobby: LobbyState; me: PlayerId }
  | { screen: "results"; results: GameResults; lobby: LobbyState; me: PlayerId }
  | { screen: "closed"; reason: string };

const OFFLINE = "Can't reach the server. Check your connection and try again.";
const KICKED = "You were removed from the room.";
const IN_PROGRESS = "Game in progress, join after this round.";
const BAD_PEEK = "This room sent something this app can't read. Reload and try again.";

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

/** What a Peek answer means for the screen: the picker, the name box, or "nothing to take, the game is running". */
function routeOf(peek: RoomPeek): "pick" | "name" | "in-progress" {
  if (peek.inGame && peek.offers.length === 0 && !peek.suggested) return "in-progress";
  if (peek.suggested || peek.campaignName != null || (peek.inGame && peek.offers.length > 0)) return "pick";
  return "name";
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
  private touchPending = false;
  private inFlight = new Set<string>();

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

  /**
   * Ask the room what this phone may take (`Peek`, with the profile keys it holds) and show the picker, or the name box
   * when there is nothing to pick. `error` is carried onto whichever screen results (a rejoin that just failed).
   * `name` is what the player already typed on the Join form: kept if the picker or an error follows, and used to join
   * in one step when the room turns out to be a plain lobby.
   */
  async open(code: string, error?: string, name?: string): Promise<void> {
    code = code.trim().toUpperCase();
    if (name !== undefined && this.state.screen === "join" && this.state.busy) return; // a double tap on Join
    this.reset();
    this.code = code;
    this.set(name !== undefined ? { screen: "join", code, name, busy: true } : { screen: "connecting" });
    try {
      if (!this.transport.connected) await this.transport.start();
      const peek = await this.transport.invoke<RoomPeek>("Peek", { code, profileKeys: this.keys() });
      if (name && !error && isRoomPeek(peek) && routeOf(peek) === "name") return await this.joinAs(name, code);
      this.showPeek(code, peek, error, false, name);
    } catch (err) {
      this.set({ screen: "join", code, ...(name !== undefined ? { name } : {}), error: serverMessage(err) });
    }
  }

  /** The Join form's submit: look at the room first (a game in progress has offline seats to take), then join. */
  joinRoom(code: string, name: string): Promise<void> {
    return this.open(code, undefined, name.trim());
  }

  /** From "Not you? Show everyone": the full list of offers instead of the remembered one. */
  showEveryone(): void {
    if (this.state.screen === "pick") this.set({ ...this.state, showAll: true });
  }

  /** Take an offered seat: an offline seat, or a campaign member who has none yet. */
  async takeSeat(seat: SeatRef): Promise<void> {
    const from = this.state;
    if (from.screen === "pick" && from.busy) return; // a double tap
    const code = this.code ?? (from.screen === "pick" ? from.peek.code : undefined);
    if (!code) return;
    // Exactly one of slot / member goes on the wire.
    const ref: SeatRef = seat.slot ? { slot: seat.slot } : { member: seat.member };
    const same = (o: SeatOffer | null) => o !== null && (o.ref.slot ?? null) === (seat.slot ?? null) && (o.ref.member ?? null) === (seat.member ?? null);
    const offered = from.screen === "pick" ? [...from.peek.offers, from.peek.suggested].find(same) : undefined;
    await this.enter({ code, seat: ref }, offered?.card.title ?? "", from, true);
  }

  /**
   * "I'm new here" or the plain name box: TakeSeat under a new name. `code` is for the Join form, where the player typed
   * it; from the picker the room is already known. On failure the form keeps its values and shows the server's message.
   */
  async joinNew(name: string, code?: string): Promise<void> {
    const from = this.state;
    if ((from.screen === "pick" || from.screen === "join") && from.busy) return; // a double tap
    await this.joinAs(name, code);
  }

  private async joinAs(name: string, code?: string): Promise<void> {
    const from = this.state;
    code = (code ?? this.code ?? (from.screen === "pick" ? from.peek.code : from.screen === "join" ? from.code : undefined))?.trim().toUpperCase();
    if (!code) return;
    name = name.trim();
    await this.enter({ code, newName: name }, name, from, false);
  }

  /** Seated in a campaign lobby: say which member you are. A null member is "I'm new here", under `newName` when given. */
  async claimMember(member: SeatRef | null, newName?: string): Promise<void> {
    const request: { member: SeatRef | null; profileKeys: string[]; newName?: string } = {
      member: member && (member.slot ? { slot: member.slot } : { member: member.member }),
      profileKeys: this.keys(),
    };
    if (member === null && newName?.trim()) request.newName = newName.trim();
    await this.lobbyCall("ClaimMember", request, (key: ProfileKey | null) => {
      if (key) return this.remember(key);
      const claimed = member?.member ?? this.lobby?.campaign?.roster.find((r) => r.seatedAs === member?.slot)?.profile;
      if (claimed) this.touchSeated(claimed);
    });
  }

  selectCampaign(id: string): Promise<void> {
    return this.lobbyCall("SelectCampaign", id);
  }

  /** VIP only: a new campaign under this name, which makes this player's profile its owner. */
  createCampaign(name: string): Promise<void> {
    return this.lobbyCall("CreateCampaign", name.trim(), (key: ProfileKey | null) => this.remember(key));
  }

  /** The profile keys this phone holds, most recently used first (the picker shows their names). */
  profiles(): StoredProfile[] {
    return loadProfileKeys(this.store, this.slot);
  }

  /**
   * Rejoin a room this browser already has a seat in. Returns false when there is no seat (and shows Join with the
   * code), or when the server no longer knows it (the stale seat is cleared and the picker is shown, once); true while
   * the seat is still live, even if the network is down for now.
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
        // The room is gone, or no longer knows this seat (someone took it): forget it and ask the room who this phone
        // can be. Never Rejoin again: with the seat cleared nothing here retries.
        clearSeat(this.store, code, this.slot);
        await this.open(code, serverMessage(err));
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

  private keys(): string[] {
    return this.profiles().map((p) => p.key);
  }

  private remember(key: ProfileKey | null | undefined): void {
    if (key) saveProfileKey(this.store, this.slot, key, Date.now());
  }

  /**
   * The server issues no new key when this phone already holds one for the profile it just became, so mark the stored
   * entry as used now (it is then the one suggested next time). With the profile not known yet, the lobby supplies it.
   */
  private touchSeated(profile: string | null | undefined): void {
    const stored = profile ? this.profiles().find((p) => p.profileId === profile) : undefined;
    if (stored) saveProfileKey(this.store, this.slot, { id: stored.profileId, key: stored.key, name: stored.name }, Date.now());
    this.touchPending = !profile;
  }

  /** Where `Peek` says to go: the picker, or the name box when there is nothing to pick from. */
  private showPeek(code: string, peek: RoomPeek, error?: string, showAll?: boolean, name?: string): void {
    const typed = name !== undefined ? { name } : {};
    if (!isRoomPeek(peek)) return this.set({ screen: "join", code, ...typed, error: BAD_PEEK });
    switch (routeOf(peek)) {
      case "in-progress":
        return this.set({ screen: "join", code, ...typed, error: error ?? IN_PROGRESS });
      case "pick": {
        const state: SessionState = { screen: "pick", peek };
        if (showAll) state.showAll = true;
        if (error) state.error = error;
        return this.set(state);
      }
      case "name":
        return this.set({ screen: "join", code, ...typed, ...(error ? { error } : {}) });
    }
  }

  /** TakeSeat, then wait for the lobby: shared by taking an offered seat and joining under a new name. */
  private async enter(request: Omit<TakeSeatRequest, "profileKeys">, name: string, from: SessionState, refresh: boolean): Promise<void> {
    const { code } = request;
    const full: TakeSeatRequest = { ...request, profileKeys: this.keys() };
    const peek = from.screen === "pick" ? from.peek : null;
    this.reset();
    this.code = code;
    this.set(peek ? { screen: "pick", peek, ...(from.screen === "pick" && from.showAll ? { showAll: true } : {}), busy: true } : { screen: "join", code, name, busy: true });
    try {
      if (!this.transport.connected) await this.transport.start();
      const result = await this.transport.invoke<JoinResult>("TakeSeat", full);
      this.code = result.code;
      this.me = result.playerId;
      saveSeat(this.store, result.code, this.slot, { playerId: result.playerId, secret: result.secret, name: result.profile?.name ?? name });
      if (result.profile) this.remember(result.profile);
      else this.touchSeated(request.seat?.member ?? this.lobby?.players.find((p) => p.id === result.playerId)?.profile);
      if (this.lobby) this.render();
      else this.set({ screen: "connecting" });
    } catch (err) {
      const message = serverMessage(err);
      if (peek && refresh && isServerError(err)) {
        // The offer may be stale (someone took the seat first): show what is on offer now, with the reason.
        try {
          return this.showPeek(code, await this.transport.invoke<RoomPeek>("Peek", { code, profileKeys: this.keys() }), message,
            from.screen === "pick" && from.showAll);
        } catch {
          // Fall through to the old list.
        }
      }
      if (peek) this.set({ screen: "pick", peek, ...(from.screen === "pick" && from.showAll ? { showAll: true } : {}), error: message });
      else this.set({ screen: "join", code, name, error: message });
    }
  }

  private async lobbyCall<T = unknown>(method: string, arg?: unknown, onResult?: (result: T) => void): Promise<void> {
    if (this.inFlight.has(method)) return; // a double tap
    this.inFlight.add(method);
    try {
      this.lobbyError = undefined;
      const args = arg === undefined ? [] : [arg];
      const result = await this.transport.invoke<T>(method, ...args);
      onResult?.(result);
    } catch (err) {
      this.lobbyError = serverMessage(err);
      this.render();
    } finally {
      this.inFlight.delete(method);
    }
  }

  private onLobby(json: unknown): void {
    if (!isLobbyState(json)) return;
    if (json.selectedGameId && json.selectedGameId !== this.lobby?.selectedGameId) void loadModule(json.selectedGameId);
    this.lobby = json;
    if (this.touchPending && this.me) {
      // Seated before the lobby said which profile this player is.
      this.touchPending = false;
      this.touchSeated(json.players.find((p) => p.id === this.me)?.profile);
    }
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
    this.touchPending = false;
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
