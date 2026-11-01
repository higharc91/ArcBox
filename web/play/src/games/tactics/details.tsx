import type { ComponentChildren } from "preact";
import { useEffect, useRef, useState } from "preact/hooks";
import { Crest } from "./art";

/**
 * The portal's details sheets (the user's note on the character screens, 2026-11-01): tap any race, class, background, ability score,
 * skill, subclass, feature or spell for what it gives and what it does in play. Every word comes from the portal view's `details`, which
 * the server writes from the rules pack and the engine's own rules (`CharacterGuide`); nothing here is game data.
 */

export type StatKey = "str" | "dex" | "con" | "int" | "wis" | "cha";
export const STATS: StatKey[] = ["str", "dex", "con", "int", "wis", "cha"];

export interface LevelGuide { level: number; features: string[]; attacks: number | null; choices: { choose: number; from: string[] }[] }
export interface PortalDetails {
  level: number;
  races: Record<string, { name: string; facts: string[]; traits: string[] }>;
  classes: Record<string, { name: string; role: string; facts: string[]; levels: LevelGuide[]; subclasses: { id: string; name: string; levels: LevelGuide[] }[] }>;
  backgrounds: Record<string, { name: string; facts: string[]; boosts: Partial<Record<StatKey, number>>; skills: string[] }>;
  features: Record<string, { name: string; tags: string[]; text: string }>;
  skills: Record<string, { name: string; stat: StatKey; text: string }>;
  stats: Record<StatKey, { name: string; drives: string[] }>;
}

export type Topic =
  | { kind: "race" | "class" | "background" | "skill" | "feature"; id: string }
  | { kind: "stat"; id: StatKey }
  | { kind: "subclass"; id: string; cls: string };

/** What the sheet knows of the character it's for, to say "your modifier" and "your bonus". */
export interface Context {
  scores?: Record<StatKey, number>;
  skills?: string[];
  /** Each skill's bonus in a story check, from the server. */
  bonuses?: Record<string, number> | null;
  level?: number;
  /** "Pick this" for a topic that can be chosen here (creation's race, class and background; the sheet's subclass). */
  choose?: (topic: Topic) => { label: string; run(): void } | null;
}

export const modifier = (score: number) => Math.floor((score - 10) / 2);
export const signed = (n: number) => (n >= 0 ? `+${n}` : `−${-n}`);

/** The title of a topic, for the sheet's heading and a button's label. */
export function topicName(d: PortalDetails, t: Topic): string {
  switch (t.kind) {
    case "race": return d.races[t.id]?.name ?? t.id;
    case "class": return d.classes[t.id]?.name ?? t.id;
    case "background": return d.backgrounds[t.id]?.name ?? t.id;
    case "skill": return d.skills[t.id]?.name ?? t.id;
    case "feature": return d.features[t.id]?.name ?? t.id;
    case "stat": return d.stats[t.id]?.name ?? t.id.toUpperCase();
    case "subclass": return d.classes[t.cls]?.subclasses.find((s) => s.id === t.id)?.name ?? t.id;
  }
}

const KIND: Record<Topic["kind"], string> = {
  race: "Race", class: "Class", background: "Background", skill: "Skill", feature: "Feature", stat: "Ability score", subclass: "Subclass",
};

const reducedMotion = () => typeof matchMedia === "function" && matchMedia("(prefers-reduced-motion: reduce)").matches;

/**
 * A modal bottom sheet: a heading and a close button, focus moved in on opening and back to what opened it on closing, Escape and the
 * backdrop close it, and Tab stays inside. It slides up unless the player asked for reduced motion.
 */
