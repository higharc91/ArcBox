import { useEffect, useMemo, useState } from "preact/hooks";
import type { PortalProps } from "../module";
import { Crest, Logo } from "./art";
import { Section } from "./ui";

/**
 * Wardenflame between sessions (sub-project 4): the party, your character, and what you can do to it. With no character
 * (or one that hasn't fought yet) you make one: race, class, background and a 27-point buy. After that you choose your
 * subclass at level 3, spend ability score increases at 4 and 8, and change your picks. Every rule is the server's
 * (`TacticsCampaignRules`); this screen only offers what the view says and shows the server's refusal when there is one.
 */

const STATS = ["str", "dex", "con", "int", "wis", "cha"] as const;
type StatKey = (typeof STATS)[number];
const COST: Record<number, number> = { 8: 0, 9: 1, 10: 2, 11: 3, 12: 4, 13: 5, 14: 7, 15: 9 };
/** The twelve classes, for finding one in a "Dwarf Cleric" description. */
const CLASSES = ["barbarian", "bard", "cleric", "druid", "fighter", "monk", "paladin", "ranger", "rogue", "sorcerer", "warlock", "wizard"];

interface Option { id: string; name: string; tooltip: string }
interface Choice { level: number; choose: number | null; from: Option[] }
interface Me {
  name: string; describe: string; race: string; class: string; subclass: string | null; background: string; level: number; xp: number;
  scores: Record<StatKey, number>; picks: string[]; injuries: string[]; increasesLeft: number;
  /** Remaking now would use the one free remake after fighting. */
  freeRemake?: boolean;
  subclasses: { id: string; name: string }[] | null; choices: Choice[];
}
interface Options {
  races: { id: string; name: string }[]; classes: { id: string; name: string; role: string }[];
  backgrounds: { id: string; name: string; boosts: Partial<Record<StatKey, number>> }[]; pointBuy: number;
}
export interface TacticsPortalView {
  level: number; completed: string[]; party: { name: string; character: string; xp: number; you: boolean }[];
  me: Me | null; options: Options | null;
}

export const pointsSpent = (scores: Record<StatKey, number>) => STATS.reduce((n, s) => n + (COST[scores[s]] ?? 99), 0);
const modifier = (score: number) => Math.floor((score - 10) / 2);
const signed = (n: number) => (n >= 0 ? `+${n}` : `−${-n}`);
const classIn = (describe: string) => CLASSES.find((c) => describe.toLowerCase().split(/\s+/).includes(c));

export function TacticsPortal({ campaign, readOnly, send }: PortalProps) {
  const view = campaign.view as TacticsPortalView;
  const [making, setMaking] = useState(view.me === null);
  // A new view (after a create, or any change) shows the sheet again, unless there's still no character.
  useEffect(() => setMaking(view.me === null), [JSON.stringify(view.me)]);
  return (
    <div class="tx tx-portal">
      <header class="tx-portal-head">
        <Logo small />
        <div>
          <h1 class="portal-title">{campaign.info.name}</h1>
          <p class="tx-note">Party level {view.level} · {view.completed.length} battles won</p>
        </div>
      </header>
      <Section>The party</Section>
      <ul class="tx-party">
        {view.party.map((p, i) => {
          const cls = classIn(p.character);
          return (
            <li key={`${i}-${p.name}`} class={p.you ? "you" : ""}>
              {cls ? <Crest cls={cls} /> : <span class="tx-crest blank x1" aria-hidden="true" />}
              <b>{p.name}</b>{p.you ? " (you)" : ""} <span class="tx-note">{p.character}</span>
            </li>
          );
        })}
      </ul>
      {making && view.options ? (
        <Create options={view.options} readOnly={readOnly} send={send} onCancel={view.me ? () => setMaking(false) : undefined}
          freeRemake={view.me?.freeRemake ?? false} />
      ) : view.me ? (
        <Sheet me={view.me} readOnly={readOnly} send={send} onRemake={view.options ? () => setMaking(true) : undefined} />
      ) : null}
    </div>
  );
}

