import { SurfaceScene } from "./surfaces";
import { useEffect, useMemo, useState } from "preact/hooks";
import type { CustomScreenProps } from "../module";
import { AbilityIcon, Portrait, abilityClass, useIcons } from "./art";
import { tileStyle, useMap, type MapData } from "./map";
import { FallBack } from "./fallback";
import { ReactionsSheet } from "./reactions";
import { classFrom, remembered, rememberTurn } from "./memory";
import { useAlertOnMount, useSfx } from "./sfx";
import type { AbilityChoice, Cost, TargetChoice, Tile, TurnData, UnitView } from "./types";
import { Conditions, EconomyChips, HpBar, HpText, LineIcon, MeCard, Section, TurnBanner } from "./ui";

const same = (a: Tile, b: Tile) => a[0] === b[0] && a[1] === b[1];

/** Where an ability sorts in its group: Attack, the class's own, the common actions, then items. */
const rank = (a: AbilityChoice) => (a.item ? 3 : a.ability === "common.attack" ? 0 : a.ability.startsWith("common.") ? 2 : 1);

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

/** The ability groups of the turn screen, by what they spend, as in the mockup: actions, then bonus actions, then the rest. */
const GROUPS: { title: string; has(c: Cost): boolean }[] = [
  { title: "Actions", has: (c) => !!c.action },
  { title: "Bonus actions", has: (c) => !c.action && !!c.bonus },
  { title: "Free", has: (c) => !c.action && !c.bonus },
];

/**
 * Your turn: who you are and what you have left, the battlefield with a cursor, your abilities, and Move here / Delay /
 * End turn in a footer that stays on screen. Picking an ability turns the screen into aiming: the target, what it does
 * to whom, and Use / Back. Browsing is local (the view stays the same, so nothing is sent until you commit), and every
 * option comes from the server's `Choices.For`, so anything you can pick, the game accepts.
 */
