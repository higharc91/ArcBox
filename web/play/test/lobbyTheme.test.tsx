import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, render } from "@testing-library/preact";
import type { CardPlace, GameModule, LobbyTheme } from "../src/games/module";
import { DEFAULT_LOBBY_TEXT, lobbyText, registerModuleForTests, resetModulesForTests } from "../src/games/registry";
import type { LobbyState, RoomPeek, SeatCard } from "../src/protocol";
import { Lobby } from "../src/screens/Lobby";
import { Pick } from "../src/screens/Pick";
import { classOfCard, toldAgo } from "../src/games/tactics/lobby";

/**
 * The optional per-game lobby theme (GameModule.lobbyTheme): a game with one restyles the shell's lobby and "Who are
 * you?" screens; every other game, and the themed game while its module is still loading, gets the default screens
 * exactly as they were.
 */

const card = (title: string, subtitle: string | null): SeatCard => ({ title, subtitle, color: null, portrait: null });
const games = [
  { id: "quest", name: "Quest", description: "A campaign game", minPlayers: 1, maxPlayers: 6, usesCampaigns: true },
  { id: "plain", name: "Plain", description: "No theme", minPlayers: 1, maxPlayers: 6, usesCampaigns: true },
];
const lobbyFor = (gameId: string): LobbyState => ({
  code: "KQZT",
  players: [
    { id: "p1", name: "Jess", isVip: true, connected: true, profile: "pr_jess" },
    { id: "p2", name: "Dave", isVip: false, connected: true, profile: "pr_dave" },
  ],
  selectedGameId: gameId,
  games,
  inGame: false,
  canStart: true,
  campaigns: [{ id: "c1", code: "BCDF", name: "Embers of the Vale", members: 2, updatedUtc: "2026-10-25T00:00:00Z" }],
  campaign: {
    id: "c1", code: "BCDF", name: "Embers of the Vale",
    roster: [
      { profile: "pr_jess", card: card("Jess", "Human Fighter · Lv 3"), seatedAs: "p1", connected: true },
      { profile: "pr_dave", card: card("Dave", "Dwarf Cleric · Lv 3"), seatedAs: "p2", connected: true },
    ],
  },
});
const peekFor = (gameId: string): RoomPeek => ({
  code: "KQZT", gameId, campaignName: "Embers of the Vale", inGame: false, suggested: null, canJoinNew: true,
  offers: [{ ref: { member: "pr_sam" }, card: card("Sam", "Elf Wizard · Lv 3"), absent: false }],
});
const noop = () => {};
const lobbyProps = { onSelectGame: noop, onStart: noop, onSelectCampaign: noop, onCreateCampaign: noop, onClaim: noop };
const pickProps = { onTake: noop, onNew: noop, onShowAll: noop };

const Badge = ({ card: c, place }: { card: SeatCard; place: CardPlace }) => <i class={`badge ${place}`}>{c.title}</i>;
const theme: LobbyTheme = {
  className: "quest-theme",
  Backdrop: ({ screen }) => <div class="test-backdrop" data-screen={screen} aria-hidden="true" />,
  Banner: ({ screen }) => <div class="test-banner" data-screen={screen}>QUEST</div>,
  CardBadge: Badge,
  text: { pickCampaign: "The party's chronicle", campaignMeta: (c) => `${c.members} heroes` },
};
const themed: GameModule = { id: "quest", lobbyTheme: theme };
const plain: GameModule = { id: "plain" };

const lobbyHtml = (gameId: string) => {
  const { container, unmount } = render(<Lobby lobby={lobbyFor(gameId)} me="p1" {...lobbyProps} />);
  const html = container.innerHTML;
  unmount();
  return html;
};
const pickHtml = (gameId: string) => {
  const { container, unmount } = render(<Pick peek={peekFor(gameId)} {...pickProps} />);
  const html = container.innerHTML;
  unmount();
  return html;
};

beforeEach(() => resetModulesForTests());
afterEach(() => cleanup());

describe("lobby theme: the default look", () => {
  it("a game whose module has no theme renders exactly like a game with no module at all", () => {
    const before = lobbyHtml("plain");   // no module loaded: the shell's own screen
    registerModuleForTests("plain", plain);
    expect(lobbyHtml("plain")).toBe(before);
    expect(before).toContain('class="screen lobby"');
    expect(before).not.toMatch(/badge|test-|quest-theme/);
  });

  it("a themed game looks exactly like an unthemed one while its module is still loading", () => {
    const loading = lobbyHtml("quest");   // the module hasn't arrived
    registerModuleForTests("quest", { id: "quest" });   // the same game with no theme
    expect(lobbyHtml("quest")).toBe(loading);
    expect(loading).toContain(DEFAULT_LOBBY_TEXT.pickCampaign);
    expect(loading).toContain(DEFAULT_LOBBY_TEXT.newCampaign);
    expect(loading).toContain("2 members");
  });

  it("the default wording is the shell's own", () => {
    expect(lobbyText(undefined)).toBe(DEFAULT_LOBBY_TEXT);
    expect(lobbyText({ className: "x" })).toBe(DEFAULT_LOBBY_TEXT);
    expect(DEFAULT_LOBBY_TEXT.campaignMeta({ id: "c", code: "C", name: "C", members: 1, updatedUtc: "" })).toBe("1 member");
  });

  it("'Who are you?' keeps the default look for a game without a theme", () => {
    const before = pickHtml("plain");
    registerModuleForTests("plain", plain);
    expect(pickHtml("plain")).toBe(before);
    expect(before).toContain('class="screen pick"');
    expect(before).not.toMatch(/badge|test-/);
  });
});

