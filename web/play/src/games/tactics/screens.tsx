import { useEffect, useMemo, useState } from "preact/hooks";
import type { CustomScreenProps } from "../module";
import { conditionName } from "./conditions";
import { tileStyle, useMap, type MapData } from "./map";
import type {
  AbilityChoice, ClassOption, Cost, EncounterOption, ReactData, ResultsData, StatusData, Tile, TargetChoice, TurnData, UnitView,
} from "./types";

const same = (a: Tile, b: Tile) => a[0] === b[0] && a[1] === b[1];

/** Where an ability sorts on the turn screen: Attack, the class's own, the common actions, then items. */
const rank = (a: AbilityChoice) => (a.item ? 3 : a.ability === "common.attack" ? 0 : a.ability.startsWith("common.") ? 2 : 1);

function Hp({ unit }: { unit: UnitView }) {
  const pct = unit.maxHp > 0 ? Math.round((100 * unit.hp) / unit.maxHp) : 0;
  return (
    <div class="tx-hp" aria-label={`HP ${unit.hp} of ${unit.maxHp}`}>
      <div class="tx-hp-bar"><i style={{ width: `${pct}%` }} /></div>
      <span>{unit.hp}/{unit.maxHp}{unit.tempHp > 0 ? ` +${unit.tempHp}` : ""}</span>
      {unit.life === "Downed" ? <b class="tx-downed">Downed · {unit.countdown}</b> : null}
    </div>
  );
}

/** "1 Action · 2 Mana": the parts of a cost worth showing. */
export function costText(cost: Cost): string {
  const parts: string[] = [];
  if (cost.action) parts.push("Action");
  if (cost.bonus) parts.push("Bonus");
  if (cost.move) parts.push(`${cost.move} Move`);
  for (const [name, n] of Object.entries(cost.resource ?? {})) parts.push(`${n} ${name[0]!.toUpperCase()}${name.slice(1)}`);
  if (cost.concentration) parts.push("Concentration");
  return parts.length > 0 ? parts.join(" · ") : "Free";
}

// ---- Muster, encounter pick, results -------------------------------------------------------------------------------

export function PickClass({ data, send }: CustomScreenProps) {
  const { classes } = data as { classes: ClassOption[] };
  return (
    <div class="tx tx-pick">
      <h2>Choose your class</h2>
      <div class="tx-grid">
        {classes.map((c) => (
          <button type="button" key={c.id} class="tx-class" onClick={() => send("pickClass", { class: c.id })}>
            <b>{c.name}</b>
            <small>{c.role}</small>
          </button>
        ))}
      </div>
    </div>
  );
}

export function PickEncounter({ data, send }: CustomScreenProps) {
  const { encounters } = data as { encounters: EncounterOption[] };
  return (
    <div class="tx tx-pick">
      <h2>Choose a battle</h2>
      {encounters.map((e) => (
        <button type="button" key={e.id} class="tx-row" disabled={!e.fits} onClick={() => send("pickEncounter", { encounter: e.id })}>
          <b>{e.name}</b>
          <small>{e.level ? `Lv ${e.level} · ` : ""}{e.min}–{e.max} heroes</small>
        </button>
      ))}
    </div>
  );
}

export function Results({ data, send }: CustomScreenProps) {
  const r = data as ResultsData;
  return (
    <div class="tx tx-results">
      <h2 class={r.outcome === "Won" ? "tx-won" : "tx-lost"}>{r.outcome === "Won" ? "Victory" : "Defeat"}</h2>
      <p class="tx-dim">{r.encounter} · party level {r.level}</p>
      <ul class="tx-lines">
        {r.lines.map((l) => (
          <li key={l.name}>
            <b>{l.name}</b> <span>+{l.xp} XP</span>
            {l.injuries.length > 0 ? <small class="tx-injury">{l.injuries.join(", ")}</small> : null}
          </li>
        ))}
      </ul>
      {r.vip ? (
        <div class="tx-actions">
          <button type="button" class="big" onClick={() => send("next")}>Next battle</button>
          <button type="button" onClick={() => send("end")}>End session</button>
        </div>
      ) : <p class="tx-dim">Waiting for the host…</p>}
    </div>
  );
}

// ---- Someone else's turn, and reactions ------------------------------------------------------------------------------

