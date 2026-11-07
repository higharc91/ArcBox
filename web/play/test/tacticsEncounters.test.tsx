import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/preact";
import type { CustomView } from "../src/protocol";
import { loadModule } from "../src/games/registry";
import type { EncounterOption } from "../src/games/tactics/types";
import { ControllerScreen } from "../src/widgets/ControllerScreen";
import fixture from "./fixtures/tactics/pickEncounter.json";

beforeAll(async () => { await loadModule("tactics"); });
afterEach(cleanup);

function setup(encounters: EncounterOption[]) {
  const view = { ...structuredClone(fixture), data: { encounters } } as CustomView;
  const send = vi.fn();
  render(<ControllerScreen view={view} send={send} me="ALEX" game="tactics" />);
  return send;
}
const option: EncounterOption = { id: "crossing", name: "River Crossing", level: 1, min: 4, max: 6, fits: true };

describe("encounter guidance", () => {
  it("explains disabled battles and never sends a pick for them", () => {
    const send = setup([{ ...option, fits: false, max: 2, unavailableReason: "Party too big (max 2)", guidance: "Recommended" }]);
    const button = screen.getByRole("button", { name: /River Crossing/ });
    expect((button as HTMLButtonElement).disabled).toBe(true);
    expect(within(button).getByText("Party too big (max 2)")).toBeTruthy();
    fireEvent.click(button);
    expect(send).not.toHaveBeenCalled();
  });

  it("labels levels relative to the party and preserves AI companion guidance", () => {
    const send = setup([
      { ...option, guidance: "Recommended", companions: 2 },
      { ...option, id: "hard", name: "Forest Ruins", level: 3, guidance: "Higher level" },
      { ...option, id: "easy", name: "Training", level: 1, guidance: "Lower level" },
    ]);
    expect(screen.getByText("Recommended")).toBeTruthy();
    expect(screen.getByText("Higher level")).toBeTruthy();
    expect(screen.getByText("Lower level")).toBeTruthy();
    expect(screen.getByText(/\+2 AI companions/)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: /River Crossing/ }));
    expect(send).toHaveBeenCalledExactlyOnceWith({ action: "pickEncounter", data: { encounter: "crossing" } });
  });

  it("calls an unknown level Unrated, including with an older server", () => {
    setup([{ ...option, level: null }]);
    expect(screen.getByText("Unrated")).toBeTruthy();
    expect(screen.getByText("Level not specified")).toBeTruthy();
    expect(screen.queryByText(/Lv [–?]/)).toBeNull();
  });

  it("still explains a disabled row from an older server", () => {
    setup([{ ...option, fits: false, max: 2 }]);
    expect(screen.getByText("Party too big (max 2)")).toBeTruthy();
  });
});
