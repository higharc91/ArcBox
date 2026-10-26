import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import type { ButtonsView, ChoiceView, GameResults, LobbyState, WaitView } from "../src/protocol";
import { Join } from "../src/screens/Join";
import { Lobby } from "../src/screens/Lobby";
import { Results } from "../src/screens/Results";
import { Closed } from "../src/screens/Closed";
import { App, boot } from "../src/App";
import { ControllerScreen } from "../src/widgets/ControllerScreen";
import { TEAMS, driverBadge, teamColor, teamColorById } from "../src/skins/redline";
import { Session } from "../src/session/session";
import { loadSeat, saveSeat, type KeyValueStore } from "../src/session/storage";
import lobbyFixture from "./fixtures/lobby.json";
import choiceFixture from "./fixtures/choice.json";
import buttonsFixture from "./fixtures/buttons.json";
import { FakeTransport, flush, hubError } from "./fakeTransport";

afterEach(cleanup);

const lobby = (over: Partial<LobbyState> = {}): LobbyState => ({ ...(structuredClone(lobbyFixture) as LobbyState), ...over });
const input = (label: RegExp) => screen.getByLabelText(label) as HTMLInputElement;
const button = (name: RegExp) => screen.getByRole("button", { name }) as HTMLButtonElement;

class MapStore implements KeyValueStore {
  map = new Map<string, string>();
  getItem(k: string) { return this.map.get(k) ?? null; }
  setItem(k: string, v: string) { this.map.set(k, v); }
  removeItem(k: string) { this.map.delete(k); }
}

describe("Join", () => {
  it("Join uppercases code and disables button for invalid name", () => {
    const onJoin = vi.fn();
    render(<Join onJoin={onJoin} />);
    fireEvent.input(input(/Room code/), { target: { value: "bc-d1f" } });
    expect(input(/Room code/).value).toBe("BCDF");
    expect(button(/Join/).disabled).toBe(true);
    fireEvent.input(input(/Your name/), { target: { value: "   " } });
    expect(button(/Join/).disabled).toBe(true);
    fireEvent.input(input(/Your name/), { target: { value: "ThirteenChars" } });
    expect(button(/Join/).disabled).toBe(true);
    fireEvent.input(input(/Your name/), { target: { value: "  Alex " } });
    expect(button(/Join/).disabled).toBe(false);
    fireEvent.click(button(/Join/));
    expect(onJoin).toHaveBeenCalledWith("BCDF", "Alex");
  });

  it("refuses a short code", () => {
    render(<Join code="BCD" name="Alex" onJoin={() => {}} />);
    expect(button(/Join/).disabled).toBe(true);
  });

  it("Join shows server error text", () => {
    render(<Join code="BCDF" name="ALEX" error="That name is taken." onJoin={() => {}} />);
    expect(screen.getByRole("alert").textContent).toBe("That name is taken.");
    expect(input(/Room code/).value).toBe("BCDF");
    expect(input(/Your name/).value).toBe("ALEX");
  });

  it("disables the form while a join is in flight", () => {
    render(<Join code="BCDF" name="ALEX" busy onJoin={() => {}} />);
    expect(button(/Joining/).disabled).toBe(true);
  });
});

