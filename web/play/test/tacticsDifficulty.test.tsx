import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import type { CustomView } from "../src/protocol";
import { loadModule } from "../src/games/registry";
import { ControllerScreen } from "../src/widgets/ControllerScreen";

const fixtures = import.meta.glob<{ default: unknown }>("./fixtures/tactics/*.json", { eager: true });
const show = (name: string, change: (data: Record<string, unknown>) => void) => {
  const mod = fixtures[`./fixtures/tactics/${name}.json`];
  if (!mod) throw new Error(`missing fixture tactics/${name}`);
  const v = structuredClone(mod.default) as CustomView;
  change(v.data as Record<string, unknown>);
  const send = vi.fn();
  render(<ControllerScreen view={{ ...v, viewId: "v1" }} send={send} me="ALEX" game="tactics" />);
  return send;
};

beforeAll(async () => {
  await loadModule("tactics");
});
afterEach(cleanup);

describe("the campaign difficulty", () => {
  it("is set from the VIP's session menu, which marks the current one", () => {
    const send = show("walk-leader", (d) => { d.vip = true; d.difficulty = "Normal"; });
    const group = screen.getByRole("group", { name: "Difficulty" });
    expect(group.querySelector('[aria-pressed="true"]')?.textContent).toBe("Normal");

    fireEvent.click(screen.getByRole("button", { name: "Hard" }));
    expect(send).toHaveBeenCalledWith({ action: "difficulty", data: { level: "Hard" } });
    fireEvent.click(screen.getByRole("button", { name: "Normal" }));
    expect(send).toHaveBeenCalledTimes(1);
  });

  it("is not on a phone that isn't the VIP's, or from a server that doesn't send it", () => {
    show("walk-follower", (d) => { d.vip = false; d.difficulty = "Normal"; });
    expect(screen.queryByRole("group", { name: "Difficulty" })).toBeNull();
    cleanup();
    show("walk-leader", (d) => { d.vip = true; delete d.difficulty; });
    expect(screen.queryByRole("group", { name: "Difficulty" })).toBeNull();
  });

  const hint = "Too hard? Change difficulty in the party menu";
  it("is offered after a loss, to the VIP, unless it is Story already", () => {
    show("results", (d) => { d.outcome = "Lost"; d.difficulty = "Normal"; });
    expect(screen.getByText(hint)).toBeTruthy();
    cleanup();
    show("results", (d) => { d.outcome = "Lost"; d.difficulty = "Hard"; });
    expect(screen.getByText(hint)).toBeTruthy();
    cleanup();
    show("results", (d) => { d.outcome = "Lost"; d.difficulty = "Story"; });
    expect(screen.queryByText(hint)).toBeNull();
  });

  it("is not hinted after a win, or to anyone but the VIP", () => {
    show("results", (d) => { d.outcome = "Won"; d.difficulty = "Normal"; });
    expect(screen.queryByText(hint)).toBeNull();
    cleanup();
    show("results", (d) => { d.outcome = "Lost"; d.difficulty = "Normal"; d.vip = false; });
    expect(screen.queryByText(hint)).toBeNull();
  });
});
