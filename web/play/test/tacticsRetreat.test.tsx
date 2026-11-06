import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import type { CustomView } from "../src/protocol";
import { loadModule } from "../src/games/registry";
import { ControllerScreen } from "../src/widgets/ControllerScreen";

const fixtures = import.meta.glob<{ default: unknown }>("./fixtures/tactics/*.json", { eager: true });
const show = (name: string, change: (data: Record<string, any>) => void = () => {}) => {
  const mod = fixtures[`./fixtures/tactics/${name}.json`];
  if (!mod) throw new Error(`missing fixture tactics/${name}`);
  const v = structuredClone(mod.default) as CustomView;
  change(v.data as Record<string, any>);
  const send = vi.fn();
  render(<ControllerScreen view={{ ...v, viewId: "v1" }} send={send} me="ALEX" game="tactics" />);
  return send;
};

beforeAll(async () => {
  await loadModule("tactics");
});
afterEach(cleanup);

describe("falling back from a losing battle (#111)", () => {
  it("offers Fall back to a waiting player, who proposes it", () => {
    const send = show("retreat-status");
    fireEvent.click(screen.getByRole("button", { name: "Fall back" }));
    expect(send).toHaveBeenCalledWith({ action: "fallBack", data: undefined });
  });

  it("offers it on the turn screen too, whichever unit is acting", () => {
    const send = show("retreat-turn");
    fireEvent.click(screen.getByRole("button", { name: "Fall back" }));
    expect(send).toHaveBeenCalledWith({ action: "fallBack", data: undefined });
    cleanup();
    show("retreat-turn", (d) => { d.summoned = true; });
    expect(screen.getByRole("button", { name: "Fall back" })).toBeTruthy();
  });

  it("offers nothing before round 3 or when the fight forbids it (the server sends no fallBack)", () => {
    show("status");
    expect(screen.queryByRole("button", { name: "Fall back" })).toBeNull();
    cleanup();
    show("turn");
    expect(screen.queryByRole("button", { name: "Fall back" })).toBeNull();
  });

  it("shows the vote with who proposed it and how many it takes, and votes yes or no", () => {
    const send = show("retreat-vote");
    expect(screen.getByText("Player1 wants to fall back")).toBeTruthy();
    expect(screen.getByText(/1 of 3 needed · 4 here/)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Fall back" }));
    expect(send).toHaveBeenLastCalledWith({ action: "fallBackVote", data: { yes: true } });
    fireEvent.click(screen.getByRole("button", { name: "Stay and fight" }));
    expect(send).toHaveBeenLastCalledWith({ action: "fallBackVote", data: { yes: false } });
  });

  it("marks the proposer's own vote and lets them change it", () => {
    show("retreat-vote-proposer");
    expect(screen.getByText(/You voted to fall back/)).toBeTruthy();
    expect(screen.getByRole("button", { name: "Fall back" }).getAttribute("aria-pressed")).toBe("true");
    expect(screen.queryByRole("button", { name: "Fall back", pressed: false })).toBeNull();
  });

  it("says when the vote failed this round, with no button", () => {
    show("retreat-status", (d) => { d.fallBack = { failed: true }; });
    expect(screen.getByText(/voted to stay and fight/)).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Fall back" })).toBeNull();
  });

  it("shows the vote on a reaction prompt as well", () => {
    show("react", (d) => { d.fallBack = { vote: { proposer: "Sam", yes: 1, no: 0, present: 2, needed: 2, mine: null, until: null } }; });
    expect(screen.getByText("Sam wants to fall back")).toBeTruthy();
  });

  describe("the results", () => {
    const hint = "Too hard? Change difficulty in the party menu";

    it("call a flight a retreat, with half XP shown and the difficulty hint for the VIP", () => {
      show("results-fled");
      expect(screen.getByText("Retreat")).toBeTruthy();
      expect(screen.getAllByText(/\+\d+ XP/).length).toBeGreaterThan(0);
      expect(screen.getByText(hint)).toBeTruthy();
    });

    it("keep the hint away from a Story campaign and from anyone but the VIP", () => {
      show("results-fled", (d) => { d.difficulty = "Story"; });
      expect(screen.queryByText(hint)).toBeNull();
      cleanup();
      show("results-fled", (d) => { d.vip = false; });
      expect(screen.queryByText(hint)).toBeNull();
    });
  });
});
