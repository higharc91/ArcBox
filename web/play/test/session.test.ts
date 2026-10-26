import { beforeEach, describe, expect, it } from "vitest";
import { Session, serverMessage } from "../src/session/session";
import { backoffDelays } from "../src/session/backoff";
import { loadSeat, saveSeat, storageKey, type KeyValueStore } from "../src/session/storage";
import type { ControllerView, GameResults, LobbyState } from "../src/protocol";
import lobbyFixture from "./fixtures/lobby.json";
import waitFixture from "./fixtures/wait.json";
import resultsFixture from "./fixtures/results.json";
import { FakeTransport, flush, hubError } from "./fakeTransport";

class MapStore implements KeyValueStore {
  map = new Map<string, string>();
  getItem(k: string) { return this.map.get(k) ?? null; }
  setItem(k: string, v: string) { this.map.set(k, v); }
  removeItem(k: string) { this.map.delete(k); }
}

const lobby = (over: Partial<LobbyState> = {}): LobbyState => ({ ...(structuredClone(lobbyFixture) as LobbyState), lastResults: null, ...over });
const view = (over: Partial<ControllerView> = {}) => ({ ...(structuredClone(waitFixture) as ControllerView), ...over }) as ControllerView;
const results = structuredClone(resultsFixture) as GameResults;

let t: FakeTransport;
let store: MapStore;
let s: Session;

beforeEach(() => {
  t = new FakeTransport();
  store = new MapStore();
  s = new Session(t, store);
});

/** Join as p1 in room BCDF; like the real server, LobbyChanged arrives before the Join result. */
async function joined() {
  t.responders.Join = () => {
    t.fire("LobbyChanged", lobby());
    return { code: "BCDF", playerId: "p1", secret: "s3cret" };
  };
  await s.join("bcdf", "  ALEX ");
}

describe("serverMessage", () => {
  it("strips SignalR's wrapper from a HubException", () => {
    expect(serverMessage(hubError("Join", "Room not found."))).toBe("Room not found.");
    expect(serverMessage(hubError("Join", "That name is taken."))).toBe("That name is taken.");
  });
  it("keeps other errors readable", () => {
    expect(serverMessage(new Error("Failed to complete negotiation with the server: TypeError: Failed to fetch")))
      .toBe("Can't reach the server. Check your connection and try again.");
    expect(serverMessage(new Error("An unexpected error occurred invoking 'Join' on the server.")))
      .toBe("Something went wrong on the server.");
  });
});

