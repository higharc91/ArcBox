import { SurfaceScene } from "./surfaces";
import { useState } from "preact/hooks";
import type { CustomScreenProps } from "../module";
import { AbilityIcon, Crest, Logo, Portrait } from "./art";
import { conditionName } from "./conditions";
import { remembered } from "./memory";
import { usePackBase } from "./pack";
import { useAlertOnMount, useSfx } from "./sfx";
import type { AbilityChoice, CharacterChoicesData, ClassOption, EncounterOption, ReactData, ResultsData, StatusData, UnitView } from "./types";
import { FallBack } from "./fallback";
import { ReactionsSheet } from "./reactions";
import { ControlNotice, HpBar, HpText, MeCard, Section } from "./ui";
import { ChoicesCard } from "./choices";

export { costText, Turn } from "./turn";

// ---- Muster, encounter pick, results -------------------------------------------------------------------------------

export function PickClass({ data, send }: CustomScreenProps) {
  const { classes, portal, me } = data as { classes: ClassOption[]; portal?: string | null; me?: CharacterChoicesData | null };
  const sfx = useSfx();

  if (me && me.waiting > 0) {
    return (
      <div class="tx tx-muster">
        <Logo />
        <h2 class="tx-title">Welcome back, {me.name}</h2>
        <p class="tx-note center">You have pending level up choices.</p>
        <ChoicesCard choices={me} send={send} />
        {portal ? (
          <p class="tx-note center">
            Or <a class="tx-link" href={portal} target="_blank" rel="noopener">open your character sheet</a> in the portal.
          </p>
        ) : null}
      </div>
    );
  }

  return (
    <div class="tx tx-muster">
      <Logo />
      <h2 class="tx-title">Choose your class</h2>
      {portal ? (
        <p class="tx-note">
          Or <a class="tx-link" href={portal} target="_blank" rel="noopener">make your own character</a> (race, scores,
          background), then come back.
        </p>
      ) : null}
      <div class="tx-classes">
        {classes.map((c) => (
          <button type="button" key={c.id} class="tx-class" onClick={() => { sfx("confirm"); send("pickClass", { class: c.id }); }}>
            <Crest cls={c.id} scale={2} />
            <b>{c.name}</b>
            <span class="tx-role">{c.role}</span>
          </button>
        ))}
      </div>
    </div>
  );
}

export function PickEncounter({ data, send }: CustomScreenProps) {
  // canWalk: whether there's a world to go back to; a pack with no region has none, and an older server sends nothing (no button).
  const { encounters, canWalk, partyLevel } = data as { encounters: EncounterOption[]; canWalk?: boolean; partyLevel?: number };
  const sfx = useSfx();
  return (
    <div class="tx tx-muster tx-controls-layout"><div class="tx-controls-scroll">
      <h2 class="tx-title">Choose a battle</h2>
      {partyLevel != null ? <p class="tx-note center">Party level {partyLevel}</p> : null}
      <ul class="tx-encounters">
        {encounters.map((e) => (
          <li key={e.id}>
            <button type="button" class="tx-encounter" disabled={!e.fits}
              onClick={() => { sfx("confirm"); send("pickEncounter", { encounter: e.id }); }}>
              <span class={`tx-lv${e.level == null ? " unrated" : ""}`}>{e.level == null ? "Unrated" : <><small>Lv </small>{e.level}</>}</span>
              <span class="tx-enc-text">
                <b>{e.name}</b>
                <span class="tx-tip">
                  {e.min}–{e.max} heroes
                  {e.companions ? ` · +${e.companions} AI companion${e.companions === 1 ? "" : "s"}` : ""}
                </span>
                <span class="tx-enc-guidance">{e.guidance ?? (e.level == null ? "Level not specified" : "")}</span>
                {!e.fits ? <span class="tx-enc-reason">{e.unavailableReason ?? `Party too big (max ${e.max})`}</span> : null}
              </span>
            </button>
          </li>
        ))}
      </ul>
      </div>
      {canWalk ? (
        <div class="tx-foot">
          <button type="button" class="tx-btn ghost" onClick={() => { sfx("cancel"); send("backToWalk"); }}>Back to walking</button>
        </div>
      ) : null}
    </div>
  );
}

/** A moment banner from the pack's title art (VICTORY, DEFEAT), with its words for screen readers and as the fallback. */
function MomentBanner({ art, text, tone }: { art: string; text: string; tone: "won" | "lost" }) {
  const base = usePackBase();
  return (
    <h2 class={`tx-moment ${tone}`}>
      {base ? <img src={`${base}title/${art}.png`} alt="" draggable={false} /> : null}
      <span class={base ? "tx-sr" : ""}>{text}</span>
    </h2>
  );
}

/** A unit seen on this phone during the battle, by name; for portraits on screens whose view only names them. */
const seen = (name: string | null | undefined): UnitView | undefined =>
  name ? remembered().units.find((u) => u.name === name) : undefined;

