import { useEffect, useMemo, useState } from "preact/hooks";
import type { PortalProps } from "../module";

/**
 * ArcBox Tactics between sessions (sub-project 4): the party, your character, and what you can do to it. With no character
 * (or one that hasn't fought yet) you make one: race, class, background and a 27-point buy. After that you choose your
 * subclass at level 3, spend ability score increases at 4 and 8, and change your picks. Every rule is the server's
 * (`TacticsCampaignRules`); this screen only offers what the view says and shows the server's refusal when there is one.
 */

const STATS = ["str", "dex", "con", "int", "wis", "cha"] as const;
type StatKey = (typeof STATS)[number];
const COST: Record<number, number> = { 8: 0, 9: 1, 10: 2, 11: 3, 12: 4, 13: 5, 14: 7, 15: 9 };

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

export function TacticsPortal({ campaign, readOnly, send }: PortalProps) {
  const view = campaign.view as TacticsPortalView;
  const [making, setMaking] = useState(view.me === null);
  // A new view (after a create, or any change) shows the sheet again, unless there's still no character.
  useEffect(() => setMaking(view.me === null), [JSON.stringify(view.me)]);
  return (
    <div class="tx tx-portal">
      <h1 class="portal-title">{campaign.info.name}</h1>
      <p class="tx-dim">Party level {view.level} · {view.completed.length} battles won</p>
      <ul class="tx-lines">
        {view.party.map((p, i) => (
          <li key={`${i}-${p.name}`}><b>{p.name}</b>{p.you ? " (you)" : ""} <span class="tx-dim">{p.character}</span></li>
        ))}
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
      <h2>Make your character</h2>
      {freeRemake ? (
        <p class="portal-banner" role="note">
          This is your one free remake. Your XP, injuries and bag carry over, and after this the character is yours for good.
        </p>
      ) : null}
      <label>Race <select value={race} onChange={(e) => setRace(e.currentTarget.value)} disabled={readOnly}>
        {options.races.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
      </select></label>
      <label>Class <select value={cls} onChange={(e) => setCls(e.currentTarget.value)} disabled={readOnly}>
        {options.classes.map((c) => <option key={c.id} value={c.id}>{c.name} ({c.role})</option>)}
      </select></label>
      <label>Background <select value={background} onChange={(e) => setBackground(e.currentTarget.value)} disabled={readOnly}>
        {options.backgrounds.map((b) => <option key={b.id} value={b.id}>{b.name}</option>)}
      </select></label>
      <p class="tx-dim">Ability scores: {left} of {options.pointBuy} points left</p>
      <table class="tx-scores">
        <tbody>
          {STATS.map((s) => (
            <tr key={s}>
              <th>{s.toUpperCase()}</th>
              <td><button type="button" aria-label={`Lower ${s}`} disabled={readOnly || scores[s] <= 8} onClick={() => step(s, -1)}>−</button></td>
              <td class="tx-score">{scores[s]}{boosts[s] ? <small> +{boosts[s]}</small> : null}</td>
              <td><button type="button" aria-label={`Raise ${s}`} disabled={readOnly || scores[s] >= 15 || COST[scores[s] + 1]! - COST[scores[s]]! > left}
                onClick={() => step(s, 1)}>+</button></td>
            </tr>
          ))}
        </tbody>
      </table>
      <div class="tx-actions">
        <button type="button" class="big" disabled={readOnly}
          onClick={() => void send("createCharacter", { race, class: cls, background, scores })}>Create</button>
        {onCancel ? <button type="button" onClick={onCancel}>Cancel</button> : null}
      </div>
    </section>
  );
}

function Sheet({ me, readOnly, send, onRemake }: { me: Me; readOnly: boolean; send: PortalProps["send"]; onRemake?: () => void }) {
  return (
    <section class="tx-panel">
      <h2>{me.name} <small class="tx-dim">{me.describe} · Lv {me.level}</small></h2>
      <p class="tx-dim">{me.xp} XP{me.injuries.length ? ` · ${me.injuries.join(", ")}` : ""}</p>
      <table class="tx-scores">
        <tbody>
          <tr>{STATS.map((s) => <th key={s}>{s.toUpperCase()}</th>)}</tr>
          <tr>{STATS.map((s) => <td key={s} class="tx-score">{me.scores[s]}</td>)}</tr>
        </tbody>
      </table>
      {me.subclasses ? (
        <div>
          <h3>Choose your subclass</h3>
          <div class="tx-actions">
            {me.subclasses.map((s) => (
              <button type="button" key={s.id} disabled={readOnly} onClick={() => void send("chooseSubclass", { subclass: s.id })}>{s.name}</button>
            ))}
          </div>
        </div>
      ) : null}
      {me.increasesLeft >= 2 ? <Increase readOnly={readOnly} send={send} /> : null}
      <Picks me={me} readOnly={readOnly} send={send} />
      {onRemake ? <button type="button" disabled={readOnly} onClick={onRemake}>Remake character</button> : null}
    </section>
  );
}

function Increase({ readOnly, send }: { readOnly: boolean; send: PortalProps["send"] }) {
  const [chosen, setChosen] = useState<StatKey[]>([]);
  const toggle = (s: StatKey) => setChosen((c) => (c.includes(s) ? c.filter((x) => x !== s) : [...c, s].slice(-2)));
  return (
    <div>
      <h3>Ability score increase</h3>
      <p class="tx-dim">One score +2, or two scores +1.</p>
      <div class="tx-actions">
        {STATS.map((s) => (
          <button type="button" key={s} class={chosen.includes(s) ? "on" : ""} aria-pressed={chosen.includes(s)} disabled={readOnly}
            onClick={() => toggle(s)}>{s.toUpperCase()}</button>
        ))}
      </div>
      <button type="button" class="big" disabled={readOnly || chosen.length === 0}
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
    <div>
      <h3>Picks <small class="tx-dim">{picks.length} of {allowed}</small></h3>
      {me.choices.map((c, i) => (
        <fieldset key={i} class="tx-choice">
          <legend>Level {c.level}: choose {c.choose}</legend>
          {c.from.map((o) => (
            <label key={o.id} class="tx-pick">
              <input type="checkbox" checked={picks.includes(o.id)} disabled={readOnly} onChange={() => toggle(o.id)} />
              <b>{o.name}</b>
              <span class="tx-pick-tip">{o.tooltip}</span>
            </label>
          ))}
        </fieldset>
      ))}
      <button type="button" class="big" disabled={readOnly || !changed} onClick={() => void send("setPicks", { picks })}>Save picks</button>
    </div>
  );
}
