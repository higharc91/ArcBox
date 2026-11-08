import { afterEach, beforeAll, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import { loadModule } from "../src/games/registry";
import { Status } from "../src/games/tactics/screens";
import type { StatusData } from "../src/games/tactics/types";
import fixture from "./fixtures/tactics/status.json";

beforeAll(async () => { await loadModule("tactics"); });
afterEach(cleanup);
it("shows battle context and abilities before any turn has been cached", () => {
  const data = structuredClone(fixture.data) as unknown as StatusData;
  data.cls = "Cleric";
  data.round = 3;
  data.objective = "Keep the mayor alive";
  data.party = [{ ...data.me, hp: 2 }, { ...data.me, id: "ally", name: "Wren", hp: 0, life: "Downed", countdown: 2 }];
  data.recent = ["Goblin used Attack.", "Wren took 4 damage.", "Wren is downed."];
  data.abilities = [{ ability: "cleric.healing_word", name: "Healing Word", tooltip: "Restore an ally's HP", item: null, cost: {}, targets: [], tiles: [] }];
  const send = vi.fn();
  render(<Status data={data} send={send} stream={send} viewId="one" />);
  expect(screen.getByText("Cleric")).toBeTruthy();
  expect(screen.getByText("Round 3")).toBeTruthy();
  expect(screen.getByText("Keep the mayor alive")).toBeTruthy();
  expect(screen.getByRole("region", { name: "Party status" }).textContent).toContain("Downed");
  expect(screen.getByRole("region", { name: "Recent events" }).textContent).toContain("Wren took 4 damage");
  fireEvent.click(screen.getByRole("tab", { name: "Abilities" }));
  expect(screen.getByText("Healing Word")).toBeTruthy();
  expect(send).not.toHaveBeenCalled();
});
