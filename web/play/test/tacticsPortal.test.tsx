import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/preact";
import type { CampaignResponse } from "../src/protocol";
import { TacticsPortal, placed, pointsSpent, type TacticsPortalView } from "../src/games/tactics/portal";
import { contrast, readable, worstContrast } from "../src/games/tactics/colors";
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

const FRESH = fresh as unknown as TacticsPortalView;
const CLERIC = cleric as unknown as TacticsPortalView;
const dialog = () => screen.getByRole("dialog");
const lastCall = (send: ReturnType<typeof show>) => send.mock.calls.at(-1) as unknown as [string, Record<string, unknown>];

afterEach(cleanup);

describe("tactics portal", () => {
  it("point-buy costs match the server's table", () => {
    expect(pointsSpent({ str: 15, dex: 15, con: 15, int: 8, wis: 8, cha: 8 })).toBe(27);
    expect(pointsSpent({ str: 8, dex: 8, con: 8, int: 8, wis: 8, cha: 8 })).toBe(0);
  });

  it("a member without a character makes one within the 27-point buy, in the colour they start on", () => {
    const send = show(fresh);
    expect(screen.getByText("Make your character")).toBeTruthy();
    expect(screen.getByText("Ability scores: 27 of 27 points left")).toBeTruthy();
    for (let i = 0; i < 7; i++) fireEvent.click(screen.getByRole("button", { name: "Raise str" }));
    expect(screen.getByText("Ability scores: 18 of 27 points left")).toBeTruthy();   // 15 costs 9
    expect((screen.getByRole("button", { name: "Raise str" }) as HTMLButtonElement).disabled).toBe(true);

    fireEvent.click(screen.getByRole("button", { name: "Create" }));
    const [type, data] = lastCall(send);
    expect(type).toBe("createCharacter");
    expect(data.scores).toEqual({ str: 15, dex: 8, con: 8, int: 8, wis: 8, cha: 8 });
    expect(data.race).toBe(FRESH.options!.races[0]!.id);
    expect(data.color).toBe(FRESH.options!.colors!.current);
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

  it("warns that a remake after fighting is the one free one", () => {
    const view = structuredClone(CLERIC);
    view.me!.freeRemake = true;
    view.options = FRESH.options;
    show(view);
    fireEvent.click(screen.getByRole("button", { name: "Remake character" }));
    expect(screen.getByText(/This is your one free remake/)).toBeTruthy();
  });

  it("is read-only while a session has the campaign", () => {
    show(cleric, true);
    expect((screen.getByRole("button", { name: "Life Domain" }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getAllByRole("button", { name: /^Change level/ })[0] as HTMLButtonElement).disabled).toBe(true);
  });

  it("shows every party member's colour", () => {
    show(cleric);
    const dots = document.querySelectorAll(".tx-party .tx-dot");
    expect(dots).toHaveLength(CLERIC.party.filter((p) => p.color).length);
    expect((dots[0] as HTMLElement).style.getPropertyValue("--c")).toBe(CLERIC.party[0]!.color);
  });
});

describe("details during creation", () => {
  it("a race's sheet says what it does in play, and can pick it", () => {
    show(fresh);
    fireEvent.click(screen.getByRole("button", { name: "About Dwarf" }));
    const d = dialog();
    expect(within(d).getByRole("heading", { name: "Dwarf" })).toBeTruthy();
    for (const fact of FRESH.details!.races.dwarf!.facts) expect(within(d).getByText(fact)).toBeTruthy();
    expect(within(d).getByText(FRESH.details!.features["dwarf.stoneskin"]!.text)).toBeTruthy();
    fireEvent.click(within(d).getByRole("button", { name: "Pick Dwarf" }));
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(screen.getByRole("button", { name: "Dwarf", pressed: true })).toBeTruthy();
  });

  it("a race's sheet opens before it's picked, and the pick stays put", () => {
    show(fresh);
    const first = FRESH.options!.races[0]!;
    fireEvent.click(screen.getByRole("button", { name: "About Tiefling" }));
    fireEvent.keyDown(dialog(), { key: "Escape" });
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(screen.getByRole("button", { name: first.name, pressed: true })).toBeTruthy();
  });

  it("closing a sheet gives focus back to what opened it", () => {
    show(fresh);
    const opener = screen.getByRole("button", { name: "About Elf" });
    opener.focus();
    fireEvent.click(opener);
    expect(document.activeElement).toBe(within(dialog()).getByRole("button", { name: "Close" }));
    fireEvent.click(within(dialog()).getByRole("button", { name: "Close" }));
    expect(document.activeElement).toBe(opener);
  });

  it("a class's sheet has its role, proficiencies and levels, and a feature opens with Back", () => {
    show(fresh);
    fireEvent.click(screen.getByRole("button", { name: "About Wizard" }));
    const wizard = FRESH.details!.classes.wizard!;
    expect(within(dialog()).getByText(wizard.role)).toBeTruthy();
    for (const fact of wizard.facts) expect(within(dialog()).getByText(fact)).toBeTruthy();
    expect(within(dialog()).getAllByText(/^Level \d+$/).length).toBe(wizard.levels.length);

    const spell = wizard.levels[0]!.choices[0]!.from[0]!;
    const name = FRESH.details!.features[spell]!.name;
    fireEvent.click(within(dialog()).getAllByRole("button", { name })[0]!);
    expect(within(dialog()).getByRole("heading", { name })).toBeTruthy();
    expect(within(dialog()).getByText(FRESH.details!.features[spell]!.text)).toBeTruthy();
    fireEvent.click(within(dialog()).getByRole("button", { name: "Back" }));
    expect(within(dialog()).getByRole("heading", { name: "Wizard" })).toBeTruthy();
  });

  it("an ability score's sheet gives its modifier now and what it drives", () => {
    show(fresh);
    for (let i = 0; i < 7; i++) fireEvent.click(screen.getByRole("button", { name: "Raise str" }));
    fireEvent.click(screen.getByRole("button", { name: "About Strength" }));
    const boost = FRESH.options!.backgrounds[0]!.boosts.str ?? 0;
    expect(within(dialog()).getByLabelText(new RegExp(`Your Strength: ${15 + boost}`))).toBeTruthy();
    for (const line of FRESH.details!.stats.str.drives) expect(within(dialog()).getByText(line)).toBeTruthy();
    fireEvent.click(within(dialog()).getByRole("button", { name: "Athletics" }));
    expect(within(dialog()).getByText(FRESH.details!.skills.athletics!.text)).toBeTruthy();
  });

  it("a background's sheet lists its boosts and skills", () => {
    show(fresh);
    fireEvent.click(screen.getByRole("button", { name: "About Sage" }));
    for (const fact of FRESH.details!.backgrounds.sage!.facts) expect(within(dialog()).getByText(fact)).toBeTruthy();
  });

  it("the chosen option's summary opens its sheet", () => {
    show(fresh);
    fireEvent.click(screen.getByRole("button", { name: "Gnome" }));
    fireEvent.click(screen.getAllByRole("button", { name: "Details" })[0]!);
    expect(within(dialog()).getByRole("heading", { name: "Gnome" })).toBeTruthy();
  });
});

describe("colours", () => {
  const colors = FRESH.options!.colors!;

  it("a palette swatch is the colour sent", () => {
    const send = show(fresh);
    fireEvent.click(screen.getByRole("radio", { name: "Sky" }));
    expect(screen.getByRole("radio", { name: "Sky" }).getAttribute("aria-checked")).toBe("true");
    fireEvent.click(screen.getByRole("button", { name: "Create" }));
    expect(lastCall(send)[1].color).toBe("#7ccbff");
  });

  it("a colour of your own too dark to read is lightened, and says so", () => {
    const send = show(fresh);
    const input = document.querySelector<HTMLInputElement>(".tx-swatch input[type=color]")!;
    fireEvent.input(input, { target: { value: "#1b2a6b" } });
    expect(screen.getByText(/lightened from #1b2a6b/)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Create" }));
    const sent = lastCall(send)[1].color as string;
    expect(sent).not.toBe("#1b2a6b");
    expect(worstContrast(sent, colors.grounds)).toBeGreaterThanOrEqual(colors.minContrast);
  });

  it("every palette colour reads on every ground", () => {
    for (const p of colors.palette) expect(readable(p.hex, colors.grounds, colors.minContrast)).toBe(p.hex);
    expect(contrast("#000000", "#ffffff")).toBeCloseTo(21, 3);
  });

  /** The same answers as the server's PlayerColors.Readable (CharacterScreenTests pins these too). */
  it("lightens exactly as the server does", () => {
    expect(readable("#1b2a6b", colors.grounds, colors.minContrast)).toBe(SERVER["#1b2a6b"]);
    expect(readable("#2e7d32", colors.grounds, colors.minContrast)).toBe(SERVER["#2e7d32"]);
    expect(readable("#C0392B", colors.grounds, colors.minContrast)).toBe(SERVER["#c0392b"]);
    expect(readable("#000000", colors.grounds, colors.minContrast)).toBe(SERVER["#000000"]);
    expect(readable("red", colors.grounds, colors.minContrast)).toBeNull();
  });

  it("the sheet changes the colour behind a Save", () => {
    const send = show(cleric);
    fireEvent.click(screen.getByRole("button", { name: "Change" }));
    const d = dialog();
    expect((within(d).getByRole("button", { name: "Save colour" }) as HTMLButtonElement).disabled).toBe(true);
    fireEvent.click(within(d).getByRole("radio", { name: "Mint" }));
    fireEvent.click(within(d).getByRole("button", { name: "Save colour" }));
    expect(send).toHaveBeenLastCalledWith("setColor", { color: "#8ff0c4" });
  });
});

/** The server's PlayerColors.Readable for these colours on the base grounds. */
const SERVER: Record<string, string> = {
  "#1b2a6b": "#aeb9ea", "#2e7d32": "#79ce7d", "#c0392b": "#ebaea7", "#000000": "#bababa",
};

describe("the sheet", () => {
  const me = CLERIC.me!;

  it("collapses picks to names per choice, with no lists until Change", () => {
    show(cleric);
    expect(screen.queryAllByRole("checkbox")).toHaveLength(0);
    expect(screen.getAllByRole("button", { name: /^Change level \d+ picks$/ })).toHaveLength(me.choices.length);
    const groups = document.querySelectorAll(".tx-pick-group");
    expect(groups).toHaveLength(me.choices.length);
    const first = me.choices[0]!.picked![0]!;
    expect(within(groups[0] as HTMLElement).getByRole("button", { name: CLERIC.details!.features[first]!.name })).toBeTruthy();
  });

  it("Change opens one choice's list, and saving swaps only its picks", () => {
    const send = show(cleric);
    const c = me.choices[0]!;
    fireEvent.click(screen.getAllByRole("button", { name: `Change level ${c.level} picks` })[0]!);
    const d = dialog();
    expect(within(d).getAllByRole("checkbox")).toHaveLength(c.from.length);
    const save = within(d).getByRole("button", { name: "Save picks" }) as HTMLButtonElement;
    expect(save.disabled).toBe(true);
    const drop = c.picked![0]!;
    const name = c.from.find((o) => o.id === drop)!.name;
    fireEvent.click(within(d).getByRole("checkbox", { name: new RegExp(name) }));
    fireEvent.click(save);
    const [type, data] = lastCall(send);
    expect(type).toBe("setPicks");
    expect((data.picks as string[]).slice().sort()).toEqual(me.picks.filter((p) => p !== drop).sort());
  });

  it("a choice of one swaps on a tap", () => {
    const send = show(cleric);
    const i = me.choices.findIndex((c) => c.choose === 1 && c.from.length > 1);
    const c = me.choices[i]!;
    fireEvent.click(screen.getAllByRole("button", { name: `Change level ${c.level} picks` })[me.choices.filter((x, j) => x.level === c.level && j < i).length]!);
    const other = c.from.find((o) => !me.picks.includes(o.id))!;
    fireEvent.click(within(dialog()).getByRole("checkbox", { name: new RegExp(other.name) }));
    fireEvent.click(within(dialog()).getByRole("button", { name: "Save picks" }));
    const picks = lastCall(send)[1].picks as string[];
    expect(picks).toContain(other.id);
    expect(picks).not.toContain(c.picked![0]);
    expect(picks).toHaveLength(me.picks.length);
  });

  it("a spell's full text opens on demand in the list, and from the picks", () => {
    show(cleric);
    const spell = me.choices[1]!.picked![0]!;
    const f = CLERIC.details!.features[spell]!;
    fireEvent.click(screen.getByRole("button", { name: f.name }));
    expect(within(dialog()).getByText(f.text)).toBeTruthy();
    fireEvent.click(within(dialog()).getByRole("button", { name: "Close" }));

    fireEvent.click(screen.getAllByRole("button", { name: /^Change level 1 picks/ })[1]!);
    expect(within(dialog()).queryByText(f.text)).toBeNull();
    fireEvent.click(within(dialog()).getByRole("button", { name: `Show ${f.name}` }));
    expect(within(dialog()).getByText(f.text)).toBeTruthy();
  });

  it("race, class, background, scores and skills open their details", () => {
    show(cleric);
    fireEvent.click(screen.getByRole("button", { name: "Dwarf" }));
    expect(within(dialog()).getByText(CLERIC.details!.races.dwarf!.facts[0]!)).toBeTruthy();
    fireEvent.click(within(dialog()).getByRole("button", { name: "Close" }));
    fireEvent.click(screen.getByRole("button", { name: /^Wisdom 17/ }));
    expect(within(dialog()).getByLabelText("Your Wisdom: 17, modifier +3")).toBeTruthy();
    fireEvent.click(within(dialog()).getByRole("button", { name: "Close" }));
    fireEvent.click(screen.getByRole("button", { name: /^Insight/ }));
    expect(within(dialog()).getByText("Yes")).toBeTruthy();
    expect(within(dialog()).getByText(`+${me.bonuses!.insight}`)).toBeTruthy();
  });

  it("a subclass's details offer to choose it", () => {
    const send = show(cleric);
    fireEvent.click(screen.getByRole("button", { name: "About Life Domain" }));
    expect(within(dialog()).getAllByText(/^Level \d+$/).length).toBeGreaterThan(1);
    fireEvent.click(within(dialog()).getByRole("button", { name: "Choose Life Domain" }));
    expect(send).toHaveBeenLastCalledWith("chooseSubclass", { subclass: "cleric.life" });
  });

  it("without the server's matching, picks fill choices in order", () => {
    const groups = placed({
      picks: ["a", "b", "c"],
      choices: [
        { level: 1, choose: 2, from: [{ id: "a", name: "A", tooltip: "" }, { id: "b", name: "B", tooltip: "" }, { id: "c", name: "C", tooltip: "" }] },
        { level: 2, choose: 1, from: [{ id: "a", name: "A", tooltip: "" }, { id: "c", name: "C", tooltip: "" }] },
      ],
    });
    expect(groups).toEqual([["a", "b"], ["c"]]);
  });
});