/** A grid of mutually exclusive choices (race, background): one pressed at a time. */
function Pills<T extends { id: string; name: string }>({ label, items, value, onPick, readOnly }: {
  label: string; items: T[]; value: string; onPick(id: string): void; readOnly: boolean;
}) {
  return (
    <div class="tx-pills" role="group" aria-label={label}>
      {items.map((it) => (
        <button type="button" key={it.id} class={it.id === value ? "on" : ""} aria-pressed={it.id === value} disabled={readOnly}
          onClick={() => onPick(it.id)}>{it.name}</button>
      ))}
    </div>
  );
}

function Create({ options, readOnly, send, onCancel, freeRemake }: {
  options: Options; readOnly: boolean; send: PortalProps["send"]; onCancel?: () => void; freeRemake: boolean;
}) {
  const [race, setRace] = useState(options.races[0]?.id ?? "");
  const [cls, setCls] = useState(options.classes[0]?.id ?? "");
  const [background, setBackground] = useState(options.backgrounds[0]?.id ?? "");
  const [scores, setScores] = useState<Record<StatKey, number>>({ str: 8, dex: 8, con: 8, int: 8, wis: 8, cha: 8 });
  const left = options.pointBuy - pointsSpent(scores);
  const boosts = options.backgrounds.find((b) => b.id === background)?.boosts ?? {};
  const step = (s: StatKey, d: number) => {
    const next = scores[s] + d;
    if (next < 8 || next > 15) return;
    const tried = { ...scores, [s]: next };
    if (pointsSpent(tried) <= options.pointBuy) setScores(tried);
  };
  return (
    <section class="tx-panel">
      <h2 class="tx-title">Make your character</h2>
      {freeRemake ? (
        <p class="portal-banner" role="note">
          This is your one free remake. Your XP, injuries and bag carry over, and after this the character is yours for good.
        </p>
      ) : null}

      <Section>Race</Section>
      <Pills label="Race" items={options.races} value={race} onPick={setRace} readOnly={readOnly} />

      <Section>Class</Section>
      <div class="tx-classes" role="group" aria-label="Class">
        {options.classes.map((c) => (
          <button type="button" key={c.id} class={`tx-class${c.id === cls ? " on" : ""}`} aria-pressed={c.id === cls} disabled={readOnly}
            onClick={() => setCls(c.id)}>
            <Crest cls={c.id} scale={2} />
            <b>{c.name}</b>
            <span class="tx-role">{c.role}</span>
          </button>
        ))}
      </div>

      <Section>Background</Section>
      <Pills label="Background" items={options.backgrounds} value={background} onPick={setBackground} readOnly={readOnly} />
      <p class="tx-note">
        {Object.entries(boosts).map(([s, n]) => `${s.toUpperCase()} +${n}`).join(", ") || "No score boosts"}
      </p>

      <p class="tx-sect tx-points">Ability scores: {left} of {options.pointBuy} points left</p>
      <div class="tx-scores">
        {STATS.map((s) => {
          const total = scores[s] + (boosts[s] ?? 0);
          return (
            <div class={`tx-score-row${boosts[s] ? " boosted" : ""}`} key={s}>
              <span class="tx-score-name">{s.toUpperCase()}</span>
              <span class="tx-score">{scores[s]}{boosts[s] ? <em>+{boosts[s]}</em> : null}</span>
              <span class="tx-score-mod">{signed(modifier(total))}</span>
              <button type="button" class="tx-step" aria-label={`Lower ${s}`} disabled={readOnly || scores[s] <= 8} onClick={() => step(s, -1)}>−</button>
              <button type="button" class="tx-step" aria-label={`Raise ${s}`}
                disabled={readOnly || scores[s] >= 15 || COST[scores[s] + 1]! - COST[scores[s]]! > left} onClick={() => step(s, 1)}>+</button>
            </div>
          );
        })}
      </div>
      <div class="tx-foot">
        {onCancel ? <button type="button" class="tx-btn ghost" onClick={onCancel}>Cancel</button> : null}
        <button type="button" class="tx-btn gold" disabled={readOnly}
          onClick={() => void send("createCharacter", { race, class: cls, background, scores })}>Create</button>
      </div>
    </section>
  );
}

