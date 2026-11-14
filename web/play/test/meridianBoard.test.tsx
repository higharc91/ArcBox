import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/preact";
import { MeridianBoard, isMeridianHostView, tileLabel, type MeridianHostView } from "../src/dev/MeridianBoard";
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

  it("signals each presented event once, just before its fallback", () => {
    vi.useFakeTimers();
    const signal = vi.fn();
    const { rerender } = render(<MeridianBoard view={view()} signal={signal} />);
    vi.advanceTimersByTime(view().now!.seconds * 1000 - 250);
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

  it("labels tiles from their ids", () => expect(tileLabel("waste_reclamation")).toBe("Waste Reclamation"));
});