export function Turn({ data, send }: CustomScreenProps) {
  const { me, units, choices, map: mapId, summoned, fallBack } = data as TurnData;
  const map = useMap(mapId);
  const icons = useIcons();
  const sfx = useSfx();
  useAlertOnMount("yourTurn", `turn ${me.id}`);
  const [abilityIx, setAbilityIx] = useState<number | null>(null);
  const [cursor, setCursor] = useState<Tile>(me.at);
  // A multi-attack's earlier attacks, aimed already: the one being aimed is the next (#107).
  const [picks, setPicks] = useState<Pick[]>([]);
  // A new view resets the selection, but the same view sent again (a reconnect, a refresh) keeps what you were doing.
  // A vote to fall back changes the view for everyone, which mustn't undo what this player is aiming.
  const content = useMemo(() => JSON.stringify({ ...(data as TurnData), fallBack: undefined, reactions: undefined }), [data]);
  useEffect(() => {
    setAbilityIx(null);
    setCursor(me.at);
    setPicks([]);
  }, [content]);

  const cls = useMemo(() => classFrom(choices.abilities, (id) => abilityClass(icons, id)) ?? remembered().classes[me.id],
    [icons, content]);
  useEffect(() => rememberTurn(me, units, choices.abilities, cls), [content, cls]);

  const ability: AbilityChoice | null = abilityIx === null ? null : choices.abilities[abilityIx] ?? null;
  const byId = useMemo(() => new Map(units.map((u) => [u.id, u])), [units]);
  const move = ability ? undefined : choices.moves.find((m) => same(m.to, cursor));
  // Every legal target: the ones with previews, then the bare tiles, which are only picked on the map.
  const all: TargetChoice[] = useMemo(() => ability
    ? [...ability.targets, ...ability.tiles.map((t): TargetChoice => ({ target: { unit: null, tile: t }, aim: t, affected: [] }))]
    : [], [ability]);
  const target: TargetChoice | undefined = all.find((t) => same(t.aim, cursor)) ?? (all.length === 1 ? all[0] : undefined);

  const pick = (i: number) => {
    sfx("select");
    setAbilityIx(i);
    setPicks([]);
    window.scrollTo(0, 0);
  };
  const back = () => {
    sfx("cancel");
    const last = picks[picks.length - 1];
    if (last) {
      // Back to aiming the attack before, where it was aimed.
      setPicks(picks.slice(0, -1));
      setCursor(last.aim);
      return;
    }
    setAbilityIx(null);
  };
  const targetUnit = target?.target.unit && target.target.unit !== me.id ? byId.get(target.target.unit) : undefined;
  // Each attack's target: the ones aimed, then the one being aimed for the rest (each defaults to the one before).
  const attacks = ability?.attacks ?? 1;
  const plan = attacks > 1 && targetUnit
    ? [...picks.map((p) => p.unit), ...Array<string>(attacks - picks.length).fill(targetUnit.id)]
    : null;
  const spread = plan !== null && plan.some((u) => u !== plan[0]);
  const next = () => {
    if (!targetUnit || !target) return;
    sfx("select");
    setPicks([...picks, { unit: targetUnit.id, aim: target.aim }]);   // the cursor stays: the next attack starts here
  };
  const use = (a: AbilityChoice, t: TargetChoice) => {
    sfx(t.target.unit && t.target.unit !== me.id ? "dice" : "confirm");
    send("use", spread && plan
      ? { ability: a.ability, item: a.item ?? undefined, unit: plan[0], targets: plan }
      : { ability: a.ability, item: a.item ?? undefined, unit: t.target.unit ?? undefined, tile: t.target.tile ?? undefined });
  };

  return (
    <div class={`tx tx-controls-layout tx-turn${ability ? " aiming" : ""}`}><div class="tx-controls-scroll">
      <SurfaceScene kinds={(data as TurnData).surfaces} />
      {ability ? (
        <MeCard me={me} title={ability.name} subtitle={target ? "Ready: check the TV, then use it" : aimHint(ability)}
          icon={<span class="tx-me-icon"><AbilityIcon ability={ability.ability} item={ability.item} /></span>} />
      ) : <MeCard me={me} cls={cls} />}
      <TurnBanner summon={summoned ? me.name : undefined} />
      {ability ? null : <FallBack fallBack={fallBack} send={send} />}
      {ability ? null : <ReactionsSheet reactions={(data as TurnData).reactions} send={send} />}

      {ability ? (
        <Aim ability={ability} target={target} unit={targetUnit} byId={byId} onPick={(t) => { sfx("select"); setCursor(t.aim); }}
          strikes={plan ? { plan, at: picks.length, onNext: next } : undefined} />
      ) : (
        <>
          <EconomyChips e={choices.economy} />
          <Conditions ids={me.conditions} />
        </>
      )}

      <div class="tx-field">
        <MiniMap me={me} units={units} cursor={cursor} onPick={setCursor} map={map}
          reach={ability ? [] : choices.moves.map((m) => m.to)} risks={ability ? [] : choices.moves.filter(m => m.warn || (m.risk ?? 0) > 0).map(m => m.to)}
          spread={target?.spread ?? []} aims={all.map((t) => t.aim)} />
        <DPad cursor={cursor} onMove={setCursor} />
        {(target?.spread?.length ?? 0) > 0 ? <span class="tx-spread-key">Dashed blue: reaction spread · includes allies</span> : null}
      </div>

      {ability ? null : <AbilityList abilities={choices.abilities} cls={cls} onPick={pick} />}

      </div>
      {/* Actions have their own row, outside the scrolling map and ability list. */}
      {ability ? (
        <div class="tx-foot tx-commit aim">
          <button type="button" class="tx-btn ghost" onClick={back}>Back</button>
          <button type="button" class="tx-btn gold" disabled={!target} onClick={() => target && use(ability, target)}>
            {target ? spread ? `Use · ${attacks} attacks` : `Use${targetUnit ? ` on ${targetUnit.name}` : ""}`
              : ability.aim === "direction" ? "Pick a direction" : ability.aim === "tile" ? "Pick a tile" : "Pick a target"}
          </button>
        </div>
      ) : (
        <div class="tx-foot tx-commit">
          <button type="button" class={`tx-btn${move ? " gold" : ""}`} disabled={!move}
            onClick={() => { if (move) { sfx("confirm"); send("move", { path: move.path }); } }}>
            {move ? `Move here · ${move.cost}${move.warn ? ` · ${move.warn}` : ""}` : "Pick a tile to move"}
          </button>
          {/* Delay stays when it's spent, so End turn doesn't jump into its place under a thumb. */}
          <button type="button" class="tx-btn" disabled={!choices.canDelay} onClick={() => send("delay", { slots: 1 })}>
            <LineIcon name="swap" />Delay
          </button>
          <button type="button" class="tx-btn" onClick={() => send("endTurn")}><LineIcon name="hourglass" />End turn</button>
        </div>
      )}
    </div>
  );
}

