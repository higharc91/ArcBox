import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/preact";
import { MeridianBoard, isMeridianHostView, signalAfterMs, tileLabel, type MeridianHostView } from "../src/dev/MeridianBoard";
import explore from "./fixtures/meridian-host/explore.json";
import endgame from "./fixtures/meridian-host/endgame.json";
import debrief from "./fixtures/meridian-host/debrief.json";

// A real TV view mid-reveal, written by games/Meridian/Meridian.Game.Tests/PhoneFixtureTests.cs.
const view = () => structuredClone(explore) as unknown as MeridianHostView;

afterEach(() => {
  cleanup();
  vi.useRealTimers();
});

describe("meridian dev board", () => {
  it("recognises a Meridian host view", () => {
    expect(isMeridianHostView(view())).toBe(true);
    expect(isMeridianHostView({ phase: "x", mustered: [] })).toBe(false);
  });

  it("draws the three decks, the seats and what is being presented", () => {
    const { container } = render(<MeridianBoard view={view()} signal={() => {}} />);
    for (const deck of ["Command", "Habitat", "Engineering"]) expect(screen.getByRole("region", { name: deck })).toBeTruthy();
    const seats = container.querySelector(".mer-seats")!.textContent!;
    for (const seat of view().seats) expect(seats).toContain(seat.name);
    expect(screen.getByRole("status").textContent).toContain(view().now!.title);
    expect(screen.getByText("Airlock")).toBeTruthy();
  });

  it("signals each presented event once, after a quick read and before its fallback", () => {
    vi.useFakeTimers();
    const signal = vi.fn();
    const due = signalAfterMs(view().now!);
    expect(due).toBeLessThan(view().now!.seconds * 1000);
    const { rerender } = render(<MeridianBoard view={view()} signal={signal} />);
    vi.advanceTimersByTime(due - 50);
    expect(signal).not.toHaveBeenCalled();
    vi.advanceTimersByTime(100);
    expect(signal).toHaveBeenCalledWith("presented", { seq: view().now!.seq });
    rerender(<MeridianBoard view={view()} signal={signal} />);
    vi.advanceTimersByTime(5000);
    expect(signal).toHaveBeenCalledTimes(1);
  });

  it("signals nothing when nothing is presented", () => {
    vi.useFakeTimers();
    const signal = vi.fn();
    render(<MeridianBoard view={{ ...view(), now: null }} signal={signal} />);
    vi.advanceTimersByTime(10_000);
    expect(signal).not.toHaveBeenCalled();
  });

  it("shows a bot-played seat as auto", () => {
    const v = view();
    v.seats[1] = { ...v.seats[1], connected: false, auto: true };
    render(<MeridianBoard view={v} signal={() => {}} />);
    expect(screen.getByText("auto")).toBeTruthy();
  });

  it("draws the scenario, its rounds and its tokens in the endgame", () => {
    const v = structuredClone(endgame) as unknown as MeridianHostView;
    const { container } = render(<MeridianBoard view={v} signal={() => {}} />);
    const panel = screen.getByRole("region", { name: "Scenario" });
    expect(panel.textContent).toContain(v.scenario!.title);
    expect(panel.textContent).toContain(`${v.scenario!.roundsLeft} rounds left`);
    expect(container.querySelectorAll(".mer-token").length).toBe(v.scenario!.tokens.filter((t) => t.at).length);
    expect(panel.querySelector(".mer-roles")).toBeNull();
  });

  it("reveals every role at the debrief", () => {
    const v = structuredClone(debrief) as unknown as MeridianHostView;
    render(<MeridianBoard view={v} signal={() => {}} />);
    const panel = screen.getByRole("region", { name: "Scenario" });
    expect(panel.textContent).toContain(v.scenario!.outcome!);
    expect(panel.querySelectorAll(".mer-roles li").length).toBe(v.seats.length);
    expect(panel.textContent).toContain("The Mimic");
  });

  it("doesn't wait out a long card's reading time", () => {
    const card = { ...view().now!, kind: "card", title: "Hull Groan", text: Array(40).fill("word").join(" "), seconds: 17 };
    expect(signalAfterMs(card)).toBe(1200 + 80 * 42);
  });

  it("shows who the game is waiting for, and a vote's wait names nobody", () => {
    const { rerender } = render(<MeridianBoard view={{ ...view(), wait: { kind: "grace", seat: "p1", name: "Ann", seconds: 20 } }} signal={() => {}} />);
    expect(screen.getByRole("status", { name: "Waiting" }).textContent).toContain("Waiting for Ann… 20 s");
    rerender(<MeridianBoard view={{ ...view(), wait: { kind: "pause", seconds: 60 } }} signal={() => {}} />);
    expect(screen.getByRole("status", { name: "Waiting" }).textContent).toContain("Waiting for the vote…");
  });

  it("counts the phones back after a restart, naming none", () => {
    render(<MeridianBoard view={{ ...view(), now: null, wait: { kind: "resume", seconds: 42, back: 2 } }} signal={() => {}} />);
    expect(screen.getByRole("status", { name: "Waiting" }).textContent).toContain(`Reconnecting… 2 of ${view().seats.length} phones back 42 s`);
  });

  it("counts ready seats during the briefing", () => {
    render(<MeridianBoard view={{ ...view(), now: null, ready: 2 }} signal={() => {}} />);
    expect(screen.getByText(`2 of ${view().seats.length} ready`)).toBeTruthy();
  });

  it("lists the awards at the debrief", () => {
    const v = structuredClone(debrief) as unknown as MeridianHostView;
    render(<MeridianBoard view={v} signal={() => {}} />);
    for (const a of v.scenario!.awards!) expect(screen.getByText(new RegExp(a.title.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")))).toBeTruthy();
  });

  it("labels tiles from their ids", () => expect(tileLabel("waste_reclamation")).toBe("Waste Reclamation"));
});
