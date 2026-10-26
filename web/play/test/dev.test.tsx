import { afterEach, describe, expect, it } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import { RedlineBoard } from "../src/dev/RedlineBoard";
import { DevPage, phoneCount } from "../src/dev/DevPage";
import { HostClient } from "../src/dev/host";
import { HostPanel } from "../src/dev/HostPanel";
import type { LobbyState } from "../src/protocol";
import type { KeyValueStore } from "../src/session/storage";
import lobbyFixture from "./fixtures/lobby.json";
import { sampleRedlineView } from "./redlineSample";
import { FakeTransport, flush, hubError } from "./fakeTransport";

afterEach(cleanup);

class MapStore implements KeyValueStore {
  map = new Map<string, string>();
  getItem(k: string) { return this.map.get(k) ?? null; }
  setItem(k: string, v: string) { this.map.set(k, v); }
  removeItem(k: string) { this.map.delete(k); }
}

const rowOf = (container: ParentNode, code: string) => container.querySelector<HTMLElement>(`.trow[data-code='${code}']`)!;

describe("RedlineBoard", () => {
  it("RedlineBoard hides throttles before Question and shows outcomes in Reveal", () => {
    // Throttle phase: even if a throttle leaked into the view, the board shows only LOCKED / CHOOSING / PIT.
    const throttle = sampleRedlineView("Throttle");
    throttle.drivers[0]!.throttle = "Redline";
    const { container, rerender } = render(<RedlineBoard view={throttle} />);
    expect(container.querySelectorAll(".trow")).toHaveLength(22);
    expect(container.querySelector(".tower .chip.CRUISE, .tower .chip.PUSH, .tower .chip.REDLINE")).toBeNull();
    expect(rowOf(container, "NOR").querySelector(".chip")!.textContent).toBe("LOCKED");
    expect(rowOf(container, "PIA").querySelector(".chip")!.textContent).toBe("CHOOSING");
    expect(rowOf(container, "VER").querySelector(".chip")!.textContent).toBe("PIT");
    expect(container.querySelector(".answers")).toBeNull();
    expect(container.querySelector(".outcome")).toBeNull();
    expect(screen.getByText("Choose your throttle")).toBeTruthy();

    // Question: throttles revealed, answers shown, no outcomes and no correct answer yet.
    rerender(<RedlineBoard view={sampleRedlineView("Question")} />);
    expect(rowOf(container, "NOR").querySelector(".chip")!.textContent).toBe("CRUISE");
    expect(rowOf(container, "PIA").querySelector(".chip")!.textContent).toBe("PUSH");
    expect(rowOf(container, "LEC").querySelector(".chip")!.textContent).toBe("REDLINE");
    expect(container.querySelectorAll(".ans")).toHaveLength(4);
    expect(container.querySelector(".ans.right")).toBeNull();
    expect(container.querySelector(".outcome")).toBeNull();

    // Reveal: the correct answer is green, every row has an outcome and movement.
    rerender(<RedlineBoard view={sampleRedlineView("Reveal")} />);
    expect(container.querySelector(".ans.right")!.textContent).toContain("Saturn");
    expect(container.querySelectorAll(".ans.wrong")).toHaveLength(3);
    expect(container.querySelectorAll(".trow .outcome")).toHaveLength(22);
    expect(rowOf(container, "NOR").querySelector(".outcome")!.className).toContain("NoAnswer");
    expect(rowOf(container, "PIA").querySelector(".mv")!.textContent).toBe("▲2");
    expect(rowOf(container, "LEC").querySelector(".mv")!.textContent).toBe("▼1");
  });

  it("throttle legend shows the view's payouts, never hardcoded ones", () => {
    const view = sampleRedlineView("Throttle");
    view.payouts = { cruiseGain: 2, pushGain: 5, pushLoss: 3, redlineGain: 7, redlineLoss: 4 };
    const { container, rerender } = render(<RedlineBoard view={view} />);
    const chips = [...container.querySelectorAll(".legend .chip")].map((c) => c.textContent);
    expect(chips).toEqual(["CRUISE +2 / 0", "PUSH +5 / −3", "REDLINE +7 / −4 · spin"]);

    // A view without payouts (an older server) names the risk in words only.
    rerender(<RedlineBoard view={{ ...view, payouts: undefined }} />);
    const legend = container.querySelector(".legend")!.textContent!;
    expect(legend).toContain("CRUISE");
    expect(legend).toContain("PUSH");
    expect(legend).toContain("REDLINE");
    expect(legend).not.toMatch(/\d/);
  });

  it("shows players, AI, gaps, the pit limiter and the team colour", () => {
    const { container } = render(<RedlineBoard view={sampleRedlineView("Question")} />);
    expect(rowOf(container, "NOR").querySelector(".who")!.textContent).toBe("JO");
    expect(rowOf(container, "LEC").querySelector(".who")!.textContent).toBe("AI");
    expect(rowOf(container, "NOR").querySelector(".gap")!.textContent).toBe("LEAD");
    expect(rowOf(container, "LEC").querySelector(".gap")!.textContent).toMatch(/^−\d+$/);
    expect(rowOf(container, "VER").querySelector(".pit")!.textContent).toBe("PIT");
    expect(rowOf(container, "HAM").classList.contains("offline")).toBe(true);
    expect(rowOf(container, "NOR").querySelector<HTMLElement>(".c")!.style.background).toMatch(/#ff8000|rgb\(255, 128, 0\)/);
    expect(screen.getByText("ROUND 7/15")).toBeTruthy();
  });

  it("lists the Chequered awards", () => {
    const { container } = render(<RedlineBoard view={sampleRedlineView("Chequered")} />);
    const awards = [...container.querySelectorAll(".award")].map((a) => a.textContent);
    expect(awards).toEqual([
      expect.stringContaining("Boldest driver"),
      expect.stringContaining("Sharpest mind"),
      expect.stringContaining("Biggest crash"),
    ]);
    expect(awards[0]).toContain("MO (ALO)");
    expect(awards[0]).toContain("6 Redlines");
  });

  it("shows the claim board in Driver Select", () => {
    const { container } = render(<RedlineBoard view={sampleRedlineView("DriverSelect")} />);
    expect(rowOf(container, "NOR").classList.contains("claimed")).toBe(true);
    expect(rowOf(container, "LEC").querySelector(".who")!.textContent).toBe("AI");
    expect(screen.getByText("Driver select")).toBeTruthy();
  });
});

describe("DevPage", () => {
  it("clamps the phone count", () => {
    expect(phoneCount("")).toBe(4);
    expect(phoneCount("?phones=6")).toBe(6);
    expect(phoneCount("?phones=20")).toBe(8);
    expect(phoneCount("?phones=0")).toBe(1);
    expect(phoneCount("?phones=abc")).toBe(4);
  });

  it("DevPage renders N iframes with distinct slots", async () => {
    const t = new FakeTransport();
    t.responders.CreateRoom = () => ({ code: "BCDF", hostToken: "tok" });
    const store = new MapStore();
    const { container } = render(<DevPage transport={t} search="?phones=3" store={store} />);
    await flush();
    await flush();
    const frames = [...container.querySelectorAll("iframe")];
    expect(frames).toHaveLength(3);
    expect(frames.map((f) => f.getAttribute("src"))).toEqual([
      "/?room=BCDF&slot=1&devName=P1",
      "/?room=BCDF&slot=2&devName=P2",
      "/?room=BCDF&slot=3&devName=P3",
    ]);
    expect(store.getItem("arcbox-host:BCDF")).toBe("tok");
    expect(t.methods()).toEqual(["CreateRoom"]);
  });

  it("resumes the host for ?room= with a stored token, else creates a new room", async () => {
    const t = new FakeTransport();
    const store = new MapStore();
    store.setItem("arcbox-host:BCDF", "tok");
    render(<DevPage transport={t} search="?room=BCDF&phones=2" store={store} />);
    await flush();
    await flush();
    expect(t.invokes[0]).toEqual({ method: "ResumeHost", args: ["BCDF", "tok"] });
    cleanup();

    const t2 = new FakeTransport();
    t2.responders.ResumeHost = () => { throw hubError("ResumeHost", "Room not found."); };
    t2.responders.CreateRoom = () => ({ code: "GHJK", hostToken: "new" });
    const { container } = render(<DevPage transport={t2} search="?room=BCDF&phones=2" store={store} />);
    await flush();
    await flush();
    expect(t2.methods()).toEqual(["ResumeHost", "CreateRoom"]);
    expect(store.getItem("arcbox-host:BCDF")).toBeNull();
    expect(container.querySelector("iframe")!.getAttribute("src")).toContain("room=GHJK");
  });

  it("when a reconnect finds the room gone (server restarted), hosts a new room and the phones follow it", async () => {
    const t = new FakeTransport();
    let rooms = 0;
    t.responders.CreateRoom = () => (++rooms === 1 ? { code: "BCDF", hostToken: "tok" } : { code: "GHJK", hostToken: "tok2" });
    const store = new MapStore();
    const onRoom: string[] = [];
    const { container } = render(<DevPage transport={t} search="?phones=2" store={store} onRoom={(c) => onRoom.push(c)} />);
    await flush();
    await flush();
    t.fire("LobbyChanged", { ...structuredClone(lobbyFixture), code: "BCDF" });
    expect(container.querySelector("iframe")!.getAttribute("src")).toContain("room=BCDF");

    t.responders.ResumeHost = () => { throw hubError("ResumeHost", "Room not found."); };
    t.fireReconnected();
    await flush();
    await flush();
    await flush();

    expect(t.methods()).toEqual(["CreateRoom", "ResumeHost", "CreateRoom"]);
    expect(store.getItem("arcbox-host:BCDF")).toBeNull();
    expect(store.getItem("arcbox-host:GHJK")).toBe("tok2");
    expect([...container.querySelectorAll("iframe")].map((f) => f.getAttribute("src"))).toEqual([
      "/?room=GHJK&slot=1&devName=P1",
      "/?room=GHJK&slot=2&devName=P2",
    ]);
    expect(onRoom).toEqual(["BCDF", "GHJK"]);
    expect(container.querySelector(".host-code b")!.textContent).toBe("GHJK");
    expect(container.querySelector(".host-players")!.textContent).not.toContain("ALEX"); // the dead room's lobby is gone
    expect(container.querySelector(".form-error")).toBeNull();
  });

  it("each phone has a reload button that remounts its iframe", async () => {
    const t = new FakeTransport();
    t.responders.CreateRoom = () => ({ code: "BCDF", hostToken: "tok" });
    const { container } = render(<DevPage transport={t} search="?phones=2" store={new MapStore()} />);
    await flush();
    await flush();
    const first = container.querySelector("iframe");
    fireEvent.click(screen.getAllByRole("button", { name: /reload/i })[0]!);
    expect(container.querySelector("iframe")).not.toBe(first);
  });
});

describe("HostPanel", () => {
  it("drives the room with host methods and renders the latest HostView", async () => {
    const t = new FakeTransport();
    t.responders.CreateRoom = () => ({ code: "BCDF", hostToken: "tok" });
    const host = new HostClient(t, new MapStore());
    await host.connect(null);
    const lobby = structuredClone(lobbyFixture) as LobbyState;
    t.fire("LobbyChanged", { ...lobby, selectedGameId: null, lastResults: null });
    const { container } = render(<HostPanel host={host} />);
    await flush();

    fireEvent.click(screen.getByRole("button", { name: /REDLINE/ }));
    expect(t.invokes.at(-1)).toEqual({ method: "SelectGame", args: ["redline"] });
    fireEvent.click(screen.getByRole("button", { name: /Start/ }));
    expect(t.invokes.at(-1)).toEqual({ method: "StartGame", args: [] });
    fireEvent.click(screen.getAllByRole("button", { name: /Kick/ })[1]!);
    expect(t.invokes.at(-1)).toEqual({ method: "Kick", args: ["p2"] });

    t.fire("LobbyChanged", { ...lobby, inGame: true });
    t.fire("HostView", "counter", { rows: [{ name: "Ann", count: 3 }] });
    await flush();
    expect(container.querySelector("pre")!.textContent).toContain('"count": 3');

    t.fire("HostView", "redline", sampleRedlineView("Question"));
    await flush();
    expect(container.querySelector(".rboard")).not.toBeNull();
    fireEvent.click(screen.getByRole("button", { name: /Return to lobby/ }));
    expect(t.invokes.at(-1)).toEqual({ method: "ReturnToLobby", args: [] });

    t.fire("GameEnded", { scores: [], message: "Game ended by host" });
    t.fire("LobbyChanged", { ...lobby, inGame: false });
    await flush();
    expect(screen.getByText("Game ended by host")).toBeTruthy();
    expect(container.querySelector(".rboard")).toBeNull();
  });

  it("shows host errors", async () => {
    const t = new FakeTransport();
    t.responders.CreateRoom = () => ({ code: "BCDF", hostToken: "tok" });
    t.responders.StartGame = () => { throw hubError("StartGame", "This game needs 2–8 players."); };
    const host = new HostClient(t, new MapStore());
    await host.connect(null);
    t.fire("LobbyChanged", structuredClone(lobbyFixture));
    render(<HostPanel host={host} />);
    await flush();
    fireEvent.click(screen.getByRole("button", { name: /Start/ }));
    await flush();
    expect(screen.getByRole("alert").textContent).toBe("This game needs 2–8 players.");
  });
});