describe("Session", () => {
  it("starts on the join screen", () => {
    expect(s.state).toEqual({ screen: "join" });
  });

  it("join stores seat and shows lobby on LobbyChanged", async () => {
    const seen: string[] = [];
    s.subscribe((st) => seen.push(st.screen));
    await joined();
    expect(t.starts).toBe(1);
    expect(t.invokes[0]).toEqual({ method: "Join", args: ["BCDF", "ALEX"] });
    expect(loadSeat(store, "BCDF")).toEqual({ playerId: "p1", secret: "s3cret", name: "ALEX" });
    expect(s.state).toMatchObject({ screen: "lobby", me: "p1", lobby: { code: "BCDF" } });
    expect(seen.at(-1)).toBe("lobby");

    t.fire("LobbyChanged", lobby({ canStart: false }));
    expect(s.state).toMatchObject({ screen: "lobby", lobby: { canStart: false } });
  });

  it("join failure keeps the form and shows the server's words", async () => {
    t.responders.Join = () => { throw hubError("Join", "Game in progress, join after this round."); };
    await s.join("BCDF", "ALEX");
    expect(s.state).toEqual({ screen: "join", code: "BCDF", name: "ALEX", error: "Game in progress, join after this round." });
    expect(loadSeat(store, "BCDF")).toBeNull();
  });

  it("ControllerView switches to game screen", async () => {
    await joined();
    t.fire("LobbyChanged", lobby({ inGame: true }));
    expect(s.state.screen).toBe("lobby");
    t.fire("ControllerView", view({ viewId: "v7" }));
    expect(s.state).toMatchObject({ screen: "game", view: { viewId: "v7" }, me: "p1" });
  });

  it("ignores a malformed ControllerView", async () => {
    await joined();
    t.fire("LobbyChanged", lobby({ inGame: true }));
    t.fire("ControllerView", { kind: "nope", viewId: "x" });
    expect(s.state.screen).toBe("lobby");
  });

  it("GameEnded shows results until next game starts", async () => {
    await joined();
    t.fire("LobbyChanged", lobby({ inGame: true }));
    t.fire("ControllerView", view());
    t.fire("GameEnded", results);
    t.fire("LobbyChanged", lobby({ inGame: false, lastResults: results }));
    expect(s.state).toMatchObject({ screen: "results", results: { message: "ALEX (NOR) wins the race!" } });
    t.fire("LobbyChanged", lobby({ inGame: false, canStart: false }));
    expect(s.state.screen).toBe("results");
    t.fire("LobbyChanged", lobby({ inGame: true }));
    expect(s.state.screen).toBe("lobby");
    t.fire("ControllerView", view({ viewId: "v9" }));
    expect(s.state).toMatchObject({ screen: "game", view: { viewId: "v9" } });
  });

  it("results can be dismissed back to the lobby", async () => {
    await joined();
    t.fire("GameEnded", results);
    expect(s.state.screen).toBe("results");
    s.dismissResults();
    expect(s.state.screen).toBe("lobby");
  });

  it("a stale view is dropped when the game ends", async () => {
    await joined();
    t.fire("LobbyChanged", lobby({ inGame: true }));
    t.fire("ControllerView", view());
    t.fire("LobbyChanged", lobby({ inGame: false }));
    s.dismissResults();
    expect(s.state.screen).toBe("lobby");
  });

  it("resume without a stored seat returns false and shows join with the code", async () => {
    expect(await s.resume("bcdf")).toBe(false);
    expect(s.state).toEqual({ screen: "join", code: "BCDF" });
    expect(t.starts).toBe(0);
  });

  it("resume with stored seat calls Rejoin", async () => {
    saveSeat(store, "BCDF", undefined, { playerId: "p1", secret: "s3cret", name: "ALEX" });
    t.responders.Rejoin = () => {
      t.fire("LobbyChanged", lobby({ inGame: true }));
      t.fire("ControllerView", view({ viewId: "v3" }));
    };
    const states: string[] = [];
    s.subscribe((st) => states.push(st.screen));
    expect(await s.resume("BCDF")).toBe(true);
    expect(states[0]).toBe("connecting");
    expect(t.invokes).toEqual([{ method: "Rejoin", args: ["BCDF", "p1", "s3cret"] }]);
    expect(s.state).toMatchObject({ screen: "game", view: { viewId: "v3" }, me: "p1" });
  });

  it("resume for a dead room clears seat and returns to join with code", async () => {
    saveSeat(store, "BCDF", undefined, { playerId: "p1", secret: "s3cret", name: "ALEX" });
    t.responders.Rejoin = () => { throw hubError("Rejoin", "Room not found."); };
    expect(await s.resume("BCDF")).toBe(false);
    expect(loadSeat(store, "BCDF")).toBeNull();
    expect(s.state).toEqual({ screen: "join", code: "BCDF", name: "ALEX", error: "Room not found." });
  });

  it("resume with a rejected secret also clears the seat", async () => {
    saveSeat(store, "BCDF", undefined, { playerId: "p1", secret: "old", name: "ALEX" });
    t.responders.Rejoin = () => { throw hubError("Rejoin", "Couldn't rejoin. Please join again."); };
    expect(await s.resume("BCDF")).toBe(false);
    expect(loadSeat(store, "BCDF")).toBeNull();
    expect(s.state).toMatchObject({ screen: "join", error: "Couldn't rejoin. Please join again." });
  });

  it("resume keeps the seat when the network is down", async () => {
    saveSeat(store, "BCDF", undefined, { playerId: "p1", secret: "s3cret", name: "ALEX" });
    t.failStart = new Error("Failed to complete negotiation with the server: TypeError: Failed to fetch");
    expect(await s.resume("BCDF")).toBe(true);
    expect(loadSeat(store, "BCDF")).not.toBeNull();
    expect(s.state).toMatchObject({ screen: "connecting" });

    // Coming back to the tab retries.
    t.failStart = null;
    t.responders.Rejoin = () => t.fire("LobbyChanged", lobby());
    await s.onVisible();
    expect(s.state).toMatchObject({ screen: "lobby", me: "p1" });
  });

  it("reconnected triggers Rejoin", async () => {
    await joined();
    t.fireClose(new Error("socket dropped"));
    t.responders.Rejoin = () => t.fire("LobbyChanged", lobby({ canStart: false }));
    t.fireReconnected();
    await flush();
    expect(t.methods()).toEqual(["Join", "Rejoin"]);
    expect(t.invokes[1]!.args).toEqual(["BCDF", "p1", "s3cret"]);
    expect(s.state).toMatchObject({ screen: "lobby", lobby: { canStart: false } });
  });

  it("onVisible restarts a dropped connection and rejoins, but leaves a live one alone", async () => {
    await joined();
    await s.onVisible();
    expect(t.starts).toBe(1);
    t.connected = false;
    await s.onVisible();
    expect(t.starts).toBe(2);
    expect(t.methods()).toEqual(["Join", "Rejoin"]);
  });

  it("RoomClosed clears seat and shows closed", async () => {
    await joined();
    t.fire("RoomClosed", "game stopped responding");
    expect(loadSeat(store, "BCDF")).toBeNull();
    expect(s.state).toEqual({ screen: "closed", reason: "game stopped responding" });
    await s.onVisible();
    expect(t.methods()).toEqual(["Join"]);
  });

  it("Kicked clears seat and shows closed", async () => {
    await joined();
    t.fire("Kicked");
    expect(loadSeat(store, "BCDF")).toBeNull();
    expect(s.state).toEqual({ screen: "closed", reason: "You were removed from the room." });
    s.leaveClosed();
    expect(s.state).toEqual({ screen: "join" });
  });

  it("send, selectGame and startGame call the hub", async () => {
    await joined();
    await s.send("v1", { pressed: "cruise" });
    await s.selectGame("redline");
    await s.startGame();
    expect(t.invokes.slice(1)).toEqual([
      { method: "SendInput", args: ["v1", { pressed: "cruise" }] },
      { method: "SelectGame", args: ["redline"] },
      { method: "StartGame", args: [] },
    ]);
  });

  it("a StartGame error shows on the lobby screen", async () => {
    await joined();
    t.responders.StartGame = () => { throw hubError("StartGame", "This game needs 2–8 players."); };
    await s.startGame();
    expect(s.state).toMatchObject({ screen: "lobby", error: "This game needs 2–8 players." });
    t.fire("LobbyChanged", lobby());
    expect(s.state).not.toHaveProperty("error");
  });

  it("slots namespace the stored seat", async () => {
    s = new Session(t, store, "2");
    await joined();
    expect(store.map.has("arcbox:BCDF:2")).toBe(true);
    expect(store.map.has("arcbox:BCDF")).toBe(false);
  });
});

describe("storage", () => {
  it("builds keys with an optional slot", () => {
    expect(storageKey("bcdf")).toBe("arcbox:BCDF");
    expect(storageKey("BCDF", "3")).toBe("arcbox:BCDF:3");
  });
  it("ignores corrupt entries", () => {
    const store = new MapStore();
    store.setItem("arcbox:BCDF", "{not json");
    expect(loadSeat(store, "BCDF")).toBeNull();
    store.setItem("arcbox:BCDF", JSON.stringify({ playerId: 1 }));
    expect(loadSeat(store, "BCDF")).toBeNull();
  });
});

describe("backoffDelays", () => {
  it("follows 0, 1, 2, 5, then every 10 seconds forever", () => {
    expect([0, 1, 2, 3, 4, 5, 6, 50].map(backoffDelays)).toEqual([0, 1000, 2000, 5000, 10000, 10000, 10000, 10000]);
  });
});