/** An attack of a multi-attack aimed already: at whom, and the tile the cursor was on. */
interface Pick { unit: string; aim: Tile }

const aimHint = (a: AbilityChoice) =>
  a.aim === "direction" ? "Pick a direction on the map" : a.aim === "tile" ? "Pick a tile on the map" : "Pick a target";

/** The ability list, grouped by what each ability spends, with its icon, what it does and its tags. */
function AbilityList({ abilities, cls, onPick }: { abilities: AbilityChoice[]; cls?: string; onPick(i: number): void }) {
  const indexed = abilities.map((a, i) => ({ a, i }));
  return (
    <>
      {GROUPS.map((g) => {
        const rows = indexed.filter(({ a }) => g.has(a.cost)).sort((x, y) => rank(x.a) - rank(y.a));
        if (rows.length === 0) return null;
        return (
          <section class="tx-group" key={g.title}>
            <Section>{g.title}</Section>
            <ul class="tx-abilities">
              {rows.map(({ a, i }) => (
                <li key={`${a.ability}/${a.item ?? ""}`}>
                  <button type="button" class="tx-ability" onClick={() => onPick(i)}>
                    <span class="tx-ic"><AbilityIcon ability={a.ability} item={a.item} /></span>
                    <span class="tx-ab-text">
                      <b>{a.name}</b>
                      <span class="tx-tip">{a.tooltip}</span>
                    </span>
                    <Tags a={a} cls={cls} />
                  </button>
                </li>
              ))}
            </ul>
          </section>
        );
      })}
    </>
  );
}

/** An ability's tags: its subclass (the id's prefix, when it isn't the class's own), its resource cost, concentration. */
function Tags({ a, cls }: { a: AbilityChoice; cls?: string }) {
  const prefix = a.ability.split(".")[0]!;
  const tags: string[] = [];
  if (cls && !a.item && prefix !== cls && prefix !== "common" && prefix !== "race") tags.push(prefix.replaceAll("_", " "));
  for (const [name, n] of Object.entries(a.cost.resource ?? {})) tags.push(`${n} ${name}`);
  if (a.cost.concentration) tags.push("conc.");
  if (a.item) tags.push("item");
  if (tags.length === 0) return null;
  return <span class="tx-tags">{tags.map((t) => <span class="tx-tag" key={t}>{t}</span>)}</span>;
}

/**
 * Aiming: the target (portrait, HP), what the ability does to each unit it touches, and, when there's more than one,
 * the targets to step through or tap. For a multi-attack (`strikes`), which attack is being aimed, each attack's target so
 * far, and the button to aim the next one ("Attack 2 of 3"), which starts on the same target.
 */
