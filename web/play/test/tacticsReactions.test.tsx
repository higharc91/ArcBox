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

describe("the Reactions sheet (#113)", () => {
  it("is closed until asked for, on the waiting screen and on a turn", () => {
    show("status");
    expect(screen.getByRole("button", { name: "Reactions" })).toBeTruthy();
    expect(screen.queryByText("Opportunity attacks")).toBeNull();
    cleanup();
    show("turn");
    expect(screen.getByRole("button", { name: "Reactions" })).toBeTruthy();
  });

  it("shows the current choices, Ask by default", () => {
    show("status");
    fireEvent.click(screen.getByRole("button", { name: "Reactions" }));
    expect(screen.getByText("Opportunity attacks")).toBeTruthy();
    expect(screen.getByText("Shield and other defensive reactions")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Ask · opportunity attacks" }).getAttribute("aria-pressed")).toBe("true");
    expect(screen.getByRole("button", { name: "Ask · defensive reactions" }).getAttribute("aria-pressed")).toBe("true");
  });

  it("reflects saved choices and sends one change at a time", () => {
    const send = show("status", (d) => { d.reactions = { opportunity: "always", defence: "miss" }; });
    fireEvent.click(screen.getByRole("button", { name: "Reactions" }));
    expect(screen.getByRole("button", { name: "Always · opportunity attacks" }).getAttribute("aria-pressed")).toBe("true");
    expect(screen.getByRole("button", { name: "Only when it changes the outcome · defensive reactions" }).getAttribute("aria-pressed")).toBe("true");

    fireEvent.click(screen.getByRole("button", { name: "Never · opportunity attacks" }));
    expect(send).toHaveBeenLastCalledWith({ action: "reactionPrefs", data: { opportunity: "never" } });
    fireEvent.click(screen.getByRole("button", { name: "Never · defensive reactions" }));
    expect(send).toHaveBeenLastCalledWith({ action: "reactionPrefs", data: { defence: "never" } });
    fireEvent.click(screen.getByRole("button", { name: "Ask · defensive reactions" }));
    expect(send).toHaveBeenLastCalledWith({ action: "reactionPrefs", data: { defence: "ask" } });
  });

  it("offers nothing when the server sends no settings", () => {
    show("status", (d) => { delete d.reactions; });
    expect(screen.queryByRole("button", { name: "Reactions" })).toBeNull();
  });
});