function Sheet({ me, readOnly, send, onRemake }: { me: Me; readOnly: boolean; send: PortalProps["send"]; onRemake?: () => void }) {
  return (
    <section class="tx-panel">
      <div class="tx-hero">
        <Crest cls={me.class} scale={2} />
        <div>
          <h2 class="tx-title">{me.name}</h2>
          <span class="tx-note">{me.describe} · Lv {me.level}</span>
          <span class="tx-note">{me.xp} XP{me.injuries.length ? ` · ${me.injuries.join(", ")}` : ""}</span>
        </div>
      </div>
      <div class="tx-score-grid">
        {STATS.map((s) => (
          <div key={s}><small>{s.toUpperCase()}</small><b class="tx-score">{me.scores[s]}</b><i>{signed(modifier(me.scores[s]))}</i></div>
        ))}
      </div>
      {me.subclasses ? (
        <div class="tx-block">
          <h3 class="tx-sect">Choose your subclass</h3>
          <div class="tx-subclasses">
            {me.subclasses.map((s) => (
              <button type="button" class="tx-subclass" key={s.id} disabled={readOnly}
                onClick={() => void send("chooseSubclass", { subclass: s.id })}>{s.name}</button>
            ))}
          </div>
        </div>
      ) : null}
      {me.increasesLeft >= 2 ? <Increase readOnly={readOnly} send={send} /> : null}
      <Picks me={me} readOnly={readOnly} send={send} />
      {onRemake ? <button type="button" class="tx-btn ghost" disabled={readOnly} onClick={onRemake}>Remake character</button> : null}
    </section>
  );
}

function Increase({ readOnly, send }: { readOnly: boolean; send: PortalProps["send"] }) {
  const [chosen, setChosen] = useState<StatKey[]>([]);
  const toggle = (s: StatKey) => setChosen((c) => (c.includes(s) ? c.filter((x) => x !== s) : [...c, s].slice(-2)));
  return (
    <div class="tx-block">
      <h3 class="tx-sect">Ability score increase</h3>
      <p class="tx-note">One score +2, or two scores +1.</p>
      <div class="tx-pills six">
        {STATS.map((s) => (
          <button type="button" key={s} class={chosen.includes(s) ? "on" : ""} aria-pressed={chosen.includes(s)} disabled={readOnly}
            onClick={() => toggle(s)}>{s.toUpperCase()}</button>
        ))}
      </div>
      <button type="button" class="tx-btn gold" disabled={readOnly || chosen.length === 0}
        onClick={() => void send("spendIncrease", { stats: chosen })}>
        {chosen.length === 1 ? `${chosen[0]!.toUpperCase()} +2` : chosen.length === 2 ? `${chosen.map((c) => c.toUpperCase()).join(" +1, ")} +1` : "Pick scores"}
      </button>
    </div>
  );
}

/** Your picks, by the feature they come from. Saving sends the whole set; the server checks it fits the features. */
function Picks({ me, readOnly, send }: { me: Me; readOnly: boolean; send: PortalProps["send"] }) {
  const [picks, setPicks] = useState<string[]>(me.picks);
  useEffect(() => setPicks(me.picks), [me.picks.join(",")]);
  const allowed = useMemo(() => me.choices.reduce((n, c) => n + (c.choose ?? 0), 0), [me.choices]);
  const changed = picks.slice().sort().join(",") !== me.picks.slice().sort().join(",");
  const toggle = (id: string) => setPicks((p) => (p.includes(id) ? p.filter((x) => x !== id) : [...p, id]));
  if (me.choices.length === 0) return null;
  return (
    <div class="tx-block">
      <h3 class="tx-sect">Picks <span class="tx-count">{picks.length} of {allowed}</span></h3>
      {me.choices.map((c, i) => (
        <fieldset key={i} class="tx-choice">
          <legend>Level {c.level}: choose {c.choose}</legend>
          {c.from.map((o) => (
            <label key={o.id} class={`tx-pick${picks.includes(o.id) ? " on" : ""}`}>
              <input type="checkbox" checked={picks.includes(o.id)} disabled={readOnly} onChange={() => toggle(o.id)} />
              <b>{o.name}</b>
              <span class="tx-pick-tip">{o.tooltip}</span>
            </label>
          ))}
        </fieldset>
      ))}
      <button type="button" class="tx-btn gold" disabled={readOnly || !changed} onClick={() => void send("setPicks", { picks })}>Save picks</button>
    </div>
  );
}