export function Status({ data }: CustomScreenProps) {
  const s = data as StatusData;
  return (
    <div class="tx tx-status">
      <p class="tx-big">{s.active ? `${s.active} is acting` : "Waiting…"}</p>
      {s.upIn ? <p class="tx-dim">You're up in {s.upIn}</p> : null}
      {s.aiPlaying ? <p class="tx-dim">The AI is playing your character</p> : null}
      <div class="tx-me"><b>{s.me.name}</b><Hp unit={s.me} /></div>
      {s.me.conditions.length > 0 ? <p class="tx-dim">{s.me.conditions.map(conditionName).join(", ")}</p> : null}
    </div>
  );
}

const TRIGGERS: Record<string, string> = {
  leavesReach: "A foe is leaving your reach",
  beforeHit: "You're about to be hit",
  spellCast: "A foe is casting a spell",
};

export function ReactPrompt({ data, send }: CustomScreenProps) {
  const r = data as ReactData;
  return (
    <div class="tx tx-react">
      <p class="tx-big">{TRIGGERS[r.trigger] ?? "You can react"}</p>
      <div class="tx-actions">
        {r.options.map((o) => (
          <button type="button" key={o.id} class={o.id === "pass" ? "" : "big"} onClick={() => send("react", { choice: o.id })}>
            {o.label}
          </button>
        ))}
      </div>
    </div>
  );
}

// ---- Your turn ---------------------------------------------------------------------------------------------------------

/**
 * Your turn: a map around you with a cursor, the abilities you can use, Move here / Use, Delay and End turn. Browsing is
 * local (the view stays the same, so nothing is sent until you commit), and every option comes from the server's
 * `Choices.For`, so anything you can pick, the game accepts.
 */
export function Turn({ data, send }: CustomScreenProps) {
  const { me, units, choices, map: mapId } = data as TurnData;
  const map = useMap(mapId);
  const [abilityIx, setAbilityIx] = useState<number | null>(null);
  const [cursor, setCursor] = useState<Tile>(me.at);
  useEffect(() => {
    setAbilityIx(null);
    setCursor(me.at);
  }, [data]);

  const ability: AbilityChoice | null = abilityIx === null ? null : choices.abilities[abilityIx] ?? null;
  const names = useMemo(() => new Map(units.map((u) => [u.id, u.name])), [units]);
  const move = ability ? undefined : choices.moves.find((m) => same(m.to, cursor));
  // Every legal target: the ones with previews, then the bare tiles, which are only picked on the map.
  const all: TargetChoice[] = useMemo(() => ability
    ? [...ability.targets, ...ability.tiles.map((t): TargetChoice => ({ target: { unit: null, tile: t }, aim: t, affected: [] }))]
    : [], [ability]);
  const target: TargetChoice | undefined = all.find((t) => same(t.aim, cursor)) ?? (all.length === 1 ? all[0] : undefined);

  const use = (a: AbilityChoice, t: TargetChoice) =>
    send("use", { ability: a.ability, item: a.item ?? undefined, unit: t.target.unit ?? undefined, tile: t.target.tile ?? undefined });
  const preview = (t: TargetChoice) => t.affected.map((p) => {
    const bits = [p.damage > 0 ? `−${p.damage}` : "", p.heal > 0 ? `+${p.heal}` : ""].filter(Boolean).join(" ");
    return `${names.get(p.unit) ?? p.unit}${bits ? ` ${bits}` : ""}`;
  }).join(", ");

  const e = choices.economy;
  return (
    <div class="tx tx-turn">
      <div class="tx-me">
        <b>{me.name}</b>
        <Hp unit={me} />
        <span class="tx-pips" aria-label="Turn economy">
          <i class={e.action || e.extraActions > 0 ? "on" : ""}>A</i>
          <i class={e.bonus ? "on" : ""}>B</i>
          <i class={e.moveLeft > 0 ? "on" : ""}>{e.moveLeft}</i>
        </span>
      </div>

      {/* The commit buttons come first, so they're on screen without scrolling past the map. */}
      {ability ? (
        <div class="tx-actions tx-commit">
          <button type="button" class="big" disabled={!target} onClick={() => target && use(ability, target)}>
            {target ? `Use${target.target.unit && target.target.unit !== me.id ? ` on ${names.get(target.target.unit) ?? ""}` : ""}` : "Pick a target"}
          </button>
          <button type="button" onClick={() => setAbilityIx(null)}>Back</button>
        </div>
      ) : (
        <div class="tx-actions tx-commit">
          <button type="button" class="big" disabled={!move} onClick={() => move && send("move", { path: move.path })}>
            {move ? `Move here · ${move.cost}` : "Pick a tile to move"}
          </button>
          {/* Delay stays when it's spent, so End turn doesn't jump into its place under a thumb. */}
          <button type="button" disabled={!choices.canDelay} onClick={() => send("delay", { slots: 1 })}>Delay</button>
          <button type="button" onClick={() => send("endTurn")}>End turn</button>
        </div>
      )}

      <MiniMap me={me} units={units} cursor={cursor} onPick={setCursor} map={map}
        reach={ability ? [] : choices.moves.map((m) => m.to)} aims={all.map((t) => t.aim)} />
      <DPad cursor={cursor} onMove={setCursor} />

      {ability ? (
        <div class="tx-panel">
          <p><b>{ability.name}</b> <small class="tx-dim">{costText(ability.cost)}</small></p>
          <p class="tx-tip">{ability.tooltip}</p>
          {ability.targets.length > 1 ? (
            <ul class="tx-targets">
              {ability.targets.map((t, i) => (
                <li key={i}>
                  <button type="button" class={target === t ? "on" : ""} onClick={() => setCursor(t.aim)}>
                    {t.target.unit ? names.get(t.target.unit) ?? t.target.unit
                      : t.affected.length > 0 ? `${t.affected.length} target${t.affected.length === 1 ? "" : "s"}`
                      : `(${t.aim[0]}, ${t.aim[1]})`}
                    <small>{preview(t)}</small>
                  </button>
                </li>
              ))}
            </ul>
          ) : null}
        </div>
      ) : (
        <ul class="tx-abilities">
          {choices.abilities.map((a, i) => ({ a, i })).sort((x, y) => rank(x.a) - rank(y.a)).map(({ a, i }) => (
            <li key={`${a.ability}/${a.item ?? ""}`}>
              <button type="button" onClick={() => { setAbilityIx(i); window.scrollTo(0, 0); }}>
                <b>{a.name}</b> <small class="tx-dim">{costText(a.cost)}</small>
                <small class="tx-tip">{a.tooltip}</small>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/**
 * The battlefield around you: the whole map with its terrain once it has loaded from the content pack, else (and in
 * tests) just the box around the units, your moves and the aims. Tap a tile to put the cursor there.
 */
function MiniMap({ me, units, cursor, reach, aims, onPick, map }: {
  me: UnitView; units: UnitView[]; cursor: Tile; reach: Tile[]; aims: Tile[]; onPick(t: Tile): void; map: MapData | null;
}) {
  const tiles = [me.at, cursor, ...units.map((u) => u.at), ...reach, ...aims];
  const [x0, x1] = map ? [0, map.w - 1] : [Math.max(0, Math.min(...tiles.map((t) => t[0])) - 1), Math.max(...tiles.map((t) => t[0])) + 1];
  const [y0, y1] = map ? [0, map.h - 1] : [Math.max(0, Math.min(...tiles.map((t) => t[1])) - 1), Math.max(...tiles.map((t) => t[1])) + 1];
  const at = (t: Tile) => units.find((u) => same(u.at, t));
  const rows = [];
  for (let y = y0; y <= y1; y++) {
    const cells = [];
    for (let x = x0; x <= x1; x++) {
      const t: Tile = [x, y];
      const u = at(t);
      const cls = ["tx-cell",
        u ? (u.id === me.id ? "me" : u.side === me.side ? "ally" : "foe") : "",
        reach.some((r) => same(r, t)) ? "reach" : "",
        aims.some((a) => same(a, t)) ? "aim" : "",
        same(cursor, t) ? "cursor" : ""].filter(Boolean).join(" ");
      cells.push(
        <button type="button" key={x} class={cls} aria-label={u ? u.name : `${x}, ${y}`} onClick={() => onPick(t)}
          style={map && !u ? tileStyle(map, y * map.w + x) : undefined}>
          {u ? u.name[0] : ""}
        </button>,
      );
    }
    rows.push(<div class="tx-maprow" key={y}>{cells}</div>);
  }
  return <div class={`tx-map${map ? " terrain" : ""}`} role="grid">{rows}</div>;
}

function DPad({ cursor, onMove }: { cursor: Tile; onMove(t: Tile): void }) {
  const go = (dx: number, dy: number) => onMove([Math.max(0, cursor[0] + dx), Math.max(0, cursor[1] + dy)]);
  return (
    <div class="tx-dpad">
      <button type="button" aria-label="Left" onClick={() => go(-1, 0)}>◀</button>
      <button type="button" aria-label="Up" onClick={() => go(0, -1)}>▲</button>
      <button type="button" aria-label="Down" onClick={() => go(0, 1)}>▼</button>
      <button type="button" aria-label="Right" onClick={() => go(1, 0)}>▶</button>
    </div>
  );
}
