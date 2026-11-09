import { SurfaceScene } from "./surfaces";
import { useEffect, useMemo, useState } from "preact/hooks";
import type { CustomScreenProps } from "../module";
import { AbilityIcon, Portrait, abilityClass, useIcons } from "./art";
import { useMap } from "./map";
import { BattleMap } from "./battleMap";
import { FallBack } from "./fallback";
import { ReactionsSheet } from "./reactions";
import { classFrom, remembered, rememberTurn } from "./memory";
import { useAlertOnMount, useSfx } from "./sfx";
import type { AbilityChoice, Cost, TargetChoice, Tile, TurnData, UnitView } from "./types";
import { ControlNotice, Conditions, EconomyChips, HpBar, HpText, LineIcon, MeCard, Section, TurnBanner } from "./ui";

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
  const { me, units, choices, map: mapId, terrain, summoned, fallBack } = data as TurnData;
  const loadedMap = useMap(terrain ? null : mapId);
  const map = terrain ?? loadedMap;
  const icons = useIcons();
  const sfx = useSfx();
  useAlertOnMount("yourTurn", `turn ${me.id}`);
  const [abilityIx, setAbilityIx] = useState<number | null>(null);
  const [cursor, setCursor] = useState<Tile>(me.at);
  const [aimedUnit, setAimedUnit] = useState<string | null>(null);
  // A multi-attack's earlier attacks, aimed already: the one being aimed is the next (#107).
  const [picks, setPicks] = useState<Pick[]>([]);
  const [endTurnUntil, setEndTurnUntil] = useState<number | null>(null);
  // A new view resets the selection, but the same view sent again (a reconnect, a refresh) keeps what you were doing.
  // A vote to fall back changes the view for everyone, which mustn't undo what this player is aiming.
  const content = useMemo(() => JSON.stringify({ ...(data as TurnData), fallBack: undefined, reactions: undefined, controlNotice: undefined }), [data]);
  useEffect(() => {
    setAbilityIx(null);
    setCursor(me.at);
    setAimedUnit(null);
    setPicks([]);
    setEndTurnUntil(null);
  }, [content]);
  useEffect(() => {
    if (endTurnUntil === null) return;
    const timer = window.setTimeout(() => setEndTurnUntil(null), Math.max(0, endTurnUntil - Date.now()));
    return () => window.clearTimeout(timer);
  }, [endTurnUntil]);

  // Choices already exclude abilities with no legal target or insufficient resources.
  const unused = [
    (choices.economy.action || choices.economy.extraActions > 0) && choices.abilities.some(a =>
      (a.cost.action ?? 0) > 0 && (a.targets.length > 0 || a.tiles.length > 0)) ? "Action" : null,
    choices.economy.bonus && choices.abilities.some(a =>
      (a.cost.bonus ?? 0) > 0 && (a.targets.length > 0 || a.tiles.length > 0)) ? "Bonus" : null,
  ].filter(Boolean).join(" & ");
  const endTurn = () => {
    if (unused && (endTurnUntil === null || Date.now() >= endTurnUntil)) {
      sfx("select");
      setEndTurnUntil(Date.now() + 3000);
      return;
    }
    setEndTurnUntil(null);
    send("endTurn");
  };
  const browse = (at: Tile) => { setEndTurnUntil(null); setAimedUnit(null); setCursor(at); };

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
  const target: TargetChoice | undefined = all.find(t => same(t.aim, cursor) && (aimedUnit === null || t.target.unit === aimedUnit));
  const unitLabel = (unit: UnitView) => units.some(other => other.id !== unit.id && other.name === unit.name)
    ? `#${units.indexOf(unit) + 1} ${unit.name}` : unit.name;

  const pick = (i: number) => {
    sfx("select");
    setEndTurnUntil(null);
    setAbilityIx(i);
    setPicks([]);
    const chosen = choices.abilities[i]!;
    const aims = [...chosen.targets.map(t => t.aim), ...chosen.tiles];
    if (aims.length === 1) {
      setCursor(aims[0]!);
      setAimedUnit(chosen.targets[0]?.target.unit ?? null);
    } else if (!chosen.targets.some(t => t.target.unit === aimedUnit)) setAimedUnit(null);
    window.scrollTo(0, 0);
  };
  const back = () => {
    sfx("cancel");
    const last = picks[picks.length - 1];
    if (last) {
      // Back to aiming the attack before, where it was aimed.
      setPicks(picks.slice(0, -1));
      setCursor(last.aim);
      setAimedUnit(last.unit);
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
      <ControlNotice notice={(data as TurnData).controlNotice} />
      {ability ? (
        <MeCard me={me} title={ability.name} subtitle={target ? "Ready: tap Use" : aimHint(ability)}
          icon={<span class="tx-me-icon"><AbilityIcon ability={ability.ability} item={ability.item} /></span>} />
      ) : <MeCard me={me} cls={cls} />}
      <TurnBanner summon={summoned ? me.name : undefined} />
      {ability ? null : <FallBack fallBack={fallBack} send={send} />}
      {ability ? null : <ReactionsSheet reactions={(data as TurnData).reactions} send={send} />}

      {ability ? (
        <Aim ability={ability} target={target} unit={targetUnit} byId={byId} unitLabel={unitLabel} onPick={(t) => { sfx("select"); setCursor(t.aim); setAimedUnit(t.target.unit); }}
          strikes={plan ? { plan, at: picks.length, onNext: next } : undefined} />
      ) : (
        <>
          <EconomyChips e={choices.economy} />
          <Conditions ids={me.conditions} />
        </>
      )}

      <BattleMap me={me} units={units} cursor={cursor} onPick={browse} map={map} zones={terrain?.zones ?? []}
        reach={ability ? [] : choices.moves.map(m => m.to)}
        risks={ability ? [] : choices.moves.filter(m => m.warn || (m.risk ?? 0) > 0).map(m => m.to)}
        warning={move?.warn} spread={target?.spread ?? []} aims={all.map(t => t.aim)}
        onUnit={unit => {
          const aim = all.find(t => t.target.unit === unit.id) ?? all.find(t => same(t.aim, unit.at))
            ?? all.find(t => t.affected.some(p => p.unit === unit.id));
          browse(aim?.aim ?? unit.at);
          setAimedUnit(aim ? aim.target.unit : unit.id);
        }} />

      {ability ? null : <AbilityList abilities={choices.abilities} cls={cls} onPick={pick} />}

      </div>
      {/* Actions have their own row, outside the scrolling map and ability list. */}
      {ability ? (
        <div class="tx-foot tx-commit aim">
          <button type="button" class="tx-btn ghost" onClick={back}>Back</button>
          <button type="button" class="tx-btn gold" disabled={!target} onClick={() => target && use(ability, target)}>
            {target ? spread ? `Use · ${attacks} attacks` : `Use${targetUnit ? ` on ${unitLabel(targetUnit)}` : ""}`
              : ability.aim === "direction" ? "Pick a direction" : ability.aim === "tile" ? "Pick a tile" : "Pick a target"}
          </button>
        </div>
      ) : (
        <div class="tx-foot tx-commit">
          <button type="button" class={`tx-btn${move ? " gold" : ""}`} disabled={!move}
            onClick={() => { if (move) { setEndTurnUntil(null); sfx("confirm"); send("move", { path: move.path }); } }}>
            {move ? `Move here · ${move.cost}${move.warn ? ` · ${move.warn}` : ""}` : "Pick a tile to move"}
          </button>
          {/* Delay stays when it's spent, so End turn doesn't jump into its place under a thumb. */}
          <button type="button" class="tx-btn" disabled={!choices.canDelay} onClick={() => { setEndTurnUntil(null); send("delay", { slots: 1 }); }}>
            <LineIcon name="swap" />Delay
          </button>
          <button type="button" class={`tx-btn tx-end-turn${endTurnUntil !== null ? " gold" : ""}`} onClick={endTurn}>
            <LineIcon name="hourglass" /><span aria-live="polite">{endTurnUntil !== null ? `End turn? ${unused} unused` : "End turn"}</span>
          </button>
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
  if (a.cost.concentration) tags.push("Concentration");
  if (a.item) tags.push("item");
  if (tags.length === 0) return null;
  return <span class="tx-tags">{tags.map((t) => <span class="tx-tag" key={t}>{t}</span>)}</span>;
}

/**
 * Aiming: the target (portrait, HP), what the ability does to each unit it touches, and, when there's more than one,
 * the targets to step through or tap. For a multi-attack (`strikes`), which attack is being aimed, each attack's target so
 * far, and the button to aim the next one ("Attack 2 of 3"), which starts on the same target.
 */
function Aim({ ability, target, unit, byId, unitLabel, onPick, strikes }: {
  ability: AbilityChoice; target?: TargetChoice; unit?: UnitView; byId: Map<string, UnitView>; unitLabel(unit: UnitView): string; onPick(t: TargetChoice): void;
  strikes?: { plan: string[]; at: number; onNext(): void };
}) {
  const targets = ability.targets;
  const at = target ? targets.indexOf(target) : -1;
  const step = (d: number) => {
    if (targets.length === 0) return;
    onPick(targets[(Math.max(at, d > 0 ? -1 : 0) + d + targets.length) % targets.length]!);
  };
  const label = (t: TargetChoice) => (t.target.unit ? (byId.get(t.target.unit) ? unitLabel(byId.get(t.target.unit)!) : "You")
    : t.affected.length > 0 ? `${t.affected.length} target${t.affected.length === 1 ? "" : "s"}` : `(${t.aim[0]}, ${t.aim[1]})`);
  return (
    <div class="tx-aim">
      {unit ? (
        <div class={`tx-target ${unit.side === "Enemy" ? "enemy" : "ally"}`}>
          <Portrait unit={unit} size={60} />
          <div class="tx-target-body">
            <b>{unitLabel(unit)}</b>
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
              <li key={i} class={i === strikes.at ? "on" : i > strikes.at ? "later" : ""}>{byId.get(id) ? unitLabel(byId.get(id)!) : id}</li>
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
                {u ? unitLabel(u) : "You"}{bits ? ` ${bits}` : ""}
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
