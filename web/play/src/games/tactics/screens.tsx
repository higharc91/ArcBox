import { useState } from "preact/hooks";
import type { CustomScreenProps } from "../module";
import { AbilityIcon, Crest, Logo, Portrait } from "./art";
import { conditionName } from "./conditions";
import { remembered } from "./memory";
import { usePackBase } from "./pack";
import { useAlertOnMount, useSfx } from "./sfx";
import type { AbilityChoice, ClassOption, EncounterOption, ReactData, ResultsData, StatusData, UnitView } from "./types";
import { HpBar, HpText, MeCard, Section } from "./ui";

export { costText, Turn } from "./turn";

// ---- Muster, encounter pick, results -------------------------------------------------------------------------------

export function PickClass({ data, send }: CustomScreenProps) {
  const { classes, portal } = data as { classes: ClassOption[]; portal?: string | null };
  const sfx = useSfx();
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
  const { encounters } = data as { encounters: EncounterOption[] };
  const sfx = useSfx();
  return (
    <div class="tx tx-muster">
      <h2 class="tx-title">Choose a battle</h2>
      <ul class="tx-encounters">
        {encounters.map((e) => (
          <li key={e.id}>
            <button type="button" class="tx-encounter" disabled={!e.fits}
              onClick={() => { sfx("confirm"); send("pickEncounter", { encounter: e.id }); }}>
              <span class="tx-lv"><small>Lv </small>{e.level ?? "–"}</span>
              <span class="tx-enc-text">
                <b>{e.name}</b>
                <span class="tx-tip">
                  {e.min}–{e.max} heroes
                  {e.companions ? ` · +${e.companions} AI companion${e.companions === 1 ? "" : "s"}` : ""}
                </span>
              </span>
            </button>
          </li>
        ))}
      </ul>
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
  return (
    <div class="tx tx-results">
      <MomentBanner art={won ? "victory" : "defeat"} text={won ? "Victory" : "Defeat"} tone={won ? "won" : "lost"} />
      <p class="tx-note center">{r.encounter} · party level {r.level}</p>
      <ul class="tx-lines">
        {r.lines.map((l) => (
          <li key={l.name}>
            <NamedPortrait name={l.name} size={40} />
            <b>{l.name}</b>
            <span class="tx-xp">+{l.xp} XP</span>
            {l.injuries.length > 0 ? <span class="tx-injury">{l.injuries.join(", ")}</span> : null}
          </li>
        ))}
      </ul>
      {r.portal ? <p><a class="tx-link" href={r.portal} target="_blank" rel="noopener">Your character: spend XP and change picks</a></p> : null}
      {r.vip ? (
        <div class="tx-foot">
          <button type="button" class="tx-btn gold" onClick={() => send("next")}>Next battle</button>
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
export function Status({ data }: CustomScreenProps) {
  const s = data as StatusData;
  const [tab, setTab] = useState<Tab>("sheet");
  const sfx = useSfx();
  const cls = remembered().classes[s.me.id];
  const abilities = remembered().abilities[s.me.id] ?? [];
  const active = seen(s.active);
  return (
    <div class="tx tx-status">
      <MeCard me={s.me} cls={cls} />
      <div class={`tx-now${active?.side === "Enemy" ? " enemy" : ""}`}>
        {s.active ? <NamedPortrait name={s.active} size={60} /> : null}
        <div>
          <span class="tx-eyebrow">{s.active ? "Now acting" : "Waiting"}</span>
          <b class="tx-now-name">{s.active ?? "Waiting…"}</b>
          {s.aiPlaying ? <span class="tx-note">The AI is playing your character</span> : null}
        </div>
      </div>
      {s.upIn ? <Section>You're up in {s.upIn}</Section> : null}

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
  useAlertOnMount("reaction");
  const answer = (id: string) => {
    sfx(id === "pass" ? "cancel" : "confirm");
    send("react", { choice: id });
  };
  const pass = r.options.find((o) => o.id === "pass");
  return (
    <div class="tx tx-react">
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