function NamedPortrait({ name, size }: { name: string; size: 40 | 60 }) {
  const u = seen(name);
  const unit = u ?? { name, sprite: null, hp: 1, maxHp: 1, side: "Party" };
  return <Portrait unit={unit} cls={u ? remembered().classes[u.id] : undefined} size={size} />;
}

export function Results({ data, send }: CustomScreenProps) {
  const r = data as ResultsData;
  const won = r.outcome === "Won";
  // After a loss the server's line says it all: no "+0 XP" or injuries on each hero.
  const lost = r.outcome === "Lost";
  const fled = r.outcome === "Fled";
  const bannerArt = r.leveledUp ? "level_up" : won ? "victory" : "defeat";
  const bannerText = r.leveledUp ? "Level Up!" : won ? "Victory" : fled ? "Retreat" : "Defeat";
  return (
    <div class="tx tx-results">
      <ControlNotice notice={r.controlNotice} />
      <MomentBanner art={bannerArt} text={bannerText} tone={won ? "won" : "lost"} />
      <p class="tx-note center">{r.encounter} · party level {r.level}</p>
      {r.leveledUp ? <p class="tx-note center tx-levelup-note">LEVEL UP! The party reached level {r.level}.</p> : null}
      {r.note ? <p class="tx-note center tx-result-note">{r.note}</p> : null}
      <ul class="tx-lines">
        {r.lines.map((l) => {
          const prev = l.prevLevelXp ?? 0;
          const next = l.nextLevelXp;
          const cur = l.totalXp;
          const pct = next && next > prev && cur !== undefined
            ? Math.max(0, Math.min(100, Math.round(((cur - prev) / (next - prev)) * 100)))
            : 100;
          return (
            <li key={l.name}>
              <NamedPortrait name={l.name} size={40} />
              <div class="tx-line-main">
                <b>{l.name}</b>
                {cur !== undefined ? (
                  <div class="tx-xp-progress" aria-label={`XP progress: ${cur} of ${next ?? cur} XP`}>
                    <div class="tx-xp-bar">
                      <div class="tx-xp-fill" style={{ width: `${pct}%` }} />
                    </div>
                    <span class="tx-xp-text">{cur} / {next ?? "Max"} XP{l.level ? ` (Lv ${l.level})` : ""}</span>
                  </div>
                ) : null}
              </div>
              {lost ? null : <span class="tx-xp">+{l.xp} XP</span>}
              {!lost && l.injuries.length > 0 ? <span class="tx-injury">{l.injuries.join(", ")}</span> : null}
            </li>
          );
        })}
      </ul>
      {r.gold ? <p class="tx-loot-line">Gold won: +{r.gold} gold</p> : null}
      {r.loot && r.loot.length > 0 ? (
        <p class="tx-loot-line">Loot: {r.loot.map(it => `${it.name}${it.count && it.count > 1 ? ` x${it.count}` : ""}`).join(", ")}</p>
      ) : null}
      {r.choices && r.choices.waiting > 0 ? <ChoicesCard choices={r.choices} send={send} /> : null}
      {r.vip && (lost || fled) && r.difficulty !== "Story" ? <p class="tx-note center tx-difficulty-hint">Too hard? After Carry on, change difficulty in the party menu</p> : null}
      {r.portal ? <p><a class="tx-link" href={r.portal} target="_blank" rel="noopener">Your character</a></p> : null}
      {r.vip ? (
        <div class="tx-foot">
          <button type="button" class="tx-btn gold" onClick={() => send("next")}>Carry on</button>
          <button type="button" class="tx-btn ghost" onClick={() => send("end")}>End session</button>
        </div>
      ) : <p class="tx-note center">Waiting for the host…</p>}
    </div>
  );
}

// ---- Someone else's turn, and reactions ------------------------------------------------------------------------------

type Tab = "abilities" | "sheet" | "bag";

/**
 * Waiting your turn: who's acting, when you're up, and your character (sheet, abilities and bag as of your last turn,
 * which this phone remembers, since the status view doesn't carry them).
 */
