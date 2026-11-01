import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/preact";
import type { CustomView } from "../src/protocol";
import { loadModule } from "../src/games/registry";
import { ControllerScreen } from "../src/widgets/ControllerScreen";

// Real views from a real session, written by games/Tactics/Tactics.Game.Tests/PhoneFixtureTests.cs.
const fixtures = import.meta.glob<{ default: unknown }>("./fixtures/tactics/*.json", { eager: true });
const view = (name: string, change?: (data: Record<string, unknown>, v: CustomView) => void, viewId = "v1"): CustomView => {
  const mod = fixtures[`./fixtures/tactics/${name}.json`];
  if (!mod) throw new Error(`missing fixture tactics/${name}`);
  const v = { ...(structuredClone(mod.default) as CustomView), viewId };
  change?.(v.data as Record<string, unknown>, v);
  return v;
};

function show(name: string, change?: (data: Record<string, unknown>, v: CustomView) => void) {
  const send = vi.fn();
  const utils = render(<ControllerScreen view={view(name, change)} send={send} me="Player1" game="tactics" />);
  const again = (next: CustomView) => utils.rerender(<ControllerScreen view={next} send={send} me="Player1" game="tactics" />);
  return { send, again, ...utils };
}

/** The option buttons of a vote, by their label. */
const option = (label: RegExp) => screen.getByRole("button", { name: label });
const pressed = (el: HTMLElement) => el.getAttribute("aria-pressed");

