import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/preact";
import { PlayerProfileContext } from "../src/games/module";
import { BattleGuide, ClassGuidance, primerKey } from "../src/games/tactics/onboarding";
import { EconomyChips, HpText } from "../src/games/tactics/ui";
import { Turn } from "../src/games/tactics/turn";
import { PickClass } from "../src/games/tactics/screens";
import { ABILITY_GUIDANCE } from "../src/games/tactics/guidance";
import type { TurnData } from "../src/games/tactics/types";
import fixture from "./fixtures/tactics/turn.json";
import pickFixture from "./fixtures/tactics/pickClass.json";

afterEach(() => { cleanup(); localStorage.clear(); vi.restoreAllMocks(); });
const guide = (profile: string, unit = "hero", firstTurn = true) => render(
  <PlayerProfileContext.Provider value={profile}><BattleGuide unit={unit} firstTurn={firstTurn} /></PlayerProfileContext.Provider>);

describe("first battle guidance", () => {
  it("shows three tips once per profile across new characters and remounts, and always allows reopening", () => {
    guide("first-profile");
    expect(screen.getByRole("region", { name: "First battle quick start" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Next tip" }));
    expect(screen.getByText("Move, then choose your moment")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Next tip" }));
    expect(screen.getByText("Keep your friends standing")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Ready to play" }));
    expect(screen.queryByRole("region")).toBeNull();
    cleanup(); guide("first-profile", "new-character");
    expect(screen.queryByRole("region")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "How to play" }));
    expect(screen.getByRole("dialog", { name: "How to play" })).toBeTruthy();
    cleanup(); guide("different-profile");
    expect(screen.getByRole("region")).toBeTruthy();
  });

  it("doesn't consume onboarding while waiting or playing a summon", () => {
    guide("summon-profile", "hound", false);
    expect(localStorage.getItem(primerKey("summon-profile"))).toBeNull();
    expect(screen.queryByRole("region")).toBeNull();
    cleanup(); guide("summon-profile", "owner");
    expect(screen.getByRole("region")).toBeTruthy();
  });

  it("can hide the inline tips without hiding the game controls or sending input", () => {
    const send = vi.fn();
    render(<PlayerProfileContext.Provider value="play-profile"><Turn data={structuredClone(fixture.data)} send={send} stream={send} viewId="test" /></PlayerProfileContext.Provider>);
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(screen.getByRole("button", { name: "End turn" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Hide tips" }));
    expect(screen.queryByRole("region", { name: "First battle quick start" })).toBeNull();
    expect(send).not.toHaveBeenCalled();
  });

  it("keeps play available when storage is blocked or full", () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => { throw new Error("blocked"); });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("full"); });
    guide("blocked-profile");
    expect(screen.getByRole("region")).toBeTruthy();
    cleanup(); guide("blocked-profile");
    expect(screen.queryByRole("region")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "How to play" }));
    expect(screen.getByRole("dialog")).toBeTruthy();
  });

  it("opens touch explanations even for spent resources, and Escape restores focus", () => {
    render(<EconomyChips e={{ moveLeft: 0, action: false, extraActions: 0, bonus: false, reaction: false }} />);
    for (const topic of ["Move", "Action", "Bonus", "Reaction"]) {
      const button = screen.getByRole("button", { name: new RegExp(`Explain ${topic}:`) });
      button.focus(); fireEvent.click(button);
      expect(screen.getByRole("dialog", { name: topic })).toBeTruthy();
      fireEvent.keyDown(document, { key: "Escape" });
      expect(screen.queryByRole("dialog")).toBeNull();
      expect(document.activeElement).toBe(button);
    }
  });

  it("explains the Downed countdown from the hero's HP without changing the countdown", () => {
    const unit = { ...(fixture.data as unknown as TurnData).me, life: "Downed", hp: 0, countdown: 2 };
    render(<HpText unit={unit} explain />);
    fireEvent.click(screen.getByRole("button", { name: "Explain Downed" }));
    expect(screen.getByRole("dialog", { name: "Downed" }).textContent).toContain("start of their own turns");
    expect(unit.countdown).toBe(2);
  });

  it("keeps noninteractive HP labels safe inside unit-selection buttons", () => {
    const unit = { ...(fixture.data as unknown as TurnData).me, life: "Downed", countdown: 2 };
    render(<button><HpText unit={unit} /></button>);
    expect(document.querySelector("button button")).toBeNull();
    expect(screen.queryByRole("button", { name: "Explain Downed" })).toBeNull();
  });

  it("gives every shipped class a playstyle and difficulty rating without changing the class pick", () => {
    const send = vi.fn(); render(<PickClass data={pickFixture.data} send={send} stream={send} viewId="classes" />);
    for (const cls of pickFixture.data.classes) {
      const card = screen.getByRole("button", { name: new RegExp(cls.name) });
      expect(card.textContent).toMatch(/Difficulty: (Easy|Moderate|Difficult)/);
      expect(card.querySelector(".tx-class-guide > span")?.textContent).toMatch(/\.$/);
    }
    const fighter = screen.getByRole("button", { name: /Fighter/ });
    expect(within(fighter).getByText("Good for new players")).toBeTruthy();
    fireEvent.click(fighter);
    expect(send).toHaveBeenCalledExactlyOnceWith("pickClass", { class: "fighter" });
    cleanup(); render(<ClassGuidance id="future-class" />);
    expect(document.body.textContent).not.toContain("Difficulty:");
  });

  it("makes plain-language summaries additive: exact server rules and costs remain in the aiming view", () => {
    const data = structuredClone(fixture.data) as unknown as TurnData;
    const dash = data.choices.abilities.find(a => a.ability === "common.dash")!;
    const send = vi.fn(); render(<Turn data={data} send={send} stream={send} viewId="rules" />);
    fireEvent.click(screen.getByRole("button", { name: /^Dash/ }));
    expect(screen.getByText(ABILITY_GUIDANCE["common.dash"]!)).toBeTruthy();
    expect(screen.getByText(dash.tooltip)).toBeTruthy();
    expect(screen.getByText("Full rules for Dash")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Use" }));
    expect(send).toHaveBeenCalledExactlyOnceWith("use", { ability: "common.dash", item: undefined, unit: undefined, tile: undefined });
  });
});
