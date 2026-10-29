import { beforeEach, describe, expect, it } from "vitest";
import { Session, serverMessage } from "../src/session/session";
import { backoffDelays } from "../src/session/backoff";
import { loadProfileKeys, loadSeat, saveProfileKey, saveSeat, storageKey, type KeyValueStore } from "../src/session/storage";
import { boot } from "../src/App";
import { isRoomPeek, type ControllerView, type GameResults, type LobbyState, type RoomPeek, type SeatOffer } from "../src/protocol";
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

/** Join as p1 in room BCDF; like the real server, LobbyChanged arrives before the TakeSeat result. */
async function joined() {
  t.responders.TakeSeat = () => {
    t.fire("LobbyChanged", lobby());
    return { code: "BCDF", playerId: "p1", secret: "s3cret" };
  };
  await s.joinNew("  ALEX ", "bcdf");
}

const offer = (over: Partial<SeatOffer> = {}): SeatOffer => ({
  ref: { slot: null, member: "pr_dave" },
  card: { title: "Aldric", subtitle: "Dwarf Cleric · Lv 3", color: null, portrait: null },
  absent: false,
  ...over,
});
const peek = (over: Partial<RoomPeek> = {}): RoomPeek => ({
  code: "BCDF", gameId: "tactics", campaignName: "Embers of the Vale", inGame: false,
  offers: [offer()], suggested: null, canJoinNew: true, ...over,
});
const seatP1 = () => saveSeat(store, "BCDF", undefined, { playerId: "p1", secret: "s3cret", name: "ALEX" });

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
    expect(t.invokes[0]).toEqual({ method: "TakeSeat", args: [{ code: "BCDF", newName: "ALEX", profileKeys: [] }] });
    expect(loadSeat(store, "BCDF")).toEqual({ playerId: "p1", secret: "s3cret", name: "ALEX" });
    expect(s.state).toMatchObject({ screen: "lobby", me: "p1", lobby: { code: "BCDF" } });
    expect(seen.at(-1)).toBe("lobby");

    t.fire("LobbyChanged", lobby({ canStart: false }));
    expect(s.state).toMatchObject({ screen: "lobby", lobby: { canStart: false } });
  });

  it("join failure keeps the form and shows the server's words", async () => {
    t.responders.TakeSeat = () => { throw hubError("TakeSeat", "Game in progress, join after this round."); };
    await s.joinNew("ALEX", "BCDF");
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

  it("resume for a dead room clears seat and shows join with the server's words", async () => {
    seatP1();
    t.responders.Rejoin = () => { throw hubError("Rejoin", "Room not found."); };
    t.responders.Peek = () => { throw hubError("Peek", "Room not found."); };
    expect(await s.resume("BCDF")).toBe(false);
    expect(loadSeat(store, "BCDF")).toBeNull();
    expect(s.state).toEqual({ screen: "join", code: "BCDF", error: "Room not found." });
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
    expect(t.methods()).toEqual(["TakeSeat", "Rejoin"]);
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
    expect(t.methods()).toEqual(["TakeSeat", "Rejoin"]);
  });

  it("RoomClosed clears seat and shows closed", async () => {
    await joined();
    t.fire("RoomClosed", "game stopped responding");
    expect(loadSeat(store, "BCDF")).toBeNull();
    expect(s.state).toEqual({ screen: "closed", reason: "game stopped responding" });
    await s.onVisible();
    expect(t.methods()).toEqual(["TakeSeat"]);
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

describe("profile keys in storage", () => {
  const key = (id: string, k = "k-" + id, name = id) => ({ id, key: k, name });

  it("saves under arcbox:profiles, or arcbox:profiles:<slot>, most recent first", () => {
    saveProfileKey(store, undefined, key("pr_a"), 100);
    saveProfileKey(store, undefined, key("pr_b"), 200);
    saveProfileKey(store, "2", key("pr_c"), 300);
    expect(loadProfileKeys(store).map((p) => p.profileId)).toEqual(["pr_b", "pr_a"]);
    expect(loadProfileKeys(store, "2").map((p) => p.profileId)).toEqual(["pr_c"]);
    expect(store.map.has("arcbox:profiles")).toBe(true);
    expect(store.map.has("arcbox:profiles:2")).toBe(true);
  });

  it("replaces the entry for the same profile and sets lastUsed", () => {
    saveProfileKey(store, undefined, key("pr_a", "old"), 100);
    saveProfileKey(store, undefined, key("pr_b"), 200);
    saveProfileKey(store, undefined, key("pr_a", "new", "Dave"), 300);
    expect(loadProfileKeys(store)).toEqual([
      { profileId: "pr_a", key: "new", name: "Dave", lastUsed: 300 },
      { profileId: "pr_b", key: "k-pr_b", name: "pr_b", lastUsed: 200 },
    ]);
  });

  it("reads corrupt storage as no keys", () => {
    store.setItem("arcbox:profiles", "{nope");
    expect(loadProfileKeys(store)).toEqual([]);
    store.setItem("arcbox:profiles", JSON.stringify([{ profileId: 1 }, { profileId: "pr_a", key: "k", name: "A", lastUsed: 1 }]));
    expect(loadProfileKeys(store).map((p) => p.profileId)).toEqual(["pr_a"]);
  });
});

describe("Session: the picker", () => {
  it("Boot_with_seat_rejoins_without_peek", async () => {
    seatP1();
    t.responders.Rejoin = () => t.fire("LobbyChanged", lobby());
    await boot(s, store, "?room=bcdf");
    expect(t.methods()).toEqual(["Rejoin"]);
    expect(s.state.screen).toBe("lobby");
  });

  it("Failed_rejoin_falls_back_to_peek_once", async () => {
    seatP1();
    t.responders.Rejoin = () => { throw hubError("Rejoin", "Couldn't rejoin. Please join again."); };
    t.responders.Peek = () => peek();
    await boot(s, store, "?room=BCDF");
    expect(t.methods()).toEqual(["Rejoin", "Peek"]);
    expect(loadSeat(store, "BCDF")).toBeNull();
    expect(s.state).toMatchObject({ screen: "pick", error: "Couldn't rejoin. Please join again.", peek: { code: "BCDF" } });

    // Coming back to the tab, or the socket reconnecting, doesn't retry the rejoin.
    t.connected = false;
    await s.onVisible();
    t.fireReconnected();
    await flush();
    expect(t.methods()).toEqual(["Rejoin", "Peek"]);
  });

  it("a rejoin that fails on the network keeps the seat and retries as before", async () => {
    seatP1();
    t.failStart = new Error("Failed to fetch");
    await boot(s, store, "?room=BCDF");
    expect(t.methods()).toEqual([]);
    expect(loadSeat(store, "BCDF")).not.toBeNull();
    expect(s.state.screen).toBe("connecting");
    s.dispose();
  });

  it("Boot_without_seat_peeks_with_stored_keys", async () => {
    saveProfileKey(store, undefined, { id: "pr_old", key: "k-old", name: "Old" }, 100);
    saveProfileKey(store, undefined, { id: "pr_new", key: "k-new", name: "Dave" }, 200);
    t.responders.Peek = () => peek({ suggested: offer() });
    await boot(s, store, "?room=bcdf");
    expect(t.invokes).toEqual([{ method: "Peek", args: [{ code: "BCDF", profileKeys: ["k-new", "k-old"] }] }]);
    expect(s.state).toMatchObject({ screen: "pick", peek: { suggested: { card: { title: "Aldric" } } } });
    expect(t.starts).toBe(1);
  });

  it("a plain lobby with nobody to pick shows the name box, with the code", async () => {
    t.responders.Peek = () => peek({ campaignName: null, offers: [] });
    await s.open("bcdf");
    expect(s.state).toEqual({ screen: "join", code: "BCDF" });
  });

  it("mid-game with no offers shows the Join screen with the server's message", async () => {
    t.responders.Peek = () => peek({ inGame: true, offers: [], canJoinNew: false });
    await s.open("BCDF");
    expect(s.state).toEqual({ screen: "join", code: "BCDF", error: "Game in progress, join after this round." });
  });

  it("mid-game with offline seats shows the picker", async () => {
    t.responders.Peek = () => peek({ campaignName: null, inGame: true, canJoinNew: false });
    await s.open("BCDF");
    expect(s.state.screen).toBe("pick");
  });

  it("a Peek failure shows the server's words on Join", async () => {
    t.responders.Peek = () => { throw hubError("Peek", "Room not found."); };
    await s.open("BCDF");
    expect(s.state).toEqual({ screen: "join", code: "BCDF", error: "Room not found." });
  });

  it("TakeSeat_saves_seat_and_profile_key", async () => {
    t.responders.Peek = () => peek();
    t.responders.TakeSeat = () => {
      t.fire("LobbyChanged", lobby());
      return { code: "BCDF", playerId: "p4", secret: "fresh", profile: { id: "pr_dave", key: "key-dave", name: "Dave" } };
    };
    await s.open("BCDF");
    const before = Date.now();
    await s.takeSeat({ slot: null, member: "pr_dave" });
    expect(t.invokes.at(-1)).toEqual({
      method: "TakeSeat",
      args: [{ code: "BCDF", seat: { member: "pr_dave" }, profileKeys: [] }], // exactly one of slot / member
    });
    expect(loadSeat(store, "BCDF")).toEqual({ playerId: "p4", secret: "fresh", name: "Dave" });
    const [saved] = loadProfileKeys(store);
    expect(saved).toMatchObject({ profileId: "pr_dave", key: "key-dave", name: "Dave" });
    expect(saved!.lastUsed).toBeGreaterThanOrEqual(before);
    expect(s.state).toMatchObject({ screen: "lobby", me: "p4" });
  });

  it("taking an offline seat by slot sends only the slot, and saves no key when none comes back", async () => {
    t.responders.Peek = () => peek({ campaignName: null, inGame: true });
    t.responders.TakeSeat = () => {
      t.fire("LobbyChanged", lobby({ inGame: true }));
      return { code: "BCDF", playerId: "p2", secret: "s2" };
    };
    await s.open("BCDF");
    await s.takeSeat({ slot: "p2", member: null });
    expect(t.invokes.at(-1)!.args).toEqual([{ code: "BCDF", seat: { slot: "p2" }, profileKeys: [] }]);
    expect(loadSeat(store, "BCDF")).toMatchObject({ playerId: "p2", secret: "s2" });
    expect(loadProfileKeys(store)).toEqual([]);
  });

  it("a refused TakeSeat shows the message on a fresh Peek", async () => {
    let n = 0;
    t.responders.Peek = () => (++n === 1 ? peek() : peek({ offers: [] }));
    t.responders.TakeSeat = () => { throw hubError("TakeSeat", "Someone's already playing as Aldric."); };
    await s.open("BCDF");
    await s.takeSeat({ slot: null, member: "pr_dave" });
    expect(t.methods()).toEqual(["Peek", "TakeSeat", "Peek"]);
    expect(s.state).toMatchObject({ screen: "pick", error: "Someone's already playing as Aldric.", peek: { offers: [] } });
    expect(loadSeat(store, "BCDF")).toBeNull();
  });

  it("JoinNew_uses_TakeSeat_with_NewName", async () => {
    saveProfileKey(store, undefined, { id: "pr_a", key: "k-a", name: "A" }, 1);
    t.responders.Peek = () => peek();
    t.responders.TakeSeat = () => {
      t.fire("LobbyChanged", lobby());
      return { code: "BCDF", playerId: "p5", secret: "s5", profile: { id: "pr_new", key: "k-new", name: "Dan" } };
    };
    await s.open("BCDF");
    await s.joinNew("  Dan ");
    expect(t.invokes.at(-1)).toEqual({ method: "TakeSeat", args: [{ code: "BCDF", newName: "Dan", profileKeys: ["k-a"] }] });
    expect(loadProfileKeys(store).map((p) => p.profileId)).toEqual(["pr_new", "pr_a"]);
    expect(s.state).toMatchObject({ screen: "lobby", me: "p5" });
  });

  it("a refused JoinNew from the picker stays on the picker with the message", async () => {
    t.responders.Peek = () => peek();
    t.responders.TakeSeat = () => { throw hubError("TakeSeat", "That name is taken."); };
    await s.open("BCDF");
    await s.joinNew("Dan");
    expect(s.state).toMatchObject({ screen: "pick", error: "That name is taken.", peek: { code: "BCDF" } });
  });

  it("Dev_devName_joins_new", async () => {
    s = new Session(t, store, "1");
    t.responders.TakeSeat = () => {
      t.fire("LobbyChanged", lobby());
      return { code: "BCDF", playerId: "p1", secret: "s" };
    };
    await boot(s, store, "?room=BCDF&slot=1&devName=P1");
    expect(t.invokes).toEqual([{ method: "TakeSeat", args: [{ code: "BCDF", newName: "P1", profileKeys: [] }] }]);
    expect(loadSeat(store, "BCDF", "1")).not.toBeNull();
  });

  it("showEveryone flips the picker to the full list", async () => {
    t.responders.Peek = () => peek({ suggested: offer() });
    await s.open("BCDF");
    expect(s.state).not.toHaveProperty("showAll");
    s.showEveryone();
    expect(s.state).toMatchObject({ screen: "pick", showAll: true });
  });

  it("claimMember sends the keys and newName only for I'm new here, and saves the key", async () => {
    await joined();
    saveProfileKey(store, undefined, { id: "pr_a", key: "k-a", name: "A" }, 1);
    t.responders.ClaimMember = () => ({ id: "pr_n", key: "k-n", name: "Dan" });
    await s.claimMember(null, "  Dan ");
    expect(t.invokes.at(-1)).toEqual({ method: "ClaimMember", args: [{ member: null, profileKeys: ["k-a"], newName: "Dan" }] });
    expect(loadProfileKeys(store)[0]).toMatchObject({ profileId: "pr_n", key: "k-n", name: "Dan" });

    t.responders.ClaimMember = () => null;
    await s.claimMember({ slot: null, member: "pr_dave" });
    expect(t.invokes.at(-1)).toEqual({ method: "ClaimMember", args: [{ member: { member: "pr_dave" }, profileKeys: ["k-n", "k-a"] }] });
    await s.claimMember(null);
    expect(t.invokes.at(-1)!.args).toEqual([{ member: null, profileKeys: ["k-n", "k-a"] }]);
  });

  it("claimMember shows the server's refusal on the lobby", async () => {
    await joined();
    t.responders.ClaimMember = () => { throw hubError("ClaimMember", "Someone's already playing as Aldric."); };
    await s.claimMember({ slot: "p2", member: null });
    expect(s.state).toMatchObject({ screen: "lobby", error: "Someone's already playing as Aldric." });
  });

  it("selectCampaign and createCampaign call the hub; createCampaign saves the key", async () => {
    await joined();
    await s.selectCampaign("c1");
    t.responders.CreateCampaign = () => ({ id: "pr_dave", key: "k-d", name: "Dave" });
    await s.createCampaign("  Embers  ");
    expect(t.invokes.slice(1)).toEqual([
      { method: "SelectCampaign", args: ["c1"] },
      { method: "CreateCampaign", args: ["Embers"] },
    ]);
    expect(loadProfileKeys(store)[0]).toMatchObject({ profileId: "pr_dave", key: "k-d" });
  });

  it("a createCampaign refusal shows on the lobby", async () => {
    await joined();
    t.responders.CreateCampaign = () => { throw hubError("CreateCampaign", "Only the VIP can create a campaign."); };
    await s.createCampaign("Embers");
    expect(s.state).toMatchObject({ screen: "lobby", error: "Only the VIP can create a campaign." });
  });

  // ---------------------------------------------------------------- the Join form

  it("joinRoom on a plain lobby peeks, then joins in one step with the typed name", async () => {
    t.responders.Peek = () => peek({ campaignName: null, offers: [] });
    t.responders.TakeSeat = () => {
      t.fire("LobbyChanged", lobby());
      return { code: "BCDF", playerId: "p1", secret: "s" };
    };
    await s.joinRoom("bcdf", " ALEX ");
    expect(t.invokes).toEqual([
      { method: "Peek", args: [{ code: "BCDF", profileKeys: [] }] },
      { method: "TakeSeat", args: [{ code: "BCDF", newName: "ALEX", profileKeys: [] }] },
    ]);
    expect(s.state).toMatchObject({ screen: "lobby", me: "p1" });
  });

  it("joinRoom mid-game with offline seats shows the picker and takes no seat", async () => {
    t.responders.Peek = () => peek({ campaignName: null, inGame: true, canJoinNew: false });
    await s.joinRoom("BCDF", "ALEX");
    expect(t.methods()).toEqual(["Peek"]);
    expect(s.state).toMatchObject({ screen: "pick", peek: { inGame: true } });
  });

  it("joinRoom mid-game with nothing to take keeps the name and shows the server's words", async () => {
    t.responders.Peek = () => peek({ campaignName: null, inGame: true, offers: [], canJoinNew: false });
    await s.joinRoom("BCDF", "ALEX");
    expect(s.state).toEqual({ screen: "join", code: "BCDF", name: "ALEX", error: "Game in progress, join after this round." });
  });

  it("joinRoom keeps the typed values when the Peek fails", async () => {
    t.responders.Peek = () => { throw hubError("Peek", "Room not found."); };
    await s.joinRoom("BCDF", "ALEX");
    expect(s.state).toEqual({ screen: "join", code: "BCDF", name: "ALEX", error: "Room not found." });
  });

  // ---------------------------------------------------------------- double taps

  it("a second takeSeat or joinNew while one is in flight is ignored", async () => {
    t.responders.Peek = () => peek();
    let release!: () => void;
    const gate = new Promise<void>((r) => (release = r));
    t.responders.TakeSeat = async () => {
      await gate;
      t.fire("LobbyChanged", lobby());
      return { code: "BCDF", playerId: "p4", secret: "x" };
    };
    await s.open("BCDF");
    const first = s.takeSeat({ slot: null, member: "pr_dave" });
    await s.takeSeat({ slot: null, member: "pr_dave" });
    await s.joinNew("Dan");
    release();
    await first;
    expect(t.methods()).toEqual(["Peek", "TakeSeat"]);
  });

  it("a second Join submit while the first is in flight is ignored", async () => {
    let release!: () => void;
    const gate = new Promise<void>((r) => (release = r));
    t.responders.Peek = async () => { await gate; return peek({ campaignName: null, offers: [] }); };
    t.responders.TakeSeat = () => {
      t.fire("LobbyChanged", lobby());
      return { code: "BCDF", playerId: "p1", secret: "s" };
    };
    const first = s.joinRoom("BCDF", "ALEX");
    await s.joinRoom("BCDF", "ALEX");
    release();
    await first;
    expect(t.methods()).toEqual(["Peek", "TakeSeat"]);
  });

  it("a second claimMember or createCampaign while one is in flight is ignored, and the next works", async () => {
    await joined();
    let release!: () => void;
    const gate = new Promise<void>((r) => (release = r));
    t.responders.ClaimMember = async () => { await gate; return null; };
    const first = s.claimMember({ slot: null, member: "pr_dave" });
    await s.claimMember({ slot: null, member: "pr_dave" });
    release();
    await first;
    await s.claimMember({ slot: null, member: "pr_dave" });
    expect(t.methods().filter((m) => m === "ClaimMember")).toHaveLength(2);
  });

  // ---------------------------------------------------------------- a Peek the phone can't read

  it("a malformed Peek shows a clear error instead of throwing", async () => {
    for (const bad of [undefined, null, {}, { ...peek(), offers: "none" }, { ...peek(), offers: [{ ref: {}, card: {}, absent: false }] }, { ...peek(), inGame: "no" }]) {
      t.responders.Peek = () => bad;
      await s.open("BCDF");
      expect(s.state).toEqual({ screen: "join", code: "BCDF", error: "This room sent something this app can't read. Reload and try again." });
    }
  });

  it("isRoomPeek accepts a real Peek, with optional fields absent or null", () => {
    expect(isRoomPeek(peek())).toBe(true);
    expect(isRoomPeek({ code: "BCDF", inGame: false, canJoinNew: true, offers: [] })).toBe(true);
    expect(isRoomPeek(peek({ suggested: offer() }))).toBe(true);
  });

  // ---------------------------------------------------------------- lastUsed

  it("taking a remembered member's seat bumps that stored key's lastUsed (the server issues no new key)", async () => {
    saveProfileKey(store, undefined, { id: "pr_dave", key: "k-dave", name: "Dave" }, 1);
    saveProfileKey(store, undefined, { id: "pr_other", key: "k-o", name: "Other" }, 2);
    t.responders.Peek = () => peek();
    t.responders.TakeSeat = () => {
      t.fire("LobbyChanged", lobby());
      return { code: "BCDF", playerId: "p4", secret: "x" };
    };
    await s.open("BCDF");
    const before = Date.now();
    await s.takeSeat({ slot: null, member: "pr_dave" });
    const stored = loadProfileKeys(store);
    expect(stored.map((p) => p.profileId)).toEqual(["pr_dave", "pr_other"]);
    expect(stored[0]!.lastUsed).toBeGreaterThanOrEqual(before);
    expect(stored[0]).toMatchObject({ key: "k-dave", name: "Dave" });
  });

  it("taking an offline seat by slot bumps the key of the profile the lobby says is on it", async () => {
    saveProfileKey(store, undefined, { id: "pr_dave", key: "k-dave", name: "Dave" }, 1);
    saveProfileKey(store, undefined, { id: "pr_other", key: "k-o", name: "Other" }, 2);
    t.responders.Peek = () => peek({ campaignName: null, inGame: true });
    t.responders.TakeSeat = () => {
      t.fire("LobbyChanged", lobby({ players: [{ id: "p2", name: "Dave", isVip: false, connected: true, profile: "pr_dave" }] }));
      return { code: "BCDF", playerId: "p2", secret: "x" };
    };
    await s.open("BCDF");
    await s.takeSeat({ slot: "p2", member: null });
    expect(loadProfileKeys(store).map((p) => p.profileId)).toEqual(["pr_dave", "pr_other"]);
  });

  it("when the lobby arrives after the TakeSeat result, its profile is used then", async () => {
    saveProfileKey(store, undefined, { id: "pr_dave", key: "k-dave", name: "Dave" }, 1);
    saveProfileKey(store, undefined, { id: "pr_other", key: "k-o", name: "Other" }, 2);
    t.responders.Peek = () => peek({ campaignName: null, inGame: true });
    t.responders.TakeSeat = () => ({ code: "BCDF", playerId: "p2", secret: "x" });
    await s.open("BCDF");
    await s.takeSeat({ slot: "p2", member: null });
    expect(loadProfileKeys(store)[0]!.profileId).toBe("pr_other");
    t.fire("LobbyChanged", lobby({ players: [{ id: "p2", name: "Dave", isVip: false, connected: true, profile: "pr_dave" }] }));
    expect(loadProfileKeys(store).map((p) => p.profileId)).toEqual(["pr_dave", "pr_other"]);
  });

  it("a ClaimMember that returns no key bumps the claimed member's stored key", async () => {
    await joined();
    saveProfileKey(store, undefined, { id: "pr_dave", key: "k-dave", name: "Dave" }, 1);
    saveProfileKey(store, undefined, { id: "pr_other", key: "k-o", name: "Other" }, 2);
    t.responders.ClaimMember = () => null;
    await s.claimMember({ slot: null, member: "pr_dave" });
    expect(loadProfileKeys(store).map((p) => p.profileId)).toEqual(["pr_dave", "pr_other"]);

    saveProfileKey(store, undefined, { id: "pr_third", key: "k-t", name: "Third" }, 5);
    t.fire("LobbyChanged", lobby({ campaign: { id: "c", code: "EMBR", name: "Embers", roster: [{ profile: "pr_other", card: { title: "Kara", subtitle: null, color: null, portrait: null }, seatedAs: "p7", connected: false }] } }));
    await s.claimMember({ slot: "p7", member: null }); // by seat: the roster says whose it is
    expect(loadProfileKeys(store)[0]!.profileId).toBe("pr_other");
  });
});