beforeAll(async () => {
  await loadModule("tactics");
});
beforeEach(() => {
  vi.useFakeTimers();
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe("dialogue", () => {
  it("shows the line and its speaker", () => {
    show("dialogue");
    expect(screen.getByText("Halden Varrow")).toBeTruthy();
    expect(screen.getByText(/Forty of them, perhaps/)).toBeTruthy();
    expect(screen.getByText("The Ford Choice")).toBeTruthy();
    // The speaker's portrait, in the line's mood, once the pack has loaded; a framed initial until then.
    expect(document.querySelector(".tx-say .tx-portrait")).not.toBeNull();
  });

  it("shows skip only to the leader", () => {
    show("dialogue");
    expect(screen.queryByRole("button", { name: /Skip/ })).toBeNull();
    cleanup();
    show("dialogue-leader");
    expect(screen.getByRole("button", { name: /Skip/ })).toBeTruthy();
  });

  it("a skip sends the line it meant, once, however fast the taps", () => {
    const { send } = show("dialogue-leader");
    vi.advanceTimersByTime(1000);
    const skip = screen.getByRole("button", { name: /Skip/ });
    // Two taps before the screen can re-render, then one more after.
    act(() => {
      skip.click();
      skip.click();
    });
    fireEvent.click(skip);
    expect(send).toHaveBeenCalledTimes(1);
    expect(send).toHaveBeenLastCalledWith({ action: "skip", data: { line: "ch1_ford_open_3" } });
  });

  it("a tap that lands as the next line appears doesn't skip that one too", () => {
    const { send, again } = show("dialogue-leader");
    vi.advanceTimersByTime(1000);
    fireEvent.click(screen.getByRole("button", { name: /Skip/ }));
    again(view("dialogue-leader", (d) => { d.line = "ch1_ford_open_4"; d.text = "Then go."; }, "v2"));
    fireEvent.click(screen.getByRole("button", { name: /Skip/ }));
    expect(send).toHaveBeenCalledTimes(1);

    vi.advanceTimersByTime(1000);
    fireEvent.click(screen.getByRole("button", { name: /Skip/ }));
    expect(send).toHaveBeenCalledTimes(2);
    expect(send).toHaveBeenLastCalledWith({ action: "skip", data: { line: "ch1_ford_open_4" } });
  });

  it("shows the narrator's lines without a speaker or portrait", () => {
    show("dialogue", (d) => { d.speaker = null; d.portrait = null; d.text = "Rain on the ford."; });
    expect(screen.getByText("Rain on the ford.")).toBeTruthy();
    expect(document.querySelector(".tx-say .tx-portrait")).toBeNull();
    expect(document.querySelector(".tx-say.narration")).not.toBeNull();
  });

  it("between lines, shows the scene and no skip", () => {
    show("dialogue-leader", (d) => { d.line = null; d.speaker = null; d.portrait = null; d.mood = null; d.text = null; d.canSkip = false; });
    expect(screen.getByText("The Ford Choice")).toBeTruthy();
    expect(screen.queryByRole("button", { name: /Skip/ })).toBeNull();
  });
});

describe("votes and choices", () => {
  it("shows the options and marks my vote", () => {
    show("vote");
    expect(screen.getByText("Party vote")).toBeTruthy();
    expect(pressed(option(/clear the ford by force/))).toBe("true");
    expect(pressed(option(/parley with the chief/))).toBe("false");
    expect(within(option(/clear the ford by force/)).getByText("Your vote")).toBeTruthy();
  });

  it("shows a check's skill and dc on its option", () => {
    show("vote");
    expect(within(option(/what's it worth/)).getByText("Persuasion check · DC 15")).toBeTruthy();
  });

  it("lets me change my vote, and shows it at once", () => {
    const { send, again } = show("vote");
    fireEvent.click(option(/parley with the chief/));
    expect(send).toHaveBeenLastCalledWith({ action: "vote", data: { option: "parley" } });
    expect(pressed(option(/parley with the chief/))).toBe("true");
    expect(pressed(option(/clear the ford by force/))).toBe("false");

    // The server's view follows; my vote stays where I put it.
    again(view("vote", (d) => { d.mine = "parley"; }, "v2"));
    expect(pressed(option(/parley with the chief/))).toBe("true");

    // Back again, and a second tap on my own vote sends nothing new.
    fireEvent.click(option(/clear the ford by force/));
    fireEvent.click(option(/clear the ford by force/));
    expect(send.mock.calls.map((c) => c[0])).toEqual([
      { action: "vote", data: { option: "parley" } },
      { action: "vote", data: { option: "force" } },
    ]);
  });

  it("counts down to the deadline", async () => {
    show("vote", (_, v) => { v.deadlineUtc = new Date(Date.now() + 30_000).toISOString(); });
    await vi.advanceTimersByTimeAsync(300);
    expect(within(document.querySelector(".tx-ask-head")!).getByRole("timer").textContent).toMatch(/0:(30|29)/);
    await vi.advanceTimersByTimeAsync(10_000);
    expect(within(document.querySelector(".tx-ask-head")!).getByRole("timer").textContent).toMatch(/0:(20|19)/);
  });

  it("lets the leader choose for the party", () => {
    const { send } = show("choice-leader");
    expect(screen.getByText("You choose for the party")).toBeTruthy();
    fireEvent.click(option(/Make your cases/));
    expect(send).toHaveBeenLastCalledWith({ action: "vote", data: { option: "cases" } });
  });

  it("shows a non-vote choice read-only to everyone else, with the party's choice", () => {
    show("choice-follower");
    expect(screen.queryAllByRole("button").filter((b) => b.closest(".tx-ask"))).toEqual([]);
    expect(screen.getByText("Player1 chooses for the party")).toBeTruthy();
    const chosen = screen.getByText(/Make your cases/).closest("li")!;
    expect(within(chosen).getByText("The party's choice")).toBeTruthy();
  });

  it("shows the party's choice to a leader who took over, until they pick", () => {
    const { send } = show("choice-follower", (d) => { d.canChoose = true; });
    expect(within(option(/Make your cases/)).getByText("The party's choice")).toBeTruthy();
    fireEvent.click(option(/Why did they take the bridge/));
    expect(send).toHaveBeenLastCalledWith({ action: "vote", data: { option: "why" } });
    expect(within(option(/Why did they take the bridge/)).getByText("Your choice")).toBeTruthy();
    expect(within(option(/Make your cases/)).queryByText("The party's choice")).toBeNull();
  });

  it("offers only the tied options on a tie-break, and names who's away", () => {
    const { send } = show("vote-tie");
    expect(screen.getByText("Tied: you break it")).toBeTruthy();
    expect(screen.getAllByRole("button").filter((b) => b.closest(".tx-options")).length).toBe(2);
    expect(screen.getByText("Player4 is away and doesn't vote")).toBeTruthy();
    fireEvent.click(option(/parley with the chief/));
    expect(send).toHaveBeenLastCalledWith({ action: "vote", data: { option: "parley" } });
  });

  it("shows a tie-break someone else decides read-only", () => {
    show("vote-tie", (d) => { d.canChoose = false; });
    expect(screen.getByText("Tied: Player1 breaks it")).toBeTruthy();
    expect(screen.queryAllByRole("button").filter((b) => b.closest(".tx-ask"))).toEqual([]);
  });
});

describe("skill checks", () => {
  it("shows cards with bonuses and who's away", () => {
    show("check-pick");
    expect(screen.getByText("Persuasion")).toBeTruthy();
    expect(screen.getByText("DC 15")).toBeTruthy();
    expect(screen.getByText(/what's it worth to you/)).toBeTruthy();
    const card = screen.getByRole("button", { name: /Player2/ });
    expect(within(card).getByText("+2")).toBeTruthy();
    expect(within(card).getByText("Proficient · CHA 10")).toBeTruthy();
    expect(within(screen.getByRole("button", { name: /Player4/ })).getByText("Away")).toBeTruthy();
    expect(pressed(screen.getByRole("button", { name: /Player3/ }))).toBe("true");
  });

  it("picks a roller", () => {
    const { send } = show("check-pick");
    fireEvent.click(screen.getByRole("button", { name: /Player2/ }));
    expect(send).toHaveBeenLastCalledWith({ action: "pickRoller", data: { member: "pr_member2" } });
    expect(pressed(screen.getByRole("button", { name: /Player2/ }))).toBe("true");
    expect(pressed(screen.getByRole("button", { name: /Player3/ }))).toBe("false");
  });

  it("on a tie, the decider picks among the tied only", () => {
    const { send } = show("check-pick", (d) => { d.tie = true; d.decider = "Player1"; d.tied = ["pr_member2", "pr_member3"]; d.mine = null; });
    expect(screen.getByText("Tied: you break it")).toBeTruthy();
    expect((screen.getByRole("button", { name: /Player1/ }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: /Player1/ }));
    expect(send).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: /Player2/ }));
    expect(send).toHaveBeenLastCalledWith({ action: "pickRoller", data: { member: "pr_member2" } });
  });

  it("shows the roll", () => {
    show("check-rolled");
    const roll = document.querySelector(".tx-roll")!;
    expect(within(roll as HTMLElement).getByText("Failure")).toBeTruthy();
    expect(roll.textContent).toMatch(/6\s*\+\s*0\s*=\s*6\s*vs DC\s*15/);
    expect(within(roll as HTMLElement).getByText(/Player3 rolled/)).toBeTruthy();
    expect(screen.queryAllByRole("button").filter((b) => b.closest(".tx-check"))).toEqual([]);
  });
});

describe("Interact", () => {
  it("shows Interact on the walk screen and sends it", () => {
    const { send } = show("walk-interact");
    fireEvent.click(screen.getByRole("button", { name: "Talk" }));
    expect(send).toHaveBeenLastCalledWith({ action: "interact" });
  });

  it("isn't there out of reach, nor for anyone but the leader", () => {
    show("walk-leader");
    expect(screen.queryByRole("button", { name: "Talk" })).toBeNull();
    cleanup();
    show("walk-interact", (d) => { d.leader = false; });
    expect(screen.queryByRole("button", { name: "Talk" })).toBeNull();
  });

  it("sends once for a double tap", async () => {
    const { send } = show("walk-interact");
    const talk = screen.getByRole("button", { name: "Talk" });
    act(() => {
      talk.click();
      talk.click();
    });
    fireEvent.click(talk);
    expect(send.mock.calls.filter((c) => (c[0] as { action: string }).action === "interact").length).toBe(1);

    // Nothing came of it (out of reach by then): it wakes again.
    await vi.advanceTimersByTimeAsync(2500);
    fireEvent.click(screen.getByRole("button", { name: "Talk" }));
    expect(send.mock.calls.filter((c) => (c[0] as { action: string }).action === "interact").length).toBe(2);
  });

  it("keeps the pad working when Interact appears", () => {
    const { send, again } = show("walk-leader");
    const zone = document.querySelector<HTMLElement>(".tx-padzone")!;
    const face = document.querySelector<HTMLElement>(".tx-pad")!;
    face.getBoundingClientRect = () => ({ left: 0, top: 0, right: 200, bottom: 200, width: 200, height: 200, x: 0, y: 0, toJSON: () => ({}) });
    zone.setPointerCapture = vi.fn();
    const e = new MouseEvent("pointerdown", { bubbles: true, cancelable: true, clientX: 100, clientY: 20, button: 0 });
    Object.defineProperty(e, "pointerId", { value: 1 });
    zone.dispatchEvent(e);

    again(view("walk-interact", undefined, "v2"));
    expect(document.querySelector(".tx-padzone")).toBe(zone);
    expect(face.classList.contains("down")).toBe(true);
    expect(send).toHaveBeenLastCalledWith({ action: "walk", data: { dir: "n" } });
    vi.advanceTimersByTime(500);
    expect(send).toHaveBeenLastCalledWith({ action: "walk", data: { dir: "n" } });
    expect(screen.getByRole("button", { name: "Talk" })).toBeTruthy();
  });
});
