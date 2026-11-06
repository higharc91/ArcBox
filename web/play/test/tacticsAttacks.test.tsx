import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import type { CustomView } from "../src/protocol";
import { loadModule } from "../src/games/registry";
import type { TurnData } from "../src/games/tactics/types";
import { ControllerScreen } from "../src/widgets/ControllerScreen";

// #107: a weapon attack of several attacks aims each one ("Attack 2 of 3"), each defaulting to the one before.

const fixtures = import.meta.glob<{ default: unknown }>(["./fixtures/tactics/turn.json", "./fixtures/tactics/turn-attacks.json"], { eager: true });

beforeAll(async () => {
  await loadModule("tactics");
});
afterEach(cleanup);

/** The turn fixture with an Attack of three attacks that can go at two goblins, renamed so they can be told apart. */
function threeAttacks() {
  const v = structuredClone(fixtures["./fixtures/tactics/turn.json"]!.default) as CustomView;
  const data = v.data as unknown as TurnData;
  const [grik, snag] = data.units.filter((u) => u.side === "Enemy");
  grik!.name = "Grik";
  snag!.name = "Snag";
  const at = (u: typeof grik) => ({ target: { unit: u!.id, tile: null }, aim: u!.at, affected: [{ unit: u!.id, damage: 9, heal: 0 }] });
  data.choices.abilities = [{
    ability: "common.attack", item: null, name: "Attack", tooltip: "Weapon attack", cost: { action: 1 },
    targets: [at(grik), at(snag)], tiles: [], aim: "unit", attacks: 3,
  }];
  const send = vi.fn();
  render(<ControllerScreen view={v} send={send} me="ALEX" game="tactics" />);
  fireEvent.click(screen.getByRole("button", { name: /^Attack/ }));
  return { send, grik: grik!.id, snag: snag!.id };
}

describe("tactics multi-attack aiming", () => {
  it("Use straight away sends every attack at the one target", () => {
    const { send, grik } = threeAttacks();
    fireEvent.click(screen.getAllByRole("button", { name: "Grik" })[0]!);
    fireEvent.click(screen.getByRole("button", { name: "Use on Grik" }));
    expect(send).toHaveBeenLastCalledWith({ action: "use", data: { ability: "common.attack", unit: grik } });
  });

  it("aims each attack in turn, each starting on the target before it", () => {
    const { send, grik, snag } = threeAttacks();
    expect(screen.queryByRole("button", { name: /Attack 2 of 3/ })).toBeNull();   // nothing picked yet

    fireEvent.click(screen.getAllByRole("button", { name: "Grik" })[0]!);
    fireEvent.click(screen.getByRole("button", { name: /Attack 2 of 3/ }));
    expect(screen.getByText("Aiming attack 2 of 3")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Use on Grik" })).toBeTruthy();   // the second defaults to the first's target

    fireEvent.click(screen.getAllByRole("button", { name: "Snag" })[0]!);
    fireEvent.click(screen.getByRole("button", { name: /Attack 3 of 3/ }));
    expect(screen.queryByRole("button", { name: /Attack 4/ })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: /^Use/ }));

    expect(send).toHaveBeenLastCalledWith({ action: "use", data: { ability: "common.attack", unit: grik, targets: [grik, snag, snag] } });
  });

  it("Use part-way sends the rest at the target being aimed", () => {
    const { send, grik, snag } = threeAttacks();
    fireEvent.click(screen.getAllByRole("button", { name: "Grik" })[0]!);
    fireEvent.click(screen.getByRole("button", { name: /Attack 2 of 3/ }));
    fireEvent.click(screen.getAllByRole("button", { name: "Snag" })[0]!);
    expect(screen.getByRole("button", { name: "Use · 3 attacks" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Use · 3 attacks" }));

    expect(send).toHaveBeenLastCalledWith({ action: "use", data: { ability: "common.attack", unit: grik, targets: [grik, snag, snag] } });
  });

  it("Back steps to the attack before, then out of aiming", () => {
    const { send, grik } = threeAttacks();
    fireEvent.click(screen.getAllByRole("button", { name: "Grik" })[0]!);
    fireEvent.click(screen.getByRole("button", { name: /Attack 2 of 3/ }));
    fireEvent.click(screen.getAllByRole("button", { name: "Snag" })[0]!);

    fireEvent.click(screen.getByRole("button", { name: "Back" }));
    expect(screen.getByText("Aiming attack 1 of 3")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Use on Grik" }));
    expect(send).toHaveBeenLastCalledWith({ action: "use", data: { ability: "common.attack", unit: grik } });

    fireEvent.click(screen.getByRole("button", { name: "Back" }));
    expect(screen.getByRole("button", { name: "End turn" })).toBeTruthy();
  });

  it("the server's view of a hero with two attacks: Attack 2 of 2 at another goblin", () => {
    // turn-attacks.json is written by games/Tactics/Tactics.Game.Tests/PhoneFixtureTests.cs.
    const v = structuredClone(fixtures["./fixtures/tactics/turn-attacks.json"]!.default) as CustomView;
    const data = v.data as unknown as TurnData;
    const attack = data.choices.abilities.find((a) => a.ability === "common.attack")!;
    expect(attack.attacks).toBe(2);
    const [first, second] = attack.targets.map((t) => data.units.find((u) => u.id === t.target.unit)!);
    first!.name = "Grik";
    second!.name = "Snag";
    const send = vi.fn();
    render(<ControllerScreen view={v} send={send} me="ALEX" game="tactics" />);

    fireEvent.click(screen.getByRole("button", { name: /^Attack/ }));
    fireEvent.click(screen.getAllByRole("button", { name: "Grik" })[0]!);
    fireEvent.click(screen.getByRole("button", { name: /Attack 2 of 2/ }));
    fireEvent.click(screen.getAllByRole("button", { name: "Snag" })[0]!);
    fireEvent.click(screen.getByRole("button", { name: "Use · 2 attacks" }));

    expect(send).toHaveBeenLastCalledWith({ action: "use", data: { ability: "common.attack", unit: first!.id, targets: [first!.id, second!.id] } });
  });
});