export function Sheet({ title, eyebrow, onClose, onBack, children, foot, wide = false }: {
  title: string; eyebrow?: string; onClose(): void; onBack?: () => void; children: ComponentChildren; foot?: ComponentChildren; wide?: boolean;
}) {
  const box = useRef<HTMLDivElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null;
    closeRef.current?.focus();
    return () => opener?.focus?.();
  }, []);
  const onKey = (e: KeyboardEvent) => {
    if (e.key === "Escape") {
      e.stopPropagation();
      onClose();
      return;
    }
    if (e.key !== "Tab" || !box.current) return;
    const focusable = [...box.current.querySelectorAll<HTMLElement>("button, [href], input, summary, [tabindex]:not([tabindex='-1'])")]
      .filter((el) => !(el as HTMLButtonElement).disabled);
    if (focusable.length === 0) return;
    const first = focusable[0]!, last = focusable[focusable.length - 1]!;
    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault();
      first.focus();
    }
  };
  const id = useRef(`tx-sheet-${Math.random().toString(36).slice(2)}`).current;
  return (
    <div class="tx-sheet-scrim" onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}>
      <div ref={box} class={`tx-sheet${wide ? " wide" : ""}${reducedMotion() ? " still" : ""}`} role="dialog" aria-modal="true" aria-labelledby={id}
        onKeyDown={onKey}>
        <header class="tx-sheet-head">
          {onBack ? <button type="button" class="tx-sheet-icon" aria-label="Back" onClick={onBack}>‹</button> : null}
          <div class="tx-sheet-titles">
            {eyebrow ? <span class="tx-eyebrow">{eyebrow}</span> : null}
            <h2 id={id} class="tx-sheet-title">{title}</h2>
          </div>
          <button ref={closeRef} type="button" class="tx-sheet-icon" aria-label="Close" onClick={onClose}>×</button>
        </header>
        <div class="tx-sheet-body">{children}</div>
        {foot ? <div class="tx-sheet-foot">{foot}</div> : null}
      </div>
    </div>
  );
}

/** The details sheet: a topic, and any topic tapped inside it, with Back to the one before. */
export function DetailsSheet({ details, topic, context, onClose }: { details: PortalDetails; topic: Topic; context: Context; onClose(): void }) {
  const [stack, setStack] = useState<Topic[]>([topic]);
  useEffect(() => setStack([topic]), [JSON.stringify(topic)]);
  const top = stack[stack.length - 1]!;
  const open = (t: Topic) => setStack((s) => [...s, t]);
  const choice = context.choose?.(top) ?? null;
  return (
    <Sheet title={topicName(details, top)} eyebrow={KIND[top.kind]} onClose={onClose}
      onBack={stack.length > 1 ? () => setStack((s) => s.slice(0, -1)) : undefined}
      foot={choice ? <button type="button" class="tx-btn gold" onClick={() => { choice.run(); onClose(); }}>{choice.label}</button> : null}>
      <Body details={details} topic={top} context={context} open={open} />
    </Sheet>
  );
}

function Facts({ facts }: { facts: string[] }) {
  return <ul class="tx-facts">{facts.map((f, i) => <li key={i}>{f}</li>)}</ul>;
}

/** A feature in full: name, what it costs, and the engine's text. */
export function FeatureCard({ details, id, open }: { details: PortalDetails; id: string; open?: (t: Topic) => void }) {
  const f = details.features[id];
  if (!f) return null;
  return (
    <div class="tx-feature">
      <div class="tx-feature-head">
        {open ? <button type="button" class="tx-feature-name" onClick={() => open({ kind: "feature", id })}>{f.name}</button> : <b>{f.name}</b>}
      </div>
      <Tags tags={f.tags} />
      <p class="tx-feature-text">{f.text}</p>
    </div>
  );
}

export const Tags = ({ tags }: { tags: string[] }) => (
  <span class="tx-ftags">{tags.map((t) => <i key={t}>{t}</i>)}</span>
);

/** A feature named in a list: a button to its own sheet. */
function FeatureLink({ details, id, open }: { details: PortalDetails; id: string; open(t: Topic): void }) {
  const f = details.features[id];
  return <button type="button" class="tx-chipbtn" onClick={() => open({ kind: "feature", id })}>{f?.name ?? id}</button>;
}

function Levels({ details, levels, open }: { details: PortalDetails; levels: LevelGuide[]; open(t: Topic): void }) {
  return (
    <ol class="tx-levels">
      {levels.map((l) => (
        <li key={l.level}>
          <span class="tx-level-n">Level {l.level}</span>
          <div class="tx-level-body">
            {l.attacks ? <p class="tx-note">Attack makes {l.attacks} attacks.</p> : null}
            {l.features.map((id) => <FeatureCard key={id} details={details} id={id} open={open} />)}
            {l.choices.map((c, i) => (
              <div key={i} class="tx-level-choice">
                <p class="tx-note">Choose {c.choose} of:</p>
                <div class="tx-chips">{c.from.map((id) => <FeatureLink key={id} details={details} id={id} open={open} />)}</div>
              </div>
            ))}
          </div>
        </li>
      ))}
    </ol>
  );
}