describe("Lobby", () => {
  const props = { onSelectGame: vi.fn(), onStart: vi.fn() };

  it("Lobby shows start only for VIP and only when canStart", () => {
    const { rerender } = render(<Lobby lobby={lobby({ canStart: true })} me="p1" {...props} />);
    expect(button(/Everybody's in/).disabled).toBe(false);
    fireEvent.click(button(/Everybody's in/));
    expect(props.onStart).toHaveBeenCalled();

    rerender(<Lobby lobby={lobby({ canStart: false })} me="p1" {...props} />);
    expect(button(/Everybody's in/).disabled).toBe(true);

    rerender(<Lobby lobby={lobby({ canStart: true })} me="p2" {...props} />);
    expect(screen.queryByRole("button", { name: /Everybody's in/ })).toBeNull();
    expect(screen.getByText("Waiting for ALEX to start")).toBeTruthy();
  });

  it("lists players with VIP and disconnected markers", () => {
    const { container } = render(<Lobby lobby={lobby()} me="p2" {...props} />);
    const rows = [...container.querySelectorAll(".player")];
    expect(rows.map((r) => r.querySelector(".pname")!.textContent)).toEqual(["ALEX", "MO"]);
    expect(rows[0]!.querySelector(".vip")).not.toBeNull();
    expect(rows[1]!.classList.contains("offline")).toBe(true);
    expect(rows[1]!.classList.contains("me")).toBe(true);
    expect(screen.getByText("BCDF")).toBeTruthy();
  });

  it("VIP picks a game from cards filtered by player count", () => {
    const games = [
      { id: "counter", name: "Counter", description: "Tap", minPlayers: 1, maxPlayers: 8 },
      { id: "solo", name: "Solo", description: "One only", minPlayers: 1, maxPlayers: 1 },
      { id: "redline", name: "REDLINE", description: "Race", minPlayers: 2, maxPlayers: 8 },
    ];
    render(<Lobby lobby={lobby({ games, selectedGameId: "counter" })} me="p1" {...props} />);
    expect(screen.queryByRole("button", { name: /Solo/ })).toBeNull();
    expect(button(/Counter/).getAttribute("aria-pressed")).toBe("true");
    fireEvent.click(button(/REDLINE/));
    expect(props.onSelectGame).toHaveBeenCalledWith("redline");
  });

  it("last results without a message name the winner, or the tie (seen with 6 bots on Counter)", () => {
    const score = (playerId: string, name: string, n: number) => ({ playerId, name, score: n });
    const { rerender } = render(
      <Lobby lobby={lobby({ lastResults: { scores: [score("p3", "P3", 50), score("p4", "P4", 45)] } })} me="p1" {...props} />,
    );
    expect(screen.getByText("P3 won with 50")).toBeTruthy();

    rerender(<Lobby lobby={lobby({ lastResults: { scores: [score("p3", "P3", 50), score("p4", "P4", 50), score("p2", "P2", 45)] } })} me="p1" {...props} />);
    expect(screen.getByText("P3 and P4 tied with 50")).toBeTruthy();

    rerender(<Lobby lobby={lobby({ lastResults: { scores: [score("a", "A", 7), score("b", "B", 7), score("c", "C", 7)] } })} me="p1" {...props} />);
    expect(screen.getByText("A, B and C tied with 7")).toBeTruthy();
  });

  it("shows the last results and a lobby error", () => {
    render(<Lobby lobby={lobby()} me="p1" error="Pick a game first." {...props} />);
    expect(screen.getByText("ALEX (NOR) wins the race!")).toBeTruthy();
    expect(screen.getByRole("alert").textContent).toBe("Pick a game first.");
  });
});

describe("Results", () => {
  it("Results lists scores in order", () => {
    const results: GameResults = {
      scores: [
        { playerId: "p2", name: "MO (ALO)", score: 22 },
        { playerId: "p1", name: "ALEX (NOR)", score: 21 },
        { playerId: "p3", name: "KAI (VER)", score: 9 },
      ],
      message: "MO (ALO) wins the race!",
    };
    const onBack = vi.fn();
    const { container } = render(<Results results={results} me="p1" onBack={onBack} />);
    const rows = [...container.querySelectorAll(".score-row")];
    expect(rows.map((r) => r.querySelector(".sname")!.textContent)).toEqual(["MO (ALO)", "ALEX (NOR)", "KAI (VER)"]);
    expect(rows.map((r) => r.querySelector(".spos")!.textContent)).toEqual(["1", "2", "3"]);
    expect(rows.map((r) => r.querySelector(".sscore")!.textContent)).toEqual(["22", "21", "9"]);
    expect(rows[1]!.classList.contains("me")).toBe(true);
    expect(screen.getByText("MO (ALO) wins the race!")).toBeTruthy();
    fireEvent.click(button(/Back to lobby/));
    expect(onBack).toHaveBeenCalled();
  });

  it("handles a game ended by the host with no scores", () => {
    render(<Results results={{ scores: [], message: "Game ended by host" }} me="p1" onBack={() => {}} />);
    expect(screen.getByText("Game ended by host")).toBeTruthy();
  });
});

describe("Closed", () => {
  it("shows the reason and a way out", () => {
    const onLeave = vi.fn();
    render(<Closed reason="game stopped responding" onLeave={onLeave} />);
    expect(screen.getByText("game stopped responding")).toBeTruthy();
    fireEvent.click(button(/Join another room/));
    expect(onLeave).toHaveBeenCalled();
  });
});

describe("REDLINE skin", () => {
  it("knows all 11 teams", () => {
    expect(TEAMS).toHaveLength(11);
    expect(teamColor("McLaren · MCL40")).toBe("#ff8000");
    expect(teamColor("Racing Bulls · VCARB 03")).toBe("#e3102f");
    expect(teamColorById("aston-martin")).toBe("#00594f");
    expect(teamColor("Unknown · X1")).toBeUndefined();
    expect(teamColor(undefined)).toBeUndefined();
  });

  it("redline skin colours driver badge by team", () => {
    expect(driverBadge({ id: "PIA", label: "#81 Oscar Piastri", hint: "McLaren · MCL40" }))
      .toEqual({ text: "81", label: "Oscar Piastri", color: "#ff8000", ink: "#111" });
    expect(driverBadge({ id: "a", label: "Saturn" })).toBeUndefined();

    const view = structuredClone(choiceFixture) as ChoiceView;
    const { container } = render(<ControllerScreen view={view} send={() => {}} me="ALEX" game="redline" />);
    expect(container.querySelector("[data-game='redline']")).not.toBeNull();
    const badge = container.querySelector<HTMLElement>("[data-id='LEC'] .badge")!;
    expect(badge.textContent).toBe("16");
    expect(badge.style.background).toMatch(/#dc0000|rgb\(220, 0, 0\)/);
    expect(container.querySelector("[data-id='LEC'] .opt-label")!.textContent).toBe("Charles Leclerc");
    expect(container.querySelector("[data-id='x'] .badge")).toBeNull();
  });

  it("leaves other games on the generic look", () => {
    const view = structuredClone(choiceFixture) as ChoiceView;
    const { container } = render(<ControllerScreen view={view} send={() => {}} me="ALEX" game="counter" />);
    expect(container.querySelector(".badge")).toBeNull();
    expect(container.querySelector("[data-id='PIA'] .opt-label")!.textContent).toBe("#81 Oscar Piastri");
  });

  it("redline skin styles throttle buttons by id", () => {
    const view = { ...(structuredClone(buttonsFixture) as ButtonsView), header: "PUSH · P5 · ROUND 3/15" };
    const { container } = render(<ControllerScreen view={view} send={() => {}} me="ALEX" game="redline" />);
    for (const id of ["cruise", "push", "redline"]) {
      expect(container.querySelector(`[data-id='${id}']`)!.classList.contains(`throttle-${id}`)).toBe(true);
    }
    expect(container.querySelector(".pstatus .pos .chip")!.textContent).toBe("PUSH");
    expect(container.querySelector(".pstatus .pos b")!.textContent).toBe("P5");
    expect(container.querySelector(".pstatus .pos small")!.textContent).toBe("ROUND 3/15");
  });

  it("splits the throttle prompt into a meta line and the question", () => {
    const view = structuredClone(buttonsFixture) as ButtonsView;
    const { container, rerender } = render(<ControllerScreen view={view} send={() => {}} me="ALEX" game="redline" />);
    expect(container.querySelector(".pq small")!.textContent).toBe("Science & Nature · Hard");
    expect(container.querySelector(".pq .prompt")!.textContent).toBe("How hard do you push?");
    rerender(<ControllerScreen view={view} send={() => {}} me="ALEX" game="counter" />);
    expect(container.querySelector(".pq small")).toBeNull();
    expect(container.querySelector(".pq .prompt")!.textContent).toBe("Science & Nature · Hard. How hard do you push?");
  });

  it("tones the reveal message", () => {
    const wait = (message: string): WaitView => ({ kind: "wait", viewId: "v", message, header: "P5 · ROUND 3/15" });
    const { container, rerender } = render(<ControllerScreen view={wait("Spun out! −2 · pit limiter next round · now P5")} send={() => {}} me="A" game="redline" />);
    expect(container.querySelector(".wait-banner.bad .banner-title")!.textContent).toBe("Spun out!");
    rerender(<ControllerScreen view={wait("Correct! +4 · now P2")} send={() => {}} me="A" game="redline" />);
    expect(container.querySelector(".wait-banner.good")).not.toBeNull();
    rerender(<ControllerScreen view={wait("Answer locked. Eyes on the TV!")} send={() => {}} me="A" game="redline" />);
    expect(container.querySelector(".wait-banner")).toBeNull();
  });
});

describe("App", () => {
  it("boot resumes a stored seat, else pre-fills the code", async () => {
    const t = new FakeTransport();
    const store = new MapStore();
    const session = new Session(t, store);
    await boot(session, store, "?room=bcdf");
    expect(session.state).toEqual({ screen: "join", code: "BCDF" });

    saveSeat(store, "BCDF", "2", { playerId: "p1", secret: "s", name: "ALEX" });
    const s2 = new Session(t, store, "2");
    t.responders.Rejoin = () => t.fire("LobbyChanged", lobby());
    await boot(s2, store, "?room=BCDF&slot=2");
    expect(s2.state.screen).toBe("lobby");
  });

  it("boot auto-joins with devName (dev page only)", async () => {
    const t = new FakeTransport();
    const store = new MapStore();
    const session = new Session(t, store, "1");
    t.responders.Join = () => {
      t.fire("LobbyChanged", lobby());
      return { code: "BCDF", playerId: "p1", secret: "s" };
    };
    await boot(session, store, "?room=BCDF&slot=1&devName=P1");
    expect(t.invokes[0]).toEqual({ method: "Join", args: ["BCDF", "P1"] });
    expect(loadSeat(store, "BCDF", "1")).not.toBeNull();
  });

  it("renders the screen for the session state and switches with it", async () => {
    const t = new FakeTransport();
    const session = new Session(t, new MapStore());
    const { container } = render(<App session={session} />);
    expect(screen.getByLabelText(/Room code/)).toBeTruthy();

    t.responders.Join = () => {
      t.fire("LobbyChanged", lobby({ selectedGameId: "redline", inGame: true }));
      return { code: "BCDF", playerId: "p1", secret: "s" };
    };
    fireEvent.input(input(/Room code/), { target: { value: "bcdf" } });
    fireEvent.input(input(/Your name/), { target: { value: "ALEX" } });
    fireEvent.click(button(/Join/));
    await flush();
    expect(container.querySelector(".lobby")).not.toBeNull();

    t.fire("ControllerView", structuredClone(buttonsFixture));
    await flush();
    expect(container.querySelector("[data-game='redline'] .throttle-redline")).not.toBeNull();
    fireEvent.click(button(/REDLINE/));
    expect(t.invokes.at(-1)).toEqual({ method: "SendInput", args: ["v1", { pressed: "redline" }] });

    t.fire("Kicked");
    await flush();
    expect(screen.getByText("You were removed from the room.")).toBeTruthy();
  });

  it("shows the server's join error under the form", async () => {
    const t = new FakeTransport();
    const session = new Session(t, new MapStore());
    render(<App session={session} />);
    t.responders.Join = () => { throw hubError("Join", "Game in progress, join after this round."); };
    fireEvent.input(input(/Room code/), { target: { value: "BCDF" } });
    fireEvent.input(input(/Your name/), { target: { value: "ALEX" } });
    fireEvent.click(button(/Join/));
    await flush();
    expect(screen.getByRole("alert").textContent).toBe("Game in progress, join after this round.");
    expect(input(/Your name/).value).toBe("ALEX");
  });
});
