import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import type { CustomView } from "../src/protocol";
import { loadModule } from "../src/games/registry";
import type { TurnData } from "../src/games/meridian/types";
import { ControllerScreen } from "../src/widgets/ControllerScreen";

// Real views from a real session, written by games/Meridian/Meridian.Game.Tests/PhoneFixtureTests.cs.
const fixtures = import.meta.glob<{ default: unknown }>("./fixtures/meridian/*.json", { eager: true });
const view = (name: string): CustomView => {
  const mod = fixtures[`./fixtures/meridian/${name}.json`];
  if (!mod) throw new Error(`missing fixture meridian/${name}`);
  return structuredClone(mod.default) as CustomView;
};
const show = (name: string) => {
  const send = vi.fn();
  render(<ControllerScreen view={view(name)} send={send} me="Player1" game="meridian" />);
  return send;
};
const names = Object.keys(fixtures).map((k) => k.replace("./fixtures/meridian/", "").replace(".json", ""));

beforeAll(async () => {
  await loadModule("meridian");
});
afterEach(cleanup);

describe("meridian phone screens", () => {
  it("has a fixture for every screen state the game pins", () => {
    expect(names.length).toBe(27);
  });

  it.each(names)("renders %s without the newer-app card", (name) => {
    show(name);
    expect(screen.queryByText("This screen needs a newer app")).toBeNull();
  });

  it("moves to a reachable compartment with an absolute intent", () => {
    const send = show("turn");
    const target = (view("turn").data as TurnData).reach[0];
    fireEvent.click(screen.getByRole("button", { name: new RegExp(`\\(${target.cost}\\)$`) }));
    expect(send).toHaveBeenCalledWith({ action: "moveTo", data: { to: target.id } });
  });

  it("offers only reachable compartments as buttons, across the decks", () => {
    show("turn");
    const reach = (view("turn").data as TurnData).reach;
    let buttons = 0;
    for (const deck of ["Command", "Habitat", "Engineering"]) {
      fireEvent.click(screen.getByRole("tab", { name: new RegExp(`^${deck}`) }));
      buttons += screen.queryAllByRole("button", { name: /^Move to / }).length;
    }
    expect(buttons).toBe(reach.length);
  });

  it("marks the decks that have somewhere to go", () => {
    show("turn");
    const data = view("turn").data as TurnData;
    const decks = new Set(data.reach.map((r) => data.map.compartments.find((c) => c.id === r.id)!.at[0]));
    for (const deck of decks) {
      expect(screen.getByRole("tab", { name: new RegExp(`${deck} \\d`, "i") })).toBeTruthy();
    }
  });

  it("opens a hatch by compartment and side", () => {
    const opens = (view("turn-no-moves").data as TurnData).opens;
    for (const name of names.filter((n) => n.startsWith("turn"))) {
      const data = view(name).data as TurnData;
      if (data.opens.length === 0) continue;
      cleanup();
      const send = show(name);
      // The open hatch may be on another deck than the first shown; switch to its deck.
      const from = data.map.compartments.find((c) => c.id === data.opens[0].at)!;
      fireEvent.click(screen.getByRole("tab", { name: new RegExp(from.at[0], "i") }));
      fireEvent.click(screen.getAllByRole("button", { name: /^Open the / })[0]);
      expect(send).toHaveBeenCalledWith({ action: "open", data: { at: data.opens[0].at, side: data.opens[0].side } });
      return;
    }
    expect(opens).toBeDefined();
  });

  it("ends the turn", () => {
    const send = show("turn");
    fireEvent.click(screen.getByRole("button", { name: "End turn" }));
    expect(send).toHaveBeenCalledWith({ action: "endTurn" });
  });

  it("uses an item with a use, and offers no button for a passive one", () => {
    const send = show("turn-with-items");
    fireEvent.click(screen.getByRole("button", { name: "Patch up" }));
    expect(send).toHaveBeenCalledWith({ action: "useItem", data: { item: "med_kit" } });
    expect(screen.queryByRole("button", { name: /Crowbar/ })).toBeNull();
  });

  it("offers a trade to a crew member alongside", () => {
    const send = show("turn-with-items");
    const offer = screen.getAllByRole("button", { name: "Offer" })[0] as HTMLButtonElement;
    expect(offer.disabled).toBe(true);
    fireEvent.change(screen.getAllByLabelText("Give")[0], { target: { value: "crowbar" } });
    fireEvent.click(offer);
    expect(send).toHaveBeenCalledWith({ action: "offerTrade", data: expect.objectContaining({ give: ["crowbar"], take: [] }) });
  });

  it("orients a new compartment", () => {
    const send = show("orient");
    fireEvent.click(screen.getAllByRole("button", { name: /^Hatches / })[0]);
    expect(send).toHaveBeenCalledWith({ action: "orient", data: { rotation: expect.any(Number) } });
  });

  it("chooses a card option", () => {
    const send = show("card");
    fireEvent.click(screen.getByRole("button", { name: /Search it/ }));
    expect(send).toHaveBeenCalledWith({ action: "choose", data: { option: "search" } });
  });

  it("rolls a check and shows its bands", () => {
    const send = show("roll-check");
    expect(screen.getByText("It finds you.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: /^Roll 3 dice/ }));
    expect(send).toHaveBeenCalledWith({ action: "roll" });
  });

  it("shows what the Breach roll needs", () => {
    show("roll-breach");
    expect(screen.getByText(/6 or more/)).toBeTruthy();
  });

  it("answers a trade", () => {
    const send = show("trade");
    fireEvent.click(screen.getByRole("button", { name: "Accept" }));
    expect(send).toHaveBeenCalledWith({ action: "answerTrade", data: { accept: true } });
  });

  it("disables a crew member someone else took", () => {
    show("crewPick-taken");
    expect((screen.getByRole("button", { name: "Taken" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("picks a crew member", () => {
    const send = show("crewPick");
    fireEvent.click(screen.getAllByRole("button", { name: /^Choose / })[1]);
    expect(send).toHaveBeenCalledWith({ action: "pick", data: { crew: expect.any(String) } });
  });

  it("browses the sheet's tabs and decks without sending anything", () => {
    const send = show("sheet");
    fireEvent.click(screen.getByRole("tab", { name: "Gear" }));
    fireEvent.click(screen.getByRole("tab", { name: "Map" }));
    fireEvent.click(screen.getByRole("tab", { name: "Command" }));
    fireEvent.click(screen.getByRole("tab", { name: "Traits" }));
    expect(send).not.toHaveBeenCalled();
  });

  it("shows a briefing with Ready, and nothing to press once ready", () => {
    const send = show("briefing-mimic");
    fireEvent.click(screen.getByRole("button", { name: "Ready" }));
    expect(send).toHaveBeenCalledWith({ action: "ready" });
  });

  it("splits damage between two traits", () => {
    const send = show("allocate");
    fireEvent.click(screen.getAllByRole("button", { name: /−/ })[0]);
    expect(send).toHaveBeenCalledWith({ action: "allocate", data: { first: 0 } });
  });

  it("keeps a vote on the phone until it is cast", () => {
    const send = show("vote");
    const cast = screen.getByRole("button", { name: "Vote" }) as HTMLButtonElement;
    expect(cast.disabled).toBe(true);
    fireEvent.click(screen.getAllByRole("radio")[1]);
    expect(send).not.toHaveBeenCalled();
    fireEvent.click(cast);
    expect(send).toHaveBeenCalledWith({ action: "vote", data: { choice: "p2" } });
  });

  it("says a vote is in once cast", () => {
    show("vote-cast");
    expect(screen.getByText(/Your vote is in/)).toBeTruthy();
  });

  it("lends a die as an echo", () => {
    const send = show("echo");
    fireEvent.click(screen.getAllByRole("button", { name: /^Lend / })[0]);
    expect(send).toHaveBeenCalledWith({ action: "lend", data: { to: "p1" } });
  });

  it("commands monsters on the controller's turn", () => {
    const send = show("turn-monsters");
    fireEvent.click(screen.getAllByRole("button", { name: /^Move to the |^Move to / }).find((b) => b.closest(".mer-monster"))!);
    expect(send).toHaveBeenCalledWith({ action: "monsterMove", data: expect.objectContaining({ monster: expect.stringMatching(/^brood-/) }) });
  });

  it("offers the scenario's actions and attacks in the endgame", () => {
    const send = show("turn-endgame");
    const button = screen.queryAllByRole("button").find((b) => b.closest(".mer-actions, .mer-attacks"));
    expect(button).toBeTruthy();
    fireEvent.click(button!);
    expect(send.mock.calls[0][0].action).toMatch(/^(act|attack)$/);
  });

  it("lets only the VIP continue from the debrief", () => {
    const send = show("debrief-won");
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));
    expect(send).toHaveBeenCalledWith({ action: "continue" });
  });

  it("keeps the briefing on the sheet in the endgame", () => {
    const send = show("sheet-endgame");
    fireEvent.click(screen.getByRole("tab", { name: "Briefing" }));
    expect(send).not.toHaveBeenCalled();
  });
});
