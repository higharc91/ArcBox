import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/preact";
import type { CampaignResponse } from "../src/protocol";
import { TacticsPortal, pointsSpent, type TacticsPortalView } from "../src/games/tactics/portal";
import fresh from "./fixtures/tactics/portal-new.json";
import cleric from "./fixtures/tactics/portal-character.json";

// Real PortalView output, written by games/Tactics/Tactics.Game.Tests/PhoneFixtureTests.cs.
const campaign = (view: unknown): CampaignResponse => ({
  info: { id: "c1", code: "ABCD", gameId: "tactics", name: "Embers", owner: "pr_ana", members: [], version: 3 },
  view: structuredClone(view), leasedBy: null,
}) as unknown as CampaignResponse;

const show = (view: unknown, readOnly = false) => {
  const send = vi.fn(async () => {});
  render(<TacticsPortal campaign={campaign(view)} readOnly={readOnly} send={send} />);
  return send;
};

afterEach(cleanup);

describe("tactics portal", () => {
  it("point-buy costs match the server's table", () => {
    expect(pointsSpent({ str: 15, dex: 15, con: 15, int: 8, wis: 8, cha: 8 })).toBe(27);
    expect(pointsSpent({ str: 8, dex: 8, con: 8, int: 8, wis: 8, cha: 8 })).toBe(0);
  });

  it("a member without a character makes one within the 27-point buy", () => {
    const send = show(fresh);
    expect(screen.getByText("Make your character")).toBeTruthy();
    expect(screen.getByText("Ability scores: 27 of 27 points left")).toBeTruthy();
    for (let i = 0; i < 7; i++) fireEvent.click(screen.getByRole("button", { name: "Raise str" }));
    expect(screen.getByText("Ability scores: 18 of 27 points left")).toBeTruthy();   // 15 costs 9
    expect((screen.getByRole("button", { name: "Raise str" }) as HTMLButtonElement).disabled).toBe(true);

    fireEvent.click(screen.getByRole("button", { name: "Create" }));
    const [type, data] = send.mock.calls[0]! as unknown as [string, { scores: Record<string, number>; race: string }];
    expect(type).toBe("createCharacter");
    expect(data.scores).toEqual({ str: 15, dex: 8, con: 8, int: 8, wis: 8, cha: 8 });
    expect(data.race).toBe((fresh as unknown as TacticsPortalView).options!.races[0]!.id);
  });

  it("a level-4 cleric chooses a subclass and spends an increase", () => {
    const send = show(cleric);
    expect(screen.getByText("Choose your subclass")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Life Domain" }));
    expect(send).toHaveBeenLastCalledWith("chooseSubclass", { subclass: "cleric.life" });

    fireEvent.click(screen.getByRole("button", { name: "WIS" }));
    fireEvent.click(screen.getByRole("button", { name: "WIS +2" }));
    expect(send).toHaveBeenLastCalledWith("spendIncrease", { stats: ["wis"] });
  });

  it("picks save as one set once changed", () => {
    const send = show(cleric);
    const me = (cleric as unknown as TacticsPortalView).me!;
    const save = screen.getByRole("button", { name: "Save picks" }) as HTMLButtonElement;
    expect(save.disabled).toBe(true);
    const first = me.picks[0]!;
    const name = me.choices.flatMap((c) => c.from).find((o) => o.id === first)!.name;
    fireEvent.click(screen.getAllByRole("checkbox").find((c) => c.parentElement!.textContent!.includes(name))!);
    fireEvent.click(save);
    expect(send).toHaveBeenLastCalledWith("setPicks", { picks: me.picks.filter((p) => p !== first) });
  });

  it("warns that a remake after fighting is the one free one", () => {
    const view = structuredClone(cleric) as unknown as TacticsPortalView;
    view.me!.freeRemake = true;
    view.options = (fresh as unknown as TacticsPortalView).options;
    show(view);
    fireEvent.click(screen.getByRole("button", { name: "Remake character" }));
    expect(screen.getByText(/This is your one free remake/)).toBeTruthy();
  });

  it("is read-only while a session has the campaign", () => {
    show(cleric, true);
    expect((screen.getByRole("button", { name: "Life Domain" }) as HTMLButtonElement).disabled).toBe(true);
  });
});