export function Status({ data, send }: CustomScreenProps) {
  const s = data as StatusData;
  const [tab, setTab] = useState<Tab>("sheet");
  const sfx = useSfx();
  const cls = remembered().classes[s.me.id];
  const abilities = remembered().abilities[s.me.id] ?? [];
  const active = seen(s.active);
  return (
    <div class="tx tx-status">
      <SurfaceScene kinds={s.surfaces} />
      <MeCard me={s.me} cls={cls} />
      <ControlNotice notice={s.controlNotice} />
      <div class={`tx-now${active?.side === "Enemy" ? " enemy" : ""}`}>
        {s.active ? <NamedPortrait name={s.active} size={60} /> : null}
        <div>
          <span class="tx-eyebrow">{s.active ? "Now acting" : "Waiting"}</span>
          <b class="tx-now-name">{s.active ?? "Waiting…"}</b>
          {s.aiPlaying && !s.controlNotice ? <span class="tx-note">The AI is playing your character</span> : null}
        </div>
      </div>
      {s.upIn ? <Section>You're up in {s.upIn}</Section> : null}
      <FallBack fallBack={s.fallBack} send={send} />
      <ReactionsSheet reactions={s.reactions} send={send} />

      <div class="tx-tabs" role="tablist">
        {(["abilities", "sheet", "bag"] as const).map((t) => (
          <button type="button" role="tab" key={t} aria-selected={tab === t} class={tab === t ? "on" : ""}
            onClick={() => { sfx("select"); setTab(t); }}>
            {t === "abilities" ? "Abilities" : t === "sheet" ? "Sheet" : "Bag"}
          </button>
        ))}
      </div>
      <div role="tabpanel" class="tx-tabpanel">
        {tab === "sheet" ? <Sheet me={s.me} /> : null}
        {tab === "abilities" ? (
          <Known list={abilities.filter((a) => !a.item)} empty="Your abilities show here after your first turn." />
        ) : null}
        {tab === "bag" ? <Known list={abilities.filter((a) => a.item)} empty="Nothing in your bag to use." /> : null}
      </div>
    </div>
  );
}

function Sheet({ me }: { me: UnitView }) {
  return (
    <>
      <div class="tx-card">
        <b>Hit points</b>
        <HpBar unit={me} />
        <span class="tx-tip"><HpText unit={me} />{me.life !== "Up" ? ` · ${me.life}` : ""}</span>
      </div>
      <div class="tx-card">
        <b>Conditions</b>
        <span class="tx-tip">{me.conditions.length > 0 ? me.conditions.map(conditionName).join(", ") : "None"}</span>
      </div>
    </>
  );
}

/** Abilities or items as of your last turn: read-only, for planning ahead. */
function Known({ list, empty }: { list: AbilityChoice[]; empty: string }) {
  if (list.length === 0) return <p class="tx-note">{empty}</p>;
  return (
    <ul class="tx-known">
      {list.map((a) => (
        <li key={`${a.ability}/${a.item ?? ""}`} class="tx-ability static">
          <span class="tx-ic"><AbilityIcon ability={a.ability} item={a.item} /></span>
          <span class="tx-ab-text"><b>{a.name}</b><span class="tx-tip">{a.tooltip}</span></span>
        </li>
      ))}
    </ul>
  );
}

const TRIGGERS: Record<string, string> = {
  leavesReach: "A foe is leaving your reach",
  beforeHit: "You're about to be hit",
  spellCast: "A foe is casting a spell",
};

/** "Goblin's Attack is hitting you: 16 against AC 11", from the reaction's cause; null when there's nothing to say. */
export function reactionDetail(r: ReactData): string | null {
  if (!r.actor) return null;
  const what = r.ability ? `${r.actor}'s ${r.ability}` : r.actor;
  switch (r.trigger) {
    case "beforeHit": {
      const roll = r.cause?.roll;
      return `${what} is hitting ${r.targetName ?? "you"}${roll ? `: ${roll.total} against AC ${roll.vs}` : ""}`;
    }
    case "leavesReach": return `${r.actor} is moving out of your reach`;
    case "spellCast": return `${r.actor} is casting ${r.ability ?? "a spell"}`;
    default: return what;
  }
}

export function ReactPrompt({ data, send }: CustomScreenProps) {
  const r = data as ReactData;
  const detail = reactionDetail(r);
  const sfx = useSfx();
  useAlertOnMount("reaction", "react");
  const answer = (id: string) => {
    sfx(id === "pass" ? "cancel" : "confirm");
    send("react", { choice: id });
  };
  const pass = r.options.find((o) => o.id === "pass");
  return (
    <div class="tx tx-react">
      <ControlNotice notice={r.controlNotice} />
      <SurfaceScene kinds={r.surfaces} />
      <FallBack fallBack={r.fallBack} send={send} />
      <div class="tx-alert" role="alert">
        <span class="tx-eyebrow">Reaction</span>
        <p class="tx-alert-title">{TRIGGERS[r.trigger] ?? "You can react"}</p>
        {detail ? <p class="tx-react-detail">{detail}</p> : null}
      </div>
      <Section>Your reactions</Section>
      <ul class="tx-abilities tx-react-options">
        {r.options.filter((o) => o.id !== "pass").map((o) => (
          <li key={o.id}>
            <button type="button" class="tx-ability" onClick={() => answer(o.id)}>
              <span class="tx-ic"><AbilityIcon ability={o.id} /></span>
              <span class="tx-ab-text">
                <b>{o.label}</b>
                {o.tooltip ? <span class="tx-react-tip">{o.tooltip}</span> : null}
              </span>
            </button>
          </li>
        ))}
      </ul>
      {pass ? (
        <div class="tx-foot">
          <button type="button" class="tx-btn ghost" onClick={() => answer(pass.id)}>{pass.label}</button>
        </div>
      ) : null}
    </div>
  );
}