describe("lobby theme: a themed game", () => {
  it("adds its class, backdrop and banner to the lobby, badges the player chips and rewords the campaign step", () => {
    registerModuleForTests("quest", themed);
    const { container } = render(<Lobby lobby={lobbyFor("quest")} me="p1" {...lobbyProps} />);
    const root = container.firstElementChild!;
    expect(root.className).toBe("screen lobby quest-theme");
    expect(root.querySelector(".test-backdrop")?.getAttribute("data-screen")).toBe("lobby");
    expect(root.querySelector(".test-banner")?.textContent).toBe("QUEST");
    expect([...root.querySelectorAll(".badge.chip")].map((b) => b.textContent)).toEqual(["Jess", "Dave"]);
    expect(container.textContent).toContain("The party's chronicle");
    expect(container.textContent).toContain("2 heroes");
    // Wording the theme leaves out keeps the default, and no control is lost.
    expect(container.textContent).toContain(DEFAULT_LOBBY_TEXT.newCampaign);
    expect(container.querySelector("button.start")).not.toBeNull();
  });

  it("switches back to the default look when the room picks another game", () => {
    registerModuleForTests("quest", themed);
    registerModuleForTests("plain", plain);
    const { container, rerender } = render(<Lobby lobby={lobbyFor("quest")} me="p1" {...lobbyProps} />);
    expect(container.firstElementChild!.className).toContain("quest-theme");
    rerender(<Lobby lobby={lobbyFor("plain")} me="p1" {...lobbyProps} />);
    expect(container.firstElementChild!.className).toBe("screen lobby");
    expect(container.querySelector(".test-backdrop, .test-banner, .badge")).toBeNull();
  });

  it("takes effect when the module arrives after the first render", async () => {
    const { container } = render(<Lobby lobby={lobbyFor("quest")} me="p1" {...lobbyProps} />);
    expect(container.firstElementChild!.className).toBe("screen lobby");
    await act(() => registerModuleForTests("quest", themed));
    expect(container.firstElementChild!.className).toBe("screen lobby quest-theme");
  });

  it("themes 'Who are you?' by the room's game, with a badge on each seat tile", () => {
    registerModuleForTests("quest", themed);
    const { container } = render(<Pick peek={peekFor("quest")} {...pickProps} />);
    expect(container.firstElementChild!.className).toBe("screen pick quest-theme");
    expect(container.querySelector(".test-backdrop")?.getAttribute("data-screen")).toBe("pick");
    expect(container.querySelector(".pick-tile .badge.tile")?.textContent).toBe("Sam");
  });

  it("badges the in-lobby claim rows too", () => {
    registerModuleForTests("quest", themed);
    const l = lobbyFor("quest");
    l.campaign!.roster.push({ profile: "pr_sam", card: card("Sam", "Elf Wizard · Lv 3"), seatedAs: null, connected: false });
    l.players.push({ id: "p3", name: "Alex", isVip: false, connected: true });
    const { container } = render(<Lobby lobby={l} me="p3" {...lobbyProps} />);
    expect(container.querySelector(".claim-row .badge.row")?.textContent).toBe("Sam");
  });
});

describe("Wardenflame's lobby theme", () => {
  it("is the tactics module's", async () => {
    vi.stubGlobal("matchMedia", () => ({ matches: true }));
    const { module } = await import("../src/games/tactics");
    expect(module.lobbyTheme?.className).toBe("wf-lobby");
    expect(module.lobbyTheme?.text?.pickCampaign).toBe("The party's chronicle");
    vi.unstubAllGlobals();
  });

  it("reads the class from a tactics seat card", () => {
    expect(classOfCard({ subtitle: "Human Fighter · Lv 3" })).toBe("fighter");
    expect(classOfCard({ subtitle: "Half-Elf Ranger · Lv 1" })).toBe("ranger");
    expect(classOfCard({ subtitle: "No character yet" })).toBeNull();
    expect(classOfCard({ subtitle: null })).toBeNull();
  });

  it("says when the chronicle was last told", () => {
    const now = Date.parse("2026-10-31T12:00:00Z");
    expect(toldAgo("2026-10-31T08:00:00Z", now)).toBe("today");
    expect(toldAgo("2026-10-30T08:00:00Z", now)).toBe("yesterday");
    expect(toldAgo("2026-10-25T08:00:00Z", now)).toBe("6 days ago");
    expect(toldAgo("2026-09-26T08:00:00Z", now)).toBe("5 weeks ago");
    expect(toldAgo("nonsense", now)).toBe("");
  });
});
