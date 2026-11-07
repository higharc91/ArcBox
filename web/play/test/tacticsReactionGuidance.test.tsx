import { afterEach, beforeAll, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import { loadModule } from "../src/games/registry";
import { ReactPrompt } from "../src/games/tactics/screens";
import type { ReactData } from "../src/games/tactics/types";
import fixture from "./fixtures/tactics/react.json";

beforeAll(async () => { await loadModule("tactics"); });
afterEach(cleanup);
it("shows the actual outcome, cost, incoming damage and timeout policy while retaining reaction inputs", () => {
  const data = structuredClone(fixture.data) as unknown as ReactData;
  data.incomingDamage = 5.5;
  data.timeout = "If you do nothing: the AI plans to choose Shield. If it can't, it chooses Don't react.";
  data.options = [{ id: "pass", label: "Don't react", target: null, outcome: "This hit lands. Keep your Reaction.", cost: "Costs nothing." },
    { id: "wizard.shield", label: "Shield", target: "hero4", outcome: "Turns this hit into a miss.", cost: "Costs 1 Reaction + 1 Mana (2 left)." }];
  const send = vi.fn();
  render(<ReactPrompt data={data} send={send} stream={send} viewId="one" />);
  expect(screen.getByText("Turns this hit into a miss.")).toBeTruthy();
  expect(screen.getByText("Costs 1 Reaction + 1 Mana (2 left).")).toBeTruthy();
  expect(screen.getByText(/About 5.5 incoming damage/)).toBeTruthy();
  expect(screen.getByRole("note").textContent).toContain("choose Shield");
  fireEvent.click(screen.getByRole("button", { name: /^Shield/ }));
  expect(send).toHaveBeenCalledExactlyOnceWith("react", { choice: "wizard.shield" });
  send.mockClear();
  fireEvent.click(screen.getByRole("button", { name: "Don't react", exact: true }));
  expect(send).toHaveBeenCalledExactlyOnceWith("react", { choice: "pass" });
});
