import { afterEach, beforeAll, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/preact";
import type { CustomView } from "../src/protocol";
import type { TurnData, UnitView } from "../src/games/tactics/types";
import { ControllerScreen } from "../src/widgets/ControllerScreen";
import { loadModule } from "../src/games/registry";
import fixture from "./fixtures/tactics/turn.json";

beforeAll(async () => { await loadModule("tactics"); });
afterEach(cleanup);
function setup(edit: (data: TurnData) => void = () => {}) {
  const view = structuredClone(fixture) as CustomView;
  const data = view.data as unknown as TurnData;
  const me: UnitView = { ...data.me, id: "hero1", name: "Kara", at: [0, 0] };
  const foe = (id: string, at: [number, number]): UnitView => ({ ...me, id, name: "Goblin", side: "Enemy", at, hp: 6, maxHp: 10 });
  data.me = me;
  data.units = [me, foe("goblin.1", [9, 1]), foe("goblin.2", [8, 1])];
  data.terrain = { w: 10, h: 3, heights: Array<number>(30).fill(0), flags: Array<number>(30).fill(0), tileMaterial: Array<number>(30).fill(0), materials: ["stone/grey"], zones: [] };
  data.terrain.heights[2] = 4;
  data.terrain.flags[3] = 8;
  data.terrain.flags[4] = 20;
  data.terrain.flags[7] = 1;
  data.terrain.zones = [{ name: "Burning", tiles: [[5, 0]], hazard: true, detail: "1d6 fire damage on entry." }, { name: "Healing Grove", tiles: [[6, 0]], hazard: false, detail: "Restores allied HP." }];
  data.choices.abilities = [{ ability: "common.attack", item: null, name: "Attack", tooltip: "Weapon attack", cost: { action: 1 }, aim: "unit", tiles: [],
    targets: data.units.slice(1).map(u => ({ target: { unit: u.id, tile: null }, aim: u.at, affected: [{ unit: u.id, damage: 4, heal: 0 }] })) }];
  edit(data);
  const send = vi.fn();
  render(<ControllerScreen view={view} send={send} me="KARA" game="tactics" />);
  return { data, send };
}
const selected = () => screen.getByRole("region", { name: "Selected tile" });
const units = () => fireEvent.click(screen.getByRole("button", { name: "Units (3)", exact: true }));

it("distinguishes identical names using map numbers and an HP roster", () => {
  const { send } = setup();
  expect(screen.getByRole("button", { name: "#2 Goblin, Enemy", exact: true }).textContent).toBe("#2");
  expect(screen.getByRole("button", { name: "#3 Goblin, Enemy", exact: true }).textContent).toBe("#3");
  units();
  const dialog = screen.getByRole("dialog", { name: "Battlefield units" });
  expect(within(dialog).getByRole("button", { name: /^#2 Goblin/ }).textContent).toContain("6/10");
  fireEvent.click(within(dialog).getByRole("button", { name: /^#3 Goblin/ }));
  expect(selected().textContent).toContain("#3 Goblin");
  expect(send).not.toHaveBeenCalled();
});

it("shows heights, dangerous cliff edges, deep ground and active hazards without a pack request", () => {
  const { send } = setup();
  const cliff = screen.getByRole("button", { name: "2, 0", exact: true });
  expect(cliff.classList.contains("cliff-right")).toBe(true);
  fireEvent.click(cliff);
  expect(selected().textContent).toContain("height 4");
  expect(selected().textContent).toContain("East: drop 4 levels · fall damage");
  fireEvent.click(screen.getByRole("button", { name: "4, 0", exact: true }));
  expect(selected().textContent).toContain("Deep ground");
  const fire = screen.getByRole("button", { name: "5, 0", exact: true });
  expect(fire.classList.contains("hazard")).toBe(true);
  fireEvent.click(fire);
  expect(selected().textContent).toContain("1d6 fire damage");
  const healing = screen.getByRole("button", { name: "6, 0", exact: true });
  expect(healing.classList.contains("hazard")).toBe(false);
  expect(healing.textContent).toBe("Z");
  fireEvent.click(screen.getByRole("button", { name: "7, 0", exact: true }));
  expect(within(selected()).getByText("Water", { exact: true })).toBeTruthy();
  expect(send).not.toHaveBeenCalled();
});

it("reaches a distant target in three taps and commits only that unit", () => {
  const { send } = setup();
  fireEvent.click(screen.getByRole("button", { name: /^Attack/ }));
  units();
  fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: /^#3 Goblin/ }));
  const use = screen.getByRole("button", { name: "Use on #3 Goblin", exact: true });
  expect(send).not.toHaveBeenCalled();
  fireEvent.click(use);
  expect(send).toHaveBeenCalledExactlyOnceWith({ action: "use", data: { ability: "common.attack", unit: "goblin.2" } });
});

it("keeps the chosen unit when two units share a tile", () => {
  const { send } = setup(data => {
    data.units[2]!.at = data.units[1]!.at;
    data.choices.abilities[0]!.targets[1]!.aim = data.units[1]!.at;
  });
  fireEvent.click(screen.getByRole("button", { name: /^Attack/ }));
  units();
  fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: /^#3 Goblin/ }));
  expect(selected().textContent).toContain("#2 Goblin");
  expect(selected().textContent).toContain("#3 Goblin");
  fireEvent.click(screen.getByRole("button", { name: "Use on #3 Goblin", exact: true }));
  expect(send).toHaveBeenCalledExactlyOnceWith({ action: "use", data: { ability: "common.attack", unit: "goblin.2" } });
});

it("inspecting an illegal unit does not silently keep the sole legal target", () => {
  const { send } = setup(data => { data.choices.abilities[0]!.targets = data.choices.abilities[0]!.targets.slice(0, 1); });
  fireEvent.click(screen.getByRole("button", { name: /^Attack/ }));
  expect(screen.getByRole("button", { name: "Use on #2 Goblin", exact: true })).toBeTruthy();
  units();
  fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: /^#3 Goblin/ }));
  expect((screen.getByRole("button", { name: "Pick a target" }) as HTMLButtonElement).disabled).toBe(true);
  expect(send).not.toHaveBeenCalled();
});

it("a unit shortcut for an area uses a legal aim tile instead of inventing a unit target", () => {
  const { send } = setup(data => {
    const ability = data.choices.abilities[0]!;
    Object.assign(ability, { ability: "wizard.fireball", name: "Fireball", aim: "tile", targets: [{ target: { unit: null, tile: [7, 1] }, aim: [7, 1], affected: [{ unit: "goblin.2", damage: 12, heal: 0 }] }] });
  });
  fireEvent.click(screen.getByRole("button", { name: /^Fireball/ }));
  units();
  fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: /^#3 Goblin/ }));
  fireEvent.click(screen.getByRole("button", { name: "Use", exact: true }));
  expect(send).toHaveBeenCalledExactlyOnceWith({ action: "use", data: { ability: "wizard.fireball", tile: [7, 1] } });
});

it("enlarged map selection is local and survives closing the sheet", () => {
  const { send } = setup();
  fireEvent.click(screen.getByRole("button", { name: "Enlarge map" }));
  const dialog = screen.getByRole("dialog", { name: "Enlarged battlefield" });
  fireEvent.click(within(dialog).getByRole("button", { name: "9, 2", exact: true }));
  expect(within(dialog).getByRole("region", { name: "Selected tile" }).textContent).toContain("Tile 9, 2");
  fireEvent.click(within(dialog).getByRole("button", { name: "Done inspecting" }));
  expect(screen.queryByRole("dialog")).toBeNull();
  expect(selected().textContent).toContain("Tile 9, 2");
  expect(send).not.toHaveBeenCalled();
});

it("the d-pad stays within the live map bounds", () => {
  setup();
  fireEvent.click(screen.getByRole("button", { name: "Left", exact: true }));
  fireEvent.click(screen.getByRole("button", { name: "Up", exact: true }));
  expect(selected().textContent).toContain("Tile 0, 0");
  units();
  fireEvent.click(within(screen.getByRole("dialog")).getByRole("button", { name: /^#2 Goblin/ }));
  fireEvent.click(screen.getByRole("button", { name: "Right", exact: true }));
  fireEvent.click(screen.getByRole("button", { name: "Down", exact: true }));
  fireEvent.click(screen.getByRole("button", { name: "Down", exact: true }));
  expect(selected().textContent).toContain("Tile 9, 2");
});
