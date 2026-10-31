import { useEffect, useRef } from "preact/hooks";
import { tileStyle, useMap, type MapData } from "../games/tactics/map";

/**
 * A browser stand-in for the TV's ArcBox Tactics screens (the Godot scene is milestone 3b): the map from the content
 * pack, the units, the turn order and the battle log, and the phase screens around a battle. It plays the TV's part in
 * the pacing: once a new beat has been on screen for a moment it sends `shown` (adapter spec §4.3), so AI turns move at a
 * watchable speed instead of waiting out the game's fallback timer.
 */

type Tile = [number, number];

interface UnitView {
  id: string; name: string; side: string; sprite: string | null; at: Tile; hp: number; maxHp: number; tempHp: number;
  life: string; countdown: number; conditions: string[]; player: string | null;
}
interface Beat { seq: number; actor: string | null; events: BattleEvent[] }
interface BattleView {
  encounter: string; map: string; round: number; order: string[]; active: string | null; units: UnitView[]; beats: Beat[];
  outcome: string | null;
}
export interface TacticsHostView {
  phase: string; banner: string; notice: string | null; partyLevel: number; mustered: string[];
  encounters: { id: string; name: string; level: number | null; min: number; max: number; fits: boolean }[] | null;
  battle: BattleView | null; outcome: string | null; results: { name: string; xp: number; injuries: string[] }[] | null;
}
type BattleEvent = { $type: string } & Record<string, unknown>;

export const isTacticsHostView = (v: unknown): v is TacticsHostView =>
  typeof v === "object" && v !== null && typeof (v as TacticsHostView).phase === "string" && "mustered" in v;

/** How long a beat stays on screen before the board says it's been shown. */
const BEAT_MS = 700;

/** One line for an event, or null for the ones the log leaves out. */
export function describe(e: BattleEvent, name: (id: unknown) => string): string | null {
  const n = (k: string) => name(e[k]);
  switch (e.$type) {
    case "RoundStarted": return `— Round ${String(e.round)} —`;
    case "TurnStarted": return `${n("unit")}'s turn`;
    case "AbilityUsed": {
      const ability = String(e.ability).split(".").pop()!.replaceAll("_", " ");
      const target = e.target as { unit?: string | null } | null;
      return `${n("source")} uses ${ability}${target?.unit && target.unit !== e.source ? ` on ${name(target.unit)}` : ""}`;
    }
    case "AttackResolved": return `${n("source")} ${e.hit ? (e.crit ? "CRITS" : "hits") : "misses"} ${n("target")}`;
    case "Damaged": return `${n("unit")} takes ${String(e.amount)}${e.type ? ` ${String(e.type)}` : ""} (${String(e.hpAfter)} HP left)`;
    case "Healed": return `${n("unit")} heals ${String(e.amount)}`;
    case "Downed": return `${n("unit")} is DOWNED`;
    case "Died": return `${n("unit")} dies`;
    case "UnitOut": return `${n("unit")} is out of the fight`;
    case "Revived": return `${n("unit")} is back up`;
    case "ConditionApplied": return `${n("unit")} is ${String(e.condition).replaceAll("_", " ")}`;
    case "Reacted": return e.choice === "pass"
      ? `${n("reactor")} lets it go`
      : `${n("reactor")} reacts: ${String(e.choice).split(".").pop()!.replaceAll("_", " ")}`;
    case "Said": return `${n("speaker")}: “${String(e.line)}”`;
    case "TriggerFired": return `⚑ ${String(e.trigger).replaceAll("_", " ")}`;
    case "BattleEnded": return `Battle over: ${String(e.outcome)}`;
    default: return null;
  }
}

export function TacticsBoard({ view, signal }: { view: TacticsHostView; signal(kind: string, payload: unknown): void }) {
  const battle = view.battle;
  const map = useMap(battle?.map);
  const shown = useRef(0);
  const latest = battle?.beats.at(-1)?.seq ?? 0;

  // Play the TV's part: a new beat stays up for a moment, then the game hears it's been shown.
  useEffect(() => {
    if (!latest || latest <= shown.current) return;
    const id = setTimeout(() => {
      shown.current = latest;
      signal("shown", { seq: latest });
    }, BEAT_MS);
    return () => clearTimeout(id);
  }, [latest]);

  return (
    <div class="tx-board">
      <header class="tx-board-banner">
        <b>{view.banner}</b>
        <small>Party level {view.partyLevel}{battle ? ` · ${battle.encounter.replaceAll("_", " ")} · round ${battle.round}` : ""}</small>
      </header>
      {view.notice ? <p class="tx-board-notice">{view.notice}</p> : null}
      {view.phase === "Muster" ? <p class="tx-board-note">Ready: {view.mustered.join(", ") || "nobody yet"}</p> : null}
      {view.encounters ? (
        <ul class="tx-board-list">
          {view.encounters.map((e) => <li key={e.id} class={e.fits ? "" : "dim"}>{e.name} <small>Lv {e.level ?? "?"} · {e.min}–{e.max}</small></li>)}
        </ul>
      ) : null}
      {view.results ? (
        <ul class="tx-board-list">
          {view.results.map((r) => <li key={r.name}>{r.name} <small>+{r.xp} XP{r.injuries.length ? ` · ${r.injuries.join(", ")}` : ""}</small></li>)}
        </ul>
      ) : null}
      {battle ? <Battle battle={battle} map={map} /> : null}
    </div>
  );
}

function Battle({ battle, map }: { battle: BattleView; map: MapData | null }) {
  const names = new Map(battle.units.map((u) => [u.id, u.name]));
  const name = (id: unknown) => (typeof id === "string" ? names.get(id) ?? id : "?");
  const w = map?.w ?? Math.max(...battle.units.map((u) => u.at[0])) + 2;
  const h = map?.h ?? Math.max(...battle.units.map((u) => u.at[1])) + 2;
  const lines = battle.beats.flatMap((b) => b.events.map((e) => ({ seq: b.seq, text: describe(e, name) })))
    .filter((l): l is { seq: number; text: string } => l.text !== null).slice(-14);
  const latest = battle.beats.at(-1)?.seq;
  return (
    <div class="tx-board-battle">
      <div class="tx-board-map" style={{ gridTemplateColumns: `repeat(${w}, 1fr)` }}>
        {Array.from({ length: w * h }, (_, i) => {
          const x = i % w, y = Math.floor(i / w);
          const u = battle.units.find((v) => v.at[0] === x && v.at[1] === y);
          return (
            <div key={i} class={`tx-board-tile${u && u.id === battle.active ? " active" : ""}`}
              style={map ? tileStyle(map, i) : { background: "#2a2a2a" }}>
              {u ? (
                <span class={`tx-board-unit ${u.side === "Party" ? "party" : u.side === "Enemy" ? "enemy" : "neutral"} ${u.life.toLowerCase()}`}
                  title={`${u.name} ${u.hp}/${u.maxHp}`}>
                  {u.name.slice(0, 2)}
                  <i style={{ width: `${Math.round((100 * u.hp) / Math.max(1, u.maxHp))}%` }} />
                </span>
              ) : null}
            </div>
          );
        })}
      </div>
      <aside class="tx-board-side">
        <ol class="tx-board-order">
          {battle.order.map((id) => <li key={id} class={id === battle.active ? "active" : ""}>{name(id)}</li>)}
        </ol>
        <ul class="tx-board-log">
          {lines.map((l, i) => <li key={`${l.seq}-${i}`} class={l.seq === latest ? "new" : ""}>{l.text}</li>)}
        </ul>
      </aside>
    </div>
  );
}