function Body({ details: d, topic: t, context, open }: { details: PortalDetails; topic: Topic; context: Context; open(t: Topic): void }) {
  switch (t.kind) {
    case "race": {
      const r = d.races[t.id];
      if (!r) return null;
      return (
        <>
          <Facts facts={r.facts} />
          {r.traits.length ? <h3 class="tx-sect">Traits</h3> : null}
          {r.traits.map((id) => <FeatureCard key={id} details={d} id={id} open={open} />)}
        </>
      );
    }
    case "class": {
      const c = d.classes[t.id];
      if (!c) return null;
      return (
        <>
          <div class="tx-sheet-lead"><Crest cls={t.id} scale={2} /><p>{c.role}</p></div>
          <Facts facts={c.facts} />
          <h3 class="tx-sect">By level</h3>
          <Levels details={d} levels={c.levels} open={open} />
          {c.subclasses.length ? (
            <>
              <h3 class="tx-sect">Subclasses, chosen at level 3</h3>
              <div class="tx-chips">
                {c.subclasses.map((s) => <button key={s.id} type="button" class="tx-chipbtn" onClick={() => open({ kind: "subclass", id: s.id, cls: t.id })}>{s.name}</button>)}
              </div>
            </>
          ) : null}
        </>
      );
    }
    case "subclass": {
      const s = d.classes[t.cls]?.subclasses.find((x) => x.id === t.id);
      if (!s) return null;
      return s.levels.length ? <Levels details={d} levels={s.levels} open={open} /> : <p class="tx-note">Its features start at level 3.</p>;
    }
    case "background": {
      const b = d.backgrounds[t.id];
      if (!b) return null;
      return (
        <>
          <Facts facts={b.facts} />
          <h3 class="tx-sect">Skills</h3>
          <div class="tx-chips">{b.skills.map((s) => <button key={s} type="button" class="tx-chipbtn" onClick={() => open({ kind: "skill", id: s })}>{d.skills[s]?.name ?? s}</button>)}</div>
          <h3 class="tx-sect">Boosts</h3>
          <div class="tx-chips">
            {Object.entries(b.boosts).map(([s, n]) => (
              <button key={s} type="button" class="tx-chipbtn" onClick={() => open({ kind: "stat", id: s as StatKey })}>{s.toUpperCase()} +{n}</button>
            ))}
          </div>
        </>
      );
    }
    case "stat": {
      const s = d.stats[t.id];
      if (!s) return null;
      const score = context.scores?.[t.id];
      const skills = Object.entries(d.skills).filter(([, k]) => k.stat === t.id);
      return (
        <>
          {score !== undefined ? (
            <div class="tx-stat-now" aria-label={`Your ${s.name}: ${score}, modifier ${signed(modifier(score))}`}>
              <span><small>Score</small><b>{score}</b></span>
              <span><small>Modifier</small><b>{signed(modifier(score))}</b></span>
            </div>
          ) : null}
          <h3 class="tx-sect">What it drives</h3>
          <Facts facts={s.drives} />
          {skills.length ? (
            <div class="tx-chips">{skills.map(([id, k]) => <button key={id} type="button" class="tx-chipbtn" onClick={() => open({ kind: "skill", id })}>{k.name}</button>)}</div>
          ) : null}
        </>
      );
    }
    case "skill": {
      const k = d.skills[t.id];
      if (!k) return null;
      const proficient = context.skills?.includes(t.id) ?? false;
      const bonus = context.bonuses?.[t.id];
      return (
        <>
          {bonus !== undefined ? (
            <div class="tx-stat-now">
              <span><small>Your bonus</small><b>{signed(bonus)}</b></span>
              <span><small>Proficient</small><b>{proficient ? "Yes" : "No"}</b></span>
            </div>
          ) : null}
          <Facts facts={[k.text]} />
          <button type="button" class="tx-chipbtn" onClick={() => open({ kind: "stat", id: k.stat })}>About {d.stats[k.stat]?.name ?? k.stat}</button>
        </>
      );
    }
    case "feature": {
      const f = d.features[t.id];
      if (!f) return null;
      return (
        <>
          <Tags tags={f.tags} />
          <p class="tx-feature-text large">{f.text}</p>
          <p class="tx-note">Dice as at level {context.level ?? d.level}.</p>
        </>
      );
    }
  }
}
