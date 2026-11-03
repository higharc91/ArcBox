import { afterEach, beforeAll, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import type { CustomView } from "../src/protocol";
import { loadModule } from "../src/games/registry";
import { ControllerScreen } from "../src/widgets/ControllerScreen";
import type { TurnData } from "../src/games/tactics/types";
import turnFixture from "./fixtures/tactics/turn.json";
import statusFixture from "./fixtures/tactics/status.json";
import reactFixture from "./fixtures/tactics/react.json";

beforeAll(async () => { await loadModule("tactics"); });
afterEach(cleanup);
it.each([turnFixture, statusFixture, reactFixture])("immerses the current surface on $screen and clears on fresh data", (fixture) => {
  const view = structuredClone(fixture) as CustomView;
  Object.assign(view.data as object, { surfaces: ["burning", "water"] });
  const send = vi.fn();
  const shown = render(<ControllerScreen view={view} send={send} me="ALEX" game="tactics" />);
  expect(screen.getByLabelText("Current terrain: Burning ground, Water")).toBeTruthy();
  expect(document.querySelectorAll(".tx-surface-scene i").length).toBeLessThanOrEqual(24);
  expect(document.querySelector(".tx-surface-scene")?.getAttribute("aria-hidden")).toBe("true");
  expect(document.querySelector(".tx-surface-glare")?.getAttribute("aria-hidden")).toBe("true");
  shown.rerender(<ControllerScreen view={structuredClone(fixture) as CustomView} send={send} me="ALEX" game="tactics" />);
  expect(document.querySelector(".tx-surface-scene")).toBeNull();
  expect(document.querySelector(".tx-surface-glare")).toBeNull();
});
it("warns before moving and commits the exact server path", () => {
  const view = structuredClone(turnFixture) as CustomView;
  const data = view.data as unknown as TurnData;
  const move = data.choices.moves[0]!;
  Object.assign(move, { risk: 3.5, warn: "burns ~4" });
  const send = vi.fn();
  render(<ControllerScreen view={view} send={send} me="ALEX" game="tactics" />);
  const tile = screen.getByRole("button", { name: `${move.to[0]}, ${move.to[1]}` });
  expect(tile.classList.contains("risk")).toBe(true);
  fireEvent.click(tile);
  expect(send).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: `Move here · ${move.cost} · burns ~4` }));
  expect(send).toHaveBeenLastCalledWith({ action: "move", data: { path: move.path } });
});
it("shows selected secondary spread without activating a surface animation", () => {
  const view = structuredClone(turnFixture) as CustomView;
  const data = view.data as unknown as TurnData;
  const ability = data.choices.abilities.find(a => a.ability === "common.dash")!;
  Object.assign(ability.targets[0]!, { spread: [[2, 10], [3, 10]] });
  render(<ControllerScreen view={view} send={() => {}} me="ALEX" game="tactics" />);
  fireEvent.click(screen.getByRole("button", { name: /^Dash/ }));
  expect(document.querySelectorAll(".tx-cell.spread")).toHaveLength(2);
  expect(document.querySelector(".tx-surface-scene")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "Back" }));
  expect(document.querySelectorAll(".tx-cell.spread")).toHaveLength(0);
});

it("web terrain uses corner-anchored silk instead of floating square particles", () => {
  const view = structuredClone(turnFixture) as CustomView;
  Object.assign(view.data as object, { surfaces: ["web"] });
  const shown = render(<ControllerScreen view={view} send={() => {}} me="ALEX" game="tactics" />);
  expect(screen.getByLabelText("Current terrain: Webs")).toBeTruthy();
  expect(document.querySelectorAll(".tx-surface-scene i")).toHaveLength(0);
  expect(document.querySelectorAll(".tx-web-corner svg")).toHaveLength(3);
  shown.rerender(<ControllerScreen view={structuredClone(turnFixture) as CustomView} send={() => {}} me="ALEX" game="tactics" />);
  expect(document.querySelector(".tx-web-corner")).toBeNull();
});
