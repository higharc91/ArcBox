import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import type { CustomView } from "../src/protocol";
import { loadModule } from "../src/games/registry";
import { costText } from "../src/games/tactics/screens";
import type { StatusData, TurnData } from "../src/games/tactics/types";
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
    const names = Object.keys(fixtures).map((k) => k.replace("./fixtures/tactics/", "").replace(".json", ""))
      .filter((n) => !n.startsWith("portal-")).sort();
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

  it("encounter pick: a battle that needs more heroes says how many AI companions join", () => {
    const v = view("pickEncounter");
    const data = v.data as unknown as { encounters: { name: string; companions?: number }[] };
    data.encounters[0]!.companions = 2;
    render(<ControllerScreen view={v} send={() => {}} me="ALEX" game="tactics" />);
    expect(screen.getByText(/\+2 AI companions/)).toBeTruthy();
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

  it("turn: picking an ability goes back to the top, where Use and Back are", () => {
    const scrollTo = vi.spyOn(window, "scrollTo").mockImplementation(() => {});
    show("turn");
    fireEvent.click(screen.getByRole("button", { name: /^Dash/ }));
    expect(scrollTo).toHaveBeenCalledWith(0, 0);
    scrollTo.mockRestore();
  });

  it("turn: Delay keeps its place once it's gone, so End turn doesn't move under your thumb", () => {
    const v = view("turn");
    (v.data as unknown as TurnData).choices.canDelay = false;
    render(<ControllerScreen view={v} send={vi.fn()} me="ALEX" game="tactics" />);
    expect((screen.getByRole("button", { name: "Delay" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("turn: Attack comes first, then the class's own abilities, the common actions and items", () => {
    const v = view("turn");
    const data = v.data as unknown as TurnData;
    const ability = (id: string, name: string, item: string | null = null) =>
      ({ ability: id, item, name, tooltip: "", cost: {}, targets: [], tiles: [] });
    data.choices.abilities = [
      ability("common.dash", "Dash"), ability("common.use_item", "Potion", "potion"), ability("common.attack", "Attack"),
      ability("wizard.sleep", "Sleep"),
    ];
    render(<ControllerScreen view={v} send={vi.fn()} me="ALEX" game="tactics" />);
    expect([...document.querySelectorAll(".tx-abilities b")].map((b) => b.textContent)).toEqual(["Attack", "Sleep", "Dash", "Potion"]);
  });

  it("turn: an area target says how many it hits, not its coordinates", () => {
    const v = view("turn");
    const data = v.data as unknown as TurnData;
    const hit = (unit: string) => ({ unit, damage: 0, heal: 0 });
    data.choices.abilities = [{
      ability: "wizard.sleep", item: null, name: "Sleep", tooltip: "", cost: { action: 1 }, tiles: [],
      targets: [
        { target: { unit: null, tile: [8, 6] }, aim: [8, 6], affected: [hit(data.units[0]!.id), hit(data.units[1]!.id)] },
        { target: { unit: null, tile: [7, 5] }, aim: [7, 5], affected: [hit(data.units[0]!.id)] },
      ],
    }];
    render(<ControllerScreen view={v} send={vi.fn()} me="ALEX" game="tactics" />);
    fireEvent.click(screen.getByRole("button", { name: /^Sleep/ }));
    expect(screen.getByRole("button", { name: /^2 targets/ })).toBeTruthy();
    expect(screen.getByRole("button", { name: /^1 target/ })).toBeTruthy();
    expect(screen.queryByRole("button", { name: /^\(8, 6\)/ })).toBeNull();
  });

  it("status: conditions read as words, not internal ids", () => {
    const v = view("status");
    (v.data as unknown as StatusData).me.conditions = ["statMod.wizard.shield", "prone", "hunters_mark"];
    render(<ControllerScreen view={v} send={vi.fn()} me="ALEX" game="tactics" />);
    expect(screen.getByText("shield, prone, hunters mark")).toBeTruthy();
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
    fireEvent.click(screen.getByRole("button", { name: /^Shield/ }));
    expect(send).toHaveBeenLastCalledWith({ action: "react", data: { choice: "wizard.shield" } });
  });

  it("react: says who is hitting whom with what roll, and what each reaction does", () => {
    show("react");
    expect(screen.getByText("Goblin's Attack is hitting you: 16 against AC 11")).toBeTruthy();
    expect(screen.getByText("Self: you get +5 AC until the end of your turn")).toBeTruthy();
  });

  it("turn: the economy chips are spelled out, and a cone asks for a direction", () => {
    const v = view("turn");
    const data = v.data as unknown as TurnData;
    const [x, y] = data.me.at;
    data.choices.abilities = [{
      ability: "wizard.burning_hands", item: null, name: "Burning Hands", tooltip: "Cone", cost: { action: 1 },
      targets: [], tiles: [[x + 1, y], [x - 1, y]], aim: "direction",
    }];
    render(<ControllerScreen view={v} send={() => {}} me="ALEX" game="tactics" />);
    expect(screen.getByTitle("Your action").textContent).toBe("Action");
    expect(screen.getByTitle("Tiles of movement left").textContent).toBe(`Move ${data.choices.economy.moveLeft}`);
    fireEvent.click(screen.getByRole("button", { name: /^Burning Hands/ }));
    expect(screen.getByRole("button", { name: "Pick a direction" })).toBeTruthy();
  });

  it("turn: the same view sent again keeps your selection; a new one clears it", () => {
    const v = view("turn");
    const { rerender } = render(<ControllerScreen view={v} send={() => {}} me="ALEX" game="tactics" />);
    fireEvent.click(screen.getByRole("button", { name: /^Dash/ }));
    expect(screen.getByRole("button", { name: "Back" })).toBeTruthy();

    rerender(<ControllerScreen view={structuredClone(v)} send={() => {}} me="ALEX" game="tactics" />);   // a reconnect re-sends it
    expect(screen.getByRole("button", { name: "Back" })).toBeTruthy();

    const moved = structuredClone(v);
    (moved.data as unknown as TurnData).choices.economy.moveLeft = 0;
    rerender(<ControllerScreen view={moved} send={() => {}} me="ALEX" game="tactics" />);
    expect(screen.queryByRole("button", { name: "Back" })).toBeNull();
  });

  it("class pick and results link to the campaign portal", () => {
    show("pickClass");
    expect(screen.getByRole("link", { name: "make your own character" }).getAttribute("href")).toBe("/c/ABCD");
    cleanup();
    show("results");
    expect(screen.getByRole("link", { name: /Your character/ }).getAttribute("href")).toBe("/c/ABCD");
  });

  it("results: the VIP goes on to the next battle", () => {
    const send = show("results");
    expect(screen.getByText("Victory")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Next battle" }));
    expect(send).toHaveBeenLastCalledWith({ action: "next" });
  });

  it("status: says who is acting and when you're up", () => {
    show("status");
    // The "Now acting" card: its label, then the acting unit's name.
    expect(screen.getByText("Now acting").nextElementSibling!.textContent).toBe("Player3");
    expect(screen.getByText("You're up in 9")).toBeTruthy();
  });

  it("costText names the parts of a cost", () => {
    expect(costText({ action: 1, resource: { mana: 3 }, concentration: true })).toBe("Action · 3 Mana · Concentration");
    expect(costText({})).toBe("Free");
  });
});
