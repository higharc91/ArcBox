import { useEffect, useRef } from "preact/hooks";
import type { CampaignSummary, SeatCard } from "../../protocol";
import type { CardPlace, LobbyTheme } from "../module";
import { Crest, Logo, Portrait } from "./art";
import "./lobby.css";

/**
 * Wardenflame's lobby theme (GameModule.lobbyTheme): while a room has Wardenflame selected, the shell's lobby and
 * "Who are you?" screens are navy glass and gold over a darkened flyover of the Vale, with the logo on top, crests on
 * the player chips and portraits on the seat cards, and the campaign step told as the party's chronicle.
 */

const CLASSES = new Set([
  "fighter", "rogue", "wizard", "cleric", "ranger", "paladin", "bard", "druid", "warlock", "monk", "barbarian", "sorcerer",
]);

/**
 * The class a seat card names. Tactics cards read "Human Fighter · Lv 3" (TacticsCampaignRules.CardFor), or
 * "No character yet": the class is the last word before the " · ". Null when there's none.
 */
export function classOfCard(card: Pick<SeatCard, "subtitle">): string | null {
  const who = card.subtitle?.split(" · ")[0]?.trim().split(/\s+/).at(-1)?.toLowerCase();
  return who && CLASSES.has(who) ? who : null;
}

/** A crest on a player chip, a class portrait on a seat card; the initial when the hero has no class yet. */
function CardBadge({ card, place }: { card: SeatCard; place: CardPlace }) {
  const cls = classOfCard(card);
  if (place === "chip") {
    return <span class="wf-badge chip">{cls ? <Crest cls={cls} /> : <b aria-hidden="true">{card.title.slice(0, 1)}</b>}</span>;
  }
  const size = place === "hero" ? 80 : place === "tile" ? 60 : 40;
  return (
    <span class={`wf-badge ${place}`}>
      <Portrait unit={{ name: card.title, sprite: null, hp: 1, maxHp: 1, side: "Party" }} cls={cls} size={size} tone="ally" />
    </span>
  );
}

function Banner({ screen }: { screen: "lobby" | "pick" }) {
  return (
    <div class={`wf-banner ${screen}`}>
      <Logo />
    </div>
  );
}

const DAY = 86_400_000;

/** "today", "yesterday", "3 days ago", "2 weeks ago", or the date. */
export function toldAgo(utc: string, now = Date.now()): string {
  const t = Date.parse(utc);
  if (Number.isNaN(t)) return "";
  const days = Math.floor((now - t) / DAY);
  if (days <= 0) return "today";
  if (days === 1) return "yesterday";
  if (days < 14) return `${days} days ago`;
  if (days < 60) return `${Math.floor(days / 7)} weeks ago`;
  return new Date(t).toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" });
}

const chronicleMeta = (c: CampaignSummary) => {
  const heroes = `${c.members} ${c.members === 1 ? "hero" : "heroes"}`;
  const ago = toldAgo(c.updatedUtc);
  return ago ? `${heroes} · last told ${ago}` : heroes;
};

// ---- The backdrop: one flyover for every screen that wants it -----------------------------------------------------

interface Shared {
  host: HTMLDivElement;
  users: number;
  release: number;
  dispose(): void;
}

let shared: Shared | null = null;

const reducedMotion = () => typeof matchMedia === "function" && matchMedia("(prefers-reduced-motion: reduce)").matches;

/**
 * The flyover behind the lobby, darkened and cheap: a coarse frame on phones (about 200 lines), paused while the page
 * is hidden, and one still frame under prefers-reduced-motion. The lobby and "Who are you?" swap screens often, so
 * one flyover is kept across them and only disposed a moment after the last one leaves.
 */
function acquire(): Shared {
  if (shared) {
    clearTimeout(shared.release);
    shared.users++;
    return shared;
  }
  const host = document.createElement("div");
  host.className = "wf-backdrop-host";
  const canvas = document.createElement("canvas");
  host.appendChild(canvas);
  let fly: { dispose(): void; play(): void; pause(): void } | null = null;
  let gone = false;
  const still = reducedMotion();
  const onVisibility = () => {
    if (!fly || still) return;
    if (document.visibilityState === "hidden") fly.pause();
    else fly.play();
  };
  document.addEventListener("visibilitychange", onVisibility);
  void import("./world/flyover").then(async ({ Flyover }) => {
    if (gone) return;
    const phone = Math.min(window.innerWidth, window.innerHeight) < 600;
    const made = await Flyover.create({
      canvas, lines: phone ? 200 : 270, dim: 0.42, framing: "backdrop", introFade: false, frozenAt: still ? 6 : undefined, tour: !still,
    });
    if (gone) { made?.dispose(); return; }
    fly = made;
    if (!made) return;
    await made.ready;
    host.classList.add("ready");
    if (document.visibilityState !== "hidden") made.play();
  });
  const s: Shared = {
    host,
    users: 1,
    release: 0,
    dispose() {
      gone = true;
      document.removeEventListener("visibilitychange", onVisibility);
      fly?.dispose();
      host.remove();
      shared = null;
    },
  };
  shared = s;
  return s;
}

function WorldBackdrop() {
  const slot = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const s = acquire();
    slot.current?.appendChild(s.host);
    return () => {
      s.users--;
      if (s.users <= 0) s.release = window.setTimeout(() => s.dispose(), 1500);
    };
  }, []);
  return <div class="wf-backdrop" ref={slot} aria-hidden="true" />;
}

export const lobbyTheme: LobbyTheme = {
  className: "wf-lobby",
  Backdrop: WorldBackdrop,
  Banner,
  CardBadge,
  text: {
    pickCampaign: "The party's chronicle",
    newCampaign: "Begin a new chronicle",
    campaignName: "Name the chronicle",
    create: "Begin the tale",
    campaignMeta: chronicleMeta,
    campaignLabel: "Chronicle",
  },
};