function Aim({ ability, target, unit, byId, onPick, strikes }: {
  ability: AbilityChoice; target?: TargetChoice; unit?: UnitView; byId: Map<string, UnitView>; onPick(t: TargetChoice): void;
  strikes?: { plan: string[]; at: number; onNext(): void };
}) {
  const targets = ability.targets;
  const at = target ? targets.indexOf(target) : -1;
  const step = (d: number) => {
    if (targets.length === 0) return;
    onPick(targets[(Math.max(at, d > 0 ? -1 : 0) + d + targets.length) % targets.length]!);
  };
  const label = (t: TargetChoice) => (t.target.unit ? byId.get(t.target.unit)?.name ?? "You"
    : t.affected.length > 0 ? `${t.affected.length} target${t.affected.length === 1 ? "" : "s"}` : `(${t.aim[0]}, ${t.aim[1]})`);
  return (
    <div class="tx-aim">
      {unit ? (
        <div class={`tx-target ${unit.side === "Enemy" ? "enemy" : "ally"}`}>
          <Portrait unit={unit} size={60} />
          <div class="tx-target-body">
            <b>{unit.name}</b>
            <HpBar unit={unit} enemy={unit.side === "Enemy"} />
            <small class="tx-target-hp"><HpText unit={unit} /> HP</small>
          </div>
        </div>
      ) : null}
      <p class="tx-aim-what"><span class="tx-cost">{costText(ability.cost)}</span> {ability.tooltip}</p>
      {strikes ? (
        <div class="tx-strikes">
          <div class="tx-strike-step">
            <span>{`Aiming attack ${strikes.at + 1} of ${strikes.plan.length}`}</span>
            {strikes.at + 1 < strikes.plan.length ? (
              <button type="button" class="tx-btn" onClick={strikes.onNext}>
                {`Attack ${strikes.at + 2} of ${strikes.plan.length}`}<LineIcon name="next" />
              </button>
            ) : null}
          </div>
          <ol class="tx-strike-plan" aria-label="Each attack's target">
            {strikes.plan.map((id, i) => (
              <li key={i} class={i === strikes.at ? "on" : i > strikes.at ? "later" : ""}>{byId.get(id)?.name ?? id}</li>
            ))}
          </ol>
        </div>
      ) : null}
      {target && target.affected.length > 0 ? (
        <div class="tx-chips">
          {target.affected.map((p) => {
            const u = byId.get(p.unit);
            // Previews are expected values (hit chance included): whole numbers read better.
            const bits = [p.damage > 0 ? `−${Math.max(1, Math.round(p.damage))}` : "", p.heal > 0 ? `+${Math.max(1, Math.round(p.heal))}` : ""].filter(Boolean).join(" ");
            return (
              <span key={p.unit} class={`tx-chip ${p.damage > 0 ? "bad" : p.heal > 0 ? "good" : ""}`}>
                {u?.name ?? "You"}{bits ? ` ${bits}` : ""}
              </span>
            );
          })}
        </div>
      ) : null}
      {targets.length > 1 ? (
        <>
          <div class="tx-cycle">
            <button type="button" class="tx-btn" aria-label="Previous target" onClick={() => step(-1)}><LineIcon name="back" /></button>
            <span>{at >= 0 ? `Target ${at + 1} of ${targets.length}` : `${targets.length} targets in reach`}</span>
            <button type="button" class="tx-btn" aria-label="Next target" onClick={() => step(1)}><LineIcon name="next" /></button>
          </div>
          <ul class="tx-targets">
            {targets.map((t, i) => (
              <li key={i}>
                <button type="button" class={target === t ? "on" : ""} onClick={() => onPick(t)}>{label(t)}</button>
              </li>
            ))}
          </ul>
        </>
      ) : null}
    </div>
  );
}

/**
 * The battlefield: the whole map with its terrain once it has loaded from the content pack, else (and in tests) just
 * the box around the units, your moves and the aims. Tap a tile to put the cursor there.
 */
function MiniMap({ me, units, cursor, reach, risks, spread, aims, onPick, map }: {
  me: UnitView; units: UnitView[]; cursor: Tile; reach: Tile[]; risks: Tile[]; spread: Tile[]; aims: Tile[]; onPick(t: Tile): void; map: MapData | null;
}) {
  const tiles = [me.at, cursor, ...units.map((u) => u.at), ...reach, ...aims, ...spread];
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
        risks.some(r => same(r, t)) ? "risk" : "",
        spread.some(r => same(r, t)) ? "spread" : "",
        same(cursor, t) ? "cursor" : ""].filter(Boolean).join(" ");
      cells.push(
        <button type="button" key={x} class={cls} aria-label={u ? u.name : `${x}, ${y}`} aria-description={spread.some(r => same(r, t)) ? "Within reaction spread" : risks.some(r => same(r, t)) ? "Hazardous movement" : undefined} onClick={() => onPick(t)}
          style={map && !u ? tileStyle(map, y * map.w + x) : undefined}>
          {u ? u.name[0] : ""}
        </button>,
      );
    }
    rows.push(<div class="tx-maprow" key={y}>{cells}</div>);
  }
  return <div class={`tx-map${map ? " terrain" : ""}`} role="grid" aria-label="Battlefield">{rows}</div>;
}

/** Moves the cursor a tile at a time: a cross of big buttons beside the map, for when a tile is too small to tap. */
function DPad({ cursor, onMove }: { cursor: Tile; onMove(t: Tile): void }) {
  const go = (dx: number, dy: number) => onMove([Math.max(0, cursor[0] + dx), Math.max(0, cursor[1] + dy)]);
  return (
    <div class="tx-dpad">
      <button type="button" class="up" aria-label="Up" onClick={() => go(0, -1)}><LineIcon name="up" /></button>
      <button type="button" class="left" aria-label="Left" onClick={() => go(-1, 0)}><LineIcon name="up" rotate={-90} /></button>
      <span class="hub" aria-hidden="true">Cursor</span>
      <button type="button" class="right" aria-label="Right" onClick={() => go(1, 0)}><LineIcon name="up" rotate={90} /></button>
      <button type="button" class="down" aria-label="Down" onClick={() => go(0, 1)}><LineIcon name="up" rotate={180} /></button>
    </div>
  );
}
