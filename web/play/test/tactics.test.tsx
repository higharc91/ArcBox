import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import type { CustomView } from "../src/protocol";
import { loadModule } from "../src/games/registry";
import { costText } from "../src/games/tactics/screens";
import type { TurnData } from "../src/games/tactics/types";
import { ControllerScreen } from "../src/widgets/ControllerScreen";

// Real views from a real session, written by games/Tactics/Tactics.Game.Tests/PhoneFixtureTests.cs.
const fixtures = import.meta.glob<{ default: unknown }>("./fixtures/tactics/*.json", { eager: true });
const view = (name: string): CustomView => {
  const mod = fixtures[`./fixtures/tactics/${name}.json`];
  if (!mod) throw new Error(`missing fixture tactics/${name}`);
  return structuredClone(mod.default) as CustomView;
};
const show = (name: string) => {
  const send = vi.fn();
  render(<ControllerScreen view={view(name)} send={send} me="ALEX" game="tactics" />);
  return send;
};

beforeAll(async () => {
  await loadModule("tactics");
});
afterEach(cleanup);

describe("tactics phone module", () => {
  it("renders every screen the game sends", () => {
    const names = Object.keys(fixtures).map((k) => k.replace("./fixtures/tactics/", "").replace(".json", "")).sort();
    expect(names).toEqual(["pickClass", "pickEncounter", "react", "results", "status", "turn"]);
    for (const name of names) {
      render(<ControllerScreen view={view(name)} send={() => {}} me="ALEX" game="tactics" />);
      expect(screen.queryByText("This screen needs a newer app")).toBeNull();
      cleanup();
    }
  });

  it("muster: tapping a class picks it", () => {
    const send = show("pickClass");
    fireEvent.click(screen.getByRole("button", { name: /Fighter/ }));
    expect(send).toHaveBeenLastCalledWith({ action: "pickClass", data: { class: "fighter" } });
  });

  it("encounter pick: the VIP taps a battle that fits the party", () => {
    const send = show("pickEncounter");
    fireEvent.click(screen.getByRole("button", { name: /River Crossing/ }));
    expect(send).toHaveBeenLastCalledWith({ action: "pickEncounter", data: { encounter: "river_crossing" } });
  });

  it("turn: a reachable tile moves along the server's path, and nothing is sent while browsing", () => {
    const send = show("turn");
    const turn = view("turn").data as unknown as TurnData;
    const first = turn.choices.moves[0]!;

    expect((screen.getByRole("button", { name: "Pick a tile to move" }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: `${first.to[0]}, ${first.to[1]}` }));
    expect(send).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: `Move here · ${first.cost}` }));
    expect(send).toHaveBeenLastCalledWith({ action: "move", data: { path: first.path } });
  });

  it("turn: an ability with one target uses it, and End turn ends it", () => {
    const send = show("turn");
    fireEvent.click(screen.getByRole("button", { name: /^Dash/ }));
    fireEvent.click(screen.getByRole("button", { name: "Use" }));
    expect(send).toHaveBeenLastCalledWith({ action: "use", data: { ability: "common.dash" } });

    fireEvent.click(screen.getByRole("button", { name: "Back" }));
    fireEvent.click(screen.getByRole("button", { name: "End turn" }));
    expect(send).toHaveBeenLastCalledWith({ action: "endTurn" });
  });

  it("turn: an area spell can be aimed at a bare tile picked on the map", () => {
    const v = view("turn");
    const data = v.data as unknown as TurnData;
    const [x, y] = data.me.at;
    data.choices.abilities = [{
      ability: "wizard.fireball", item: null, name: "Fireball", tooltip: "Boom", cost: { action: 1, resource: { mana: 3 } },
      targets: [], tiles: [[x + 1, y]],
    }];
    const send = vi.fn();
    render(<ControllerScreen view={v} send={send} me="ALEX" game="tactics" />);
    fireEvent.click(screen.getByRole("button", { name: /^Fireball/ }));
    // A single legal target is picked for you; the map marks it as an aim tile.
    expect(document.querySelectorAll(".tx-cell.aim")).toHaveLength(1);
    fireEvent.click(screen.getByRole("button", { name: "Use" }));
    expect(send).toHaveBeenLastCalledWith({ action: "use", data: { ability: "wizard.fireball", tile: [x + 1, y] } });
  });

  it("turn: the d-pad moves the cursor off your own tile", () => {
    show("turn");
    const cursor = () => document.querySelector(".tx-cell.cursor")!;
    expect(cursor().className).toContain("me");
    fireEvent.click(screen.getByRole("button", { name: "Right" }));
    expect(cursor().className).not.toContain(" me");
    fireEvent.click(screen.getByRole("button", { name: "Left" }));
    expect(cursor().className).toContain("me");
  });

  it("react: a reaction answers the offer", () => {
    const send = show("react");
    expect(screen.getByText("You're about to be hit")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Shield" }));
    expect(send).toHaveBeenLastCalledWith({ action: "react", data: { choice: "wizard.shield" } });
  });

  it("results: the VIP goes on to the next battle", () => {
    const send = show("results");
    expect(screen.getByText("Victory")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Next battle" }));
    expect(send).toHaveBeenLastCalledWith({ action: "next" });
  });

  it("status: says who is acting and when you're up", () => {
    show("status");
    expect(screen.getByText("Player3 is acting")).toBeTruthy();
    expect(screen.getByText("You're up in 9")).toBeTruthy();
  });

  it("costText names the parts of a cost", () => {
    expect(costText({ action: 1, resource: { mana: 3 }, concentration: true })).toBe("Action · 3 Mana · Concentration");
    expect(costText({})).toBe("Free");
  });
});
