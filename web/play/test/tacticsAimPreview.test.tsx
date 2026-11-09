import { afterEach, beforeAll, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import { loadModule } from "../src/games/registry";
import { ControllerScreen } from "../src/widgets/ControllerScreen";
import type { CustomView } from "../src/protocol";
import type { TurnData } from "../src/games/tactics/types";
import fixture from "./fixtures/tactics/turn-attacks.json";

beforeAll(async () => { await loadModule("tactics"); });
afterEach(cleanup);

it.each(["hit", "fail save"] as const)("aim target list and selected chip both explain %s odds before Use", kind => {
  const view = structuredClone(fixture) as CustomView;
  const data = view.data as unknown as TurnData;
  const ability = data.choices.abilities.find(a => a.ability === "common.attack")!;
  data.choices.abilities = [ability];
  for (const t of ability.targets) for (const p of t.affected) Object.assign(p, {
    roll: { kind, chance: 75, modifier: 5, vs: 14, edge: "advantage", reason: "flanking" }, damageMin: 0, damageMax: 16,
  });
  render(<ControllerScreen view={view} send={vi.fn()} me="ALEX" game="tactics" />);
  fireEvent.click(screen.getByRole("button", { name: /^Attack/ }));
  const target = document.querySelector<HTMLButtonElement>(".tx-targets button")!;
  expect(target.textContent).toContain(`75% ${kind}`);
  expect(target.textContent).toContain("Advantage: flanking");
  expect(target.textContent).toContain("0–16 damage");
  fireEvent.click(target);
  expect(document.querySelector(".tx-chips")?.textContent).toContain(`75% ${kind}`);
  expect(document.querySelector(".tx-chips")?.textContent).toContain(kind === "hit" ? "AC 14" : "DC 14");
});
