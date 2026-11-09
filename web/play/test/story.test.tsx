import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/preact";
import type { CustomView } from "../src/protocol";
import { loadModule } from "../src/games/registry";
import { QUEUE_MS } from "../src/games/tactics/story";
import { ControllerScreen } from "../src/widgets/ControllerScreen";
import { Feedback, FeedbackContext } from "../src/feedback/feedback";

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
  vi.unstubAllGlobals();
});

/**
 * A run of views on one phone with the speaker on, counting its alerts by their buzz (the sound needs an AudioContext,
 * which tests don't have). The clock starts clear of any alert an earlier test played.
 */
let clear = Date.now();
function phone() {
  const vibrate = vi.fn(() => true);
  vi.stubGlobal("navigator", { ...navigator, vibrate });
  // Each test's fake clock starts again from the real one, so each phone starts an hour past the last.
  clear += 3_600_000;
  vi.setSystemTime(clear);
  const feedback = new Feedback({ getItem: () => null, setItem() {}, removeItem() {} } as never, { unlock() {}, play() {} } as never);
  const send = vi.fn();
  const shown = (v: CustomView) => (
    <FeedbackContext.Provider value={feedback}>
      <ControllerScreen view={v} send={send} me="Player1" game="tactics" />
    </FeedbackContext.Provider>
  );
  let utils: ReturnType<typeof render> | null = null;
  const to = (v: CustomView) => {
    if (utils) utils.rerender(shown(v));
    else utils = render(shown(v));
    vi.advanceTimersByTime(10);
  };
  /** A reconnect: the screen goes, and the same view comes back. */
  const remount = (v: CustomView) => {
    utils?.unmount();
    utils = null;
    to(v);
  };
  return { send, to, remount, alerts: () => vibrate.mock.calls.length };
}

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

  it("says each new line to a screen reader, in a live region that stays put", () => {
    const { again } = show("dialogue");
    const live = document.querySelector<HTMLElement>(".tx-story [aria-live='polite']")!;
    expect(live.textContent).toMatch(/Halden Varrow.*Forty of them, perhaps/);
    again(view("dialogue", (d) => { d.line = "ch1_ford_open_4"; d.speaker = null; d.portrait = null; d.text = "Rain on the ford."; }, "v2"));
    expect(document.querySelector(".tx-story [aria-live='polite']")).toBe(live);
    expect(live.textContent).toBe("Rain on the ford.");
    again(view("dialogue", (d) => { d.line = null; d.speaker = null; d.text = null; }, "v3"));
    expect(live.textContent).toBe("Follow the dialogue on the TV.");
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

  it("asks who takes a found item, with a vote for each player here (the owner's decision 1)", () => {
    const { send, again } = show("vote-loot");
    expect(screen.getByRole("heading", { name: "Who takes the Smoke Bomb?" })).toBeTruthy();
    expect(screen.getByText("Party vote")).toBeTruthy();
    expect(within(option(/Player3/)).getByText("Your vote")).toBeTruthy();
    fireEvent.click(option(/Player2/));
    expect(send).toHaveBeenLastCalledWith({ action: "vote", data: { option: "pr_member2" } });
    // The next item's vote is a new question, even between the same players: nothing tapped carries over.
    again(view("vote-loot", (d) => { d.question = "Who takes the Potion of Healing?"; d.vote = 1; d.mine = null; }, "v2"));
    expect(screen.getByRole("heading", { name: "Who takes the Potion of Healing?" })).toBeTruthy();
    expect(screen.queryByText("Your vote")).toBeNull();
  });

  it("offers only the tied options on a tie-break, and names who's away", () => {
    const { send } = show("vote-tie");
    expect(screen.getByText("Tied: you break it")).toBeTruthy();
    expect(screen.getAllByRole("button").filter((b) => b.closest(".tx-options")).length).toBe(2);
    expect(screen.getByText("Player4 is away and doesn't vote")).toBeTruthy();
    fireEvent.click(option(/parley with the chief/));
    expect(send).toHaveBeenLastCalledWith({ action: "vote", data: { option: "parley" } });
  });

  it("on a tie-break, my vote from the open round isn't my answer yet: tapping it sends", () => {
    // The crown passed to me mid-question; I'd voted force before the tie.
    const { send, again } = show("vote-tie", (d) => { d.mine = "force"; });
    expect(pressed(option(/clear the ford by force/))).toBe("false");
    expect(within(option(/clear the ford by force/)).getByText("You voted this earlier")).toBeTruthy();
    fireEvent.click(option(/clear the ford by force/));
    expect(send).toHaveBeenLastCalledWith({ action: "vote", data: { option: "force" } });
    expect(pressed(option(/clear the ford by force/))).toBe("true");

    // The server's view comes back with it: it's my answer now, and a second tap sends nothing more.
    again(view("vote-tie", (d) => { d.mine = "force"; }, "v2"));
    expect(within(option(/clear the ford by force/)).getByText("Your vote")).toBeTruthy();
    fireEvent.click(option(/clear the ford by force/));
    expect(send).toHaveBeenCalledTimes(1);
  });

  it("a second tap before the view comes back waits for it, then goes on the new view", () => {
    const first = vi.fn();
    const { rerender } = render(<ControllerScreen view={view("vote")} send={first} me="Player1" game="tactics" />);
    act(() => {
      option(/parley with the chief/).click();
      option(/what's it worth/).click();
    });
    expect(first.mock.calls.map((c) => c[0])).toEqual([{ action: "vote", data: { option: "parley" } }]);
    // The latest tap is the one shown.
    expect(pressed(option(/what's it worth/))).toBe("true");
    expect(pressed(option(/parley with the chief/))).toBe("false");

    const second = vi.fn();
    rerender(<ControllerScreen view={view("vote", (d) => { d.mine = "parley"; }, "v2")} send={second} me="Player1" game="tactics" />);
    expect(second.mock.calls.map((c) => c[0])).toEqual([{ action: "vote", data: { option: "pay" } }]);
    expect(first).toHaveBeenCalledTimes(1);
    expect(pressed(option(/what's it worth/))).toBe("true");
  });

  it("a held tap goes anyway if no view comes back", () => {
    const { send } = show("vote");
    act(() => {
      option(/parley with the chief/).click();
      option(/what's it worth/).click();
    });
    expect(send).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(QUEUE_MS + 100);
    expect(send.mock.calls.map((c) => c[0])).toEqual([
      { action: "vote", data: { option: "parley" } },
      { action: "vote", data: { option: "pay" } },
    ]);
  });

  it("shows a tie-break someone else decides read-only", () => {
    show("vote-tie", (d) => { d.canChoose = false; });
    expect(screen.getByText("Tied: Player1 breaks it")).toBeTruthy();
    // Nothing to answer with: the only button is the VIP's End session, which isn't the question's.
    expect(screen.queryAllByRole("button").filter((b) => b.closest(".tx-ask") && !b.closest(".tx-walk-host"))).toEqual([]);
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

  it("on a tie, the decider's own earlier pick is tappable and sends", () => {
    const { send } = show("check-pick", (d) => { d.tie = true; d.decider = "Player1"; d.tied = ["pr_member2", "pr_member3"]; });
    expect(pressed(screen.getByRole("button", { name: /Player3/ }))).toBe("false");
    expect(within(screen.getByRole("button", { name: /Player3/ })).getByText("Your pick earlier")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: /Player3/ }));
    expect(send).toHaveBeenLastCalledWith({ action: "pickRoller", data: { member: "pr_member3" } });
    expect(pressed(screen.getByRole("button", { name: /Player3/ }))).toBe("true");
  });

  it("rolls the die (sound and spin) once: a reconnect's remount stays still", () => {
    show("check-rolled");
    vi.advanceTimersByTime(5000);
    cleanup();
    show("check-rolled");
    expect(document.querySelector(".tx-roll")!.classList.contains("fresh")).toBe(true);
    cleanup();
    show("check-rolled");
    expect(document.querySelector(".tx-roll")!.classList.contains("fresh")).toBe(false);
  });

  it("shows the roll", () => {
    show("check-rolled");
    const roll = document.querySelector(".tx-roll")!;
    expect(within(roll as HTMLElement).getByText("Success")).toBeTruthy();
    expect(roll.textContent).toMatch(/16\s*\+\s*0\s*=\s*16\s*vs DC\s*15/);
    expect(within(roll as HTMLElement).getByText(/Player3 rolled/)).toBeTruthy();
    expect(screen.queryAllByRole("button").filter((b) => b.closest(".tx-check"))).toEqual([]);
  });
});

describe("a search's check (interactables design §7.4)", () => {
  it("shows no DC while the party picks who rolls", () => {
    show("check-search");
    const head = document.querySelector(".tx-check-head")!;
    expect(head.textContent).toContain("DC hidden");
    expect(head.textContent).toContain("Search the area");
    expect(head.textContent).not.toMatch(/DC \d/);
  });

  it("rolls with no DC, and says what it found", () => {
    show("check-search-rolled");
    const roll = document.querySelector(".tx-roll")! as HTMLElement;
    expect(roll.textContent).toMatch(/3\s*\+\s*5\s*=\s*8/);
    expect(roll.textContent).not.toContain("vs DC");
    expect(within(roll).getByText("Nothing found")).toBeTruthy();
    expect(roll.getAttribute("aria-label")).not.toContain("DC");
    cleanup();
    show("check-search-rolled", (d) => { (d.roll as { success: boolean }).success = true; });
    expect(within(document.querySelector(".tx-roll")! as HTMLElement).getByText("Found something")).toBeTruthy();
  });
});

describe("alerts", () => {
  it("a who-rolls question right after a vote alerts too: the quiet window is the vote's, not every question's", () => {
    const p = phone();
    p.to(view("vote"));
    expect(p.alerts()).toBe(1);
    expect(document.querySelector(".tx-ask.arrived")).not.toBeNull();
    vi.advanceTimersByTime(2500);
    p.to(view("check-pick", (d) => { d.canChoose = true; }, "v2"));
    expect(p.alerts()).toBe(2);
    expect(document.querySelector(".tx-check.arrived")).not.toBeNull();
  });

  it("a reconnect re-sending the same question stays quiet and still", () => {
    const p = phone();
    p.to(view("check-pick", (d) => { d.canChoose = true; }));
    expect(p.alerts()).toBe(1);
    vi.advanceTimersByTime(1000);
    p.remount(view("check-pick", (d) => { d.canChoose = true; }, "v2"));
    expect(p.alerts()).toBe(1);
    expect(document.querySelector(".tx-check.arrived")).toBeNull();
    // Long after, it's an arrival again (a reload, say).
    vi.advanceTimersByTime(5000);
    p.remount(view("check-pick", (d) => { d.canChoose = true; }, "v3"));
    expect(p.alerts()).toBe(2);
  });

  it("a question straight after another is a new screen: it alerts, and the same question's next view doesn't", () => {
    const p = phone();
    p.to(view("vote"));
    p.to(view("vote", (d) => { d.mine = "parley"; }, "v2"));
    expect(p.alerts()).toBe(1);
    const first = document.querySelector(".tx-ask");
    p.to(view("choice-leader", undefined, "v3"));
    expect(p.alerts()).toBe(2);
    expect(document.querySelector(".tx-ask")).not.toBe(first);
    expect(document.querySelector(".tx-ask.arrived")).not.toBeNull();
  });

  it("a question that reuses the last one's option ids is still a new question", () => {
    const p = phone();
    p.to(view("vote"));
    p.to(view("vote", (d) => { (d.options as { label: string }[])[0]!.label = "We'll hold the ford."; }, "v2"));
    expect(p.alerts()).toBe(2);
  });

  it("two skill checks in a row are two questions; the same check's next view is not", () => {
    const p = phone();
    const pick = (change?: (d: Record<string, unknown>) => void) => (d: Record<string, unknown>) => { d.canChoose = true; change?.(d); };
    p.to(view("check-pick", pick()));
    p.to(view("check-pick", pick((d) => { d.mine = "pr_member2"; }), "v2"));
    expect(p.alerts()).toBe(1);
    p.to(view("check-pick", pick((d) => { d.skill = "athletics"; d.title = "We'll swim it."; }), "v3"));
    expect(p.alerts()).toBe(2);
  });

  it("a card added mid-check (someone got a character) is the same check: no alert, no remount, and a held tap still goes", () => {
    const p = phone();
    const pick = (change?: (d: Record<string, unknown>) => void) => (d: Record<string, unknown>) => { d.canChoose = true; d.mine = null; change?.(d); };
    p.to(view("check-pick", pick()));
    expect(p.alerts()).toBe(1);
    const first = document.querySelector(".tx-check");
    act(() => {
      screen.getByRole("button", { name: /Player2/ }).click();
      screen.getByRole("button", { name: /Player3/ }).click();      // held behind the first, for the next view
    });
    vi.advanceTimersByTime(500);
    // Buzzes so far: the alert, and the taps' own feedback. From here on, only a send's tap cue may buzz.
    const [buzzed, sent] = [p.alerts(), p.send.mock.calls.length];
    p.to(view("check-pick", pick((d) => {
      const cards = d.cards as { id: string; name: string; player: string }[];
      cards.push({ ...cards[0]!, id: "pr_member5", name: "Player5", player: "Player5" });
      d.mine = "pr_member2";
    }), "v2"));

    expect(p.alerts() - buzzed).toBe(p.send.mock.calls.length - sent);     // no alert
    expect(document.querySelector(".tx-check")).toBe(first);
    expect(screen.getByRole("button", { name: /Player5/ })).toBeTruthy();
    expect(p.send.mock.calls.map((c) => c[0])).toEqual([
      { action: "pickRoller", data: { member: "pr_member2" } },
      { action: "pickRoller", data: { member: "pr_member3" } },
    ]);
    expect(pressed(screen.getByRole("button", { name: /Player3/ }))).toBe("true");
  });

  it("the same check rolled is a new screen, and so is its tie-break", () => {
    const p = phone();
    p.to(view("check-pick", (d) => { d.canChoose = true; }));
    const first = document.querySelector(".tx-check");
    p.to(view("check-pick", (d) => { d.canChoose = true; d.tie = true; d.decider = "Player1"; d.tied = ["pr_member2", "pr_member3"]; }, "v2"));
    const tie = document.querySelector(".tx-check");
    expect(tie).not.toBe(first);
    p.to(view("check-rolled", undefined, "v3"));
    expect(document.querySelector(".tx-check")).not.toBe(tie);
  });

  it("a tap held behind question 1 never goes out on question 2", () => {
    const p = phone();
    p.to(view("vote"));
    act(() => {
      option(/parley with the chief/).click();
      option(/what's it worth/).click();
    });
    p.to(view("choice-leader", undefined, "v2"));
    vi.advanceTimersByTime(QUEUE_MS + 100);
    expect(p.send.mock.calls.map((c) => c[0])).toEqual([{ action: "vote", data: { option: "parley" } }]);
    expect(screen.getAllByRole("button").filter((b) => b.closest(".tx-options") && pressed(b) === "true")).toEqual([]);
  });

  it("a tap held when the vote closes in a tie is dropped: the decider breaks it from the tie-break's view", () => {
    const p = phone();
    // A two-way vote that ties: the tie-break offers the very same options.
    p.to(view("vote", (d) => { d.mine = null; d.options = (d.options as unknown[]).slice(0, 2); }));
    act(() => {
      option(/clear the ford by force/).click();
      option(/parley with the chief/).click();
    });
    p.to(view("vote-tie", (d) => { d.mine = "force"; }, "v2"));
    vi.advanceTimersByTime(QUEUE_MS + 100);
    expect(p.send.mock.calls.map((c) => c[0])).toEqual([{ action: "vote", data: { option: "force" } }]);
    expect(pressed(option(/parley with the chief/))).toBe("false");
    expect(within(option(/clear the ford by force/)).getByText("You voted this earlier")).toBeTruthy();
  });

  it("a tie-break landing on the decider's phone alerts once; on anyone else's, not at all", () => {
    const p = phone();
    p.to(view("vote", (d) => { d.options = (d.options as unknown[]).slice(0, 2); }));
    vi.advanceTimersByTime(3000);
    p.to(view("vote-tie", undefined, "v2"));
    expect(p.alerts()).toBe(2);
    p.to(view("vote-tie", (d) => { d.mine = "parley"; }, "v3"));
    expect(p.alerts()).toBe(2);
    cleanup();

    const q = phone();
    q.to(view("vote"));
    q.to(view("vote-tie", (d) => { d.canChoose = false; }, "v2"));
    expect(q.alerts()).toBe(1);
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

  it("a tapped Interact sleeps without dropping keyboard focus", () => {
    const { send } = show("walk-interact");
    const talk = screen.getByRole("button", { name: "Talk" }) as HTMLButtonElement;
    talk.focus();
    fireEvent.click(talk);
    expect(talk.getAttribute("aria-disabled")).toBe("true");
    expect(talk.disabled).toBe(false);
    expect(document.activeElement).toBe(talk);
    // Asleep, a tap or Enter on it sends nothing more.
    fireEvent.click(talk);
    expect(send.mock.calls.filter((c) => (c[0] as { action: string }).action === "interact").length).toBe(1);
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
    // It keeps pinging while held: more walks go out across the wait, every one north.
    const before = send.mock.calls.length;
    vi.advanceTimersByTime(500);
    const pings = send.mock.calls.slice(before).map((c) => c[0]);
    expect(pings.length).toBeGreaterThanOrEqual(2);
    expect(pings).toEqual(pings.map(() => ({ action: "walk", data: { dir: "n" } })));
    expect(screen.getByRole("button", { name: "Talk" })).toBeTruthy();
  });
});

describe("the VIP's End session in a scene", () => {
  /** The host menu's places, in order: with no battle list the first is an empty slot, as on the walk screen. */
  const places = () => [...document.querySelector(".tx-walk-host")!.children];

  it.each([["dialogue-leader"], ["choice-leader"], ["vote-tie"]])("is on the VIP's %s, and on nobody else's", (name) => {
    show(name);
    expect(screen.getByRole("button", { name: "End session" })).toBeTruthy();
    cleanup();
    show(name, (d) => { d.vip = false; });
    expect(screen.queryByRole("button", { name: "End session" })).toBeNull();
  });

  it.each([["dialogue"], ["vote"], ["check-pick"], ["check-rolled"]])("isn't on a player's %s", (name) => {
    show(name);
    expect(screen.queryByRole("button", { name: "End session" })).toBeNull();
  });

  it.each([["check-pick"], ["check-rolled"]])("is on a skill check's screen for the VIP (%s)", (name) => {
    show(name, (d) => { d.vip = true; });
    expect(screen.getByRole("button", { name: "End session" })).toBeTruthy();
  });

  it.each([["dialogue-leader", ".tx-story-foot"], ["choice-leader", ".tx-options"], ["vote-tie", ".tx-options"]])(
    "heads %s, above the thumbs (%s), and isn't inside them", (name, thumbs) => {
      show(name);
      const menu = document.querySelector(".tx-walk-host")!;
      const root = menu.parentElement!;
      expect(root.classList.contains("tx")).toBe(true);
      expect(root.firstElementChild).toBe(menu);
      const under = document.querySelector(thumbs)!;
      expect(under.contains(menu)).toBe(false);
      expect(menu.compareDocumentPosition(under) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

  it("asks first: \"Keep playing\" takes the place of the first tap, and yes ends the session", () => {
    const { send } = show("dialogue-leader");
    const at = places().indexOf(screen.getByRole("button", { name: "End session" }));
    fireEvent.click(screen.getByRole("button", { name: "End session" }));
    expect(send).not.toHaveBeenCalledWith({ action: "endSession" });
    expect(places()[at]!.textContent).toBe("Keep playing");
    fireEvent.click(places()[at]!);       // a double tap lands on "Keep playing"
    expect(screen.queryByRole("button", { name: "Yes, end it" })).toBeNull();
    expect(send).not.toHaveBeenCalledWith({ action: "endSession" });

    fireEvent.click(screen.getByRole("button", { name: "End session" }));
    fireEvent.click(screen.getByRole("button", { name: "Yes, end it" }));
    expect(send).toHaveBeenLastCalledWith({ action: "endSession" });
  });

  it("works from a question too, and doesn't answer it", () => {
    const { send } = show("choice-leader");
    fireEvent.click(screen.getByRole("button", { name: "End session" }));
    fireEvent.click(screen.getByRole("button", { name: "Yes, end it" }));
    expect(send.mock.calls.map((c) => (c[0] as { action: string }).action)).toEqual(["endSession"]);
  });

  it("lets its confirm lapse by itself", () => {
    show("dialogue-leader");
    fireEvent.click(screen.getByRole("button", { name: "End session" }));
    act(() => { vi.advanceTimersByTime(5000); });
    expect(screen.queryByRole("button", { name: "Yes, end it" })).toBeNull();
    expect(screen.getByRole("button", { name: "End session" })).toBeTruthy();
  });
});
