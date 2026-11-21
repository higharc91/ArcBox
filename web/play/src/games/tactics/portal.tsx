import type { JSX } from "preact";
import { useEffect, useMemo, useRef, useState } from "preact/hooks";
import type { PortalProps } from "../module";
import { Crest, Logo, Portrait } from "./art";
import { colorName, readable, type ColorOptions, type PaletteColor } from "./colors";
import { DetailsSheet, modifier, Sheet as Panel, signed, STATS, Tags, type Context, type PortalDetails, type StatKey, type Topic } from "./details";
import { canCompose, lookFor, RETRY_MS, type Look } from "./look";
import { Section } from "./ui";
import { TrinketSlot, type Trinket } from "./trinket";
import { KeyRing, type KeyEntry } from "./quests";

/**
 * Wardenflame between sessions (sub-project 4): the party, your character, and what you can do to it. With no character
 * (or one that hasn't fought yet) you make one: race, class, background, a 27-point buy and a colour, with a live look. After that you
 * choose your subclass at level 3, spend ability score increases at 4 and 8, change your picks and your colour. Every rule is the
 * server's (`TacticsCampaignRules`); every explanation is the server's too (`details`, from the rules pack): tap anything for its sheet.
 */

const COST: Record<number, number> = { 8: 0, 9: 1, 10: 2, 11: 3, 12: 4, 13: 5, 14: 7, 15: 9 };
/** The twelve classes, for finding one in a "Dwarf Cleric" description. */
const CLASSES = ["barbarian", "bard", "cleric", "druid", "fighter", "monk", "paladin", "ranger", "rogue", "sorcerer", "warlock", "wizard"];

interface Option { id: string; name: string; tooltip: string }
interface Choice { level: number; choose: number | null; from: Option[]; picked?: string[] }
interface Me {
  name: string; describe: string; race: string; class: string; subclass: string | null; background: string; level: number; xp: number;
  scores: Record<StatKey, number>; picks: string[]; injuries: string[]; increasesLeft: number;
  /** Remaking now would use the one free remake after fighting. */
  freeRemake?: boolean;
  subclasses: { id: string; name: string }[] | null; choices: Choice[];
  color?: string; skills?: string[]; saves?: string[]; bonuses?: Record<string, number> | null;
  trinket?: Trinket;
  /** The voice the character barks in, picked or defaulted: "wry.high". */
  voice?: string;
}
/** A player voice: a set in a lower or higher register (lore spec §7). */
interface VoiceOption { id: string; set: string; register: "low" | "high"; name: string }
interface Options {
  races: { id: string; name: string }[]; classes: { id: string; name: string; role: string }[];
  backgrounds: { id: string; name: string; boosts: Partial<Record<StatKey, number>> }[]; pointBuy: number;
  colors?: ColorOptions;
}
export interface TacticsPortalView {
  level: number; completed: string[]; party: { name: string; character: string; xp: number; you: boolean; color?: string | null }[];
  me: Me | null; options: Options | null; details?: PortalDetails;
  /** The voices a character can bark in. */
  voices?: VoiceOption[];
  /** The party's key ring, once it holds a key (the owner's decision 8). */
  keys?: KeyEntry[];
}

export const pointsSpent = (scores: Record<StatKey, number>) => STATS.reduce((n, s) => n + (COST[scores[s]] ?? 99), 0);
const classIn = (describe: string) => CLASSES.find((c) => describe.toLowerCase().split(/\s+/).includes(c));

/** Opens a details sheet; null where the view has no details (an old server). */
type Explain = ((topic: Topic) => void) | null;

export function TacticsPortal({ campaign, readOnly, send }: PortalProps) {
  const view = campaign.view as TacticsPortalView;
  const [making, setMaking] = useState(view.me === null);
  const [topic, setTopic] = useState<{ topic: Topic; context: Context } | null>(null);
  // A new view (after a create, or any change) shows the sheet again, unless there's still no character.
  useEffect(() => setMaking(view.me === null), [JSON.stringify(view.me)]);
  const details = view.details ?? null;
  const explainWith = (context: Context): Explain => (details ? (t) => setTopic({ topic: t, context }) : null);
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
              {p.color ? <i class="tx-dot" style={{ "--c": p.color } as JSX.CSSProperties} aria-hidden="true" /> : null}
              <b>{p.name}</b>{p.you ? " (you)" : ""} <span class="tx-note">{p.character}</span>
            </li>
          );
        })}
      </ul>
      <KeyRing keys={view.keys} />
      {making && view.options ? (
        <Create options={view.options} readOnly={readOnly} send={send} onCancel={view.me ? () => setMaking(false) : undefined}
          freeRemake={view.me?.freeRemake ?? false} start={view.me} explainWith={explainWith} details={details} voices={view.voices ?? []} />
      ) : view.me ? (
        <CharacterSheet me={view.me} readOnly={readOnly} send={send} onRemake={view.options ? () => setMaking(true) : undefined}
          explainWith={explainWith} details={details} colors={view.options?.colors ?? null} voices={view.voices ?? []} />
      ) : null}
      {topic && details ? <DetailsSheet details={details} topic={topic.topic} context={topic.context} onClose={() => setTopic(null)} /> : null}
    </div>
  );
}

/** A small "i" that opens an option's details, beside the button that picks it. */
function InfoButton({ label, onClick }: { label: string; onClick(): void }) {
  return <button type="button" class="tx-info" aria-label={label} onClick={onClick}><span aria-hidden="true">i</span></button>;
}

/** A grid of mutually exclusive choices (race, background): one pressed at a time, each with its details. */
function Options<T extends { id: string; name: string }>({ label, items, value, onPick, readOnly, explain, kind }: {
  label: string; items: T[]; value: string; onPick(id: string): void; readOnly: boolean; explain: Explain; kind: "race" | "background";
}) {
  return (
    <div class="tx-opts" role="group" aria-label={label}>
      {items.map((it) => (
        <div class={`tx-opt${it.id === value ? " on" : ""}`} key={it.id}>
          <button type="button" class="tx-opt-pick" aria-pressed={it.id === value} disabled={readOnly} onClick={() => onPick(it.id)}>{it.name}</button>
          {explain ? <InfoButton label={`About ${it.name}`} onClick={() => explain({ kind, id: it.id })} /> : null}
        </div>
      ))}
    </div>
  );
}

/** The voice a character barks in during battle and at camp: six sets, each lower or higher. */
function VoicePicker({ voices, value, onPick, readOnly }: { voices: VoiceOption[]; value: string; onPick(id: string): void; readOnly: boolean }) {
  return (
    <div class="tx-opts" role="group" aria-label="Voice">
      {voices.map((v) => (
        <div class={`tx-opt${v.id === value ? " on" : ""}`} key={v.id}>
          <button type="button" class="tx-opt-pick" aria-pressed={v.id === value} disabled={readOnly} onClick={() => onPick(v.id)}>{v.name}</button>
        </div>
      ))}
    </div>
  );
}

/** The voice on the sheet, and Change for the picker, which saves on its own. */
function SheetVoice({ me, voices, readOnly, send }: { me: Me; voices: VoiceOption[]; readOnly: boolean; send: PortalProps["send"] }) {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState(me.voice ?? "");
  useEffect(() => setDraft(me.voice ?? ""), [me.voice]);
  return (
    <div class="tx-block">
      <h3 class="tx-sect">Your voice</h3>
      <div class="tx-color-row">
        <span>{voices.find((v) => v.id === me.voice)?.name ?? "None yet"}</span>
        <button type="button" class="tx-btn ghost tx-more" aria-label="Change voice" disabled={readOnly} onClick={() => setOpen(true)}>Change</button>
      </div>
      {open ? (
        <Panel title="Your voice" onClose={() => setOpen(false)}
          foot={<>
            <button type="button" class="tx-btn ghost" onClick={() => setOpen(false)}>Cancel</button>
            <button type="button" class="tx-btn gold" disabled={readOnly || !draft || draft === me.voice}
              onClick={() => { void send("setVoice", { voice: draft }); setOpen(false); }}>Save voice</button>
          </>}>
          <VoicePicker voices={voices} value={draft} onPick={setDraft} readOnly={readOnly} />
        </Panel>
      ) : null}
    </div>
  );
}

/** The chosen option's headline facts, with a button to its full sheet. */
function Summary({ title, lines, onMore }: { title: string; lines: string[]; onMore: (() => void) | null }) {
  return (
    <div class="tx-summary">
      <div>
        <b>{title}</b>
        {lines.map((l, i) => <span key={i} class="tx-tip">{l}</span>)}
      </div>
      {onMore ? <button type="button" class="tx-btn ghost tx-more" onClick={onMore}>Details</button> : null}
    </div>
  );
}

function Create({ options, readOnly, send, onCancel, freeRemake, start, explainWith, details, voices }: {
  options: Options; readOnly: boolean; send: PortalProps["send"]; onCancel?: () => void; freeRemake: boolean; start: Me | null;
  explainWith(c: Context): Explain; details: PortalDetails | null; voices: VoiceOption[];
}) {
  const [voice, setVoice] = useState(start?.voice ?? voices[0]?.id ?? "");
  const [race, setRace] = useState(start?.race ?? options.races[0]?.id ?? "");
  const [cls, setCls] = useState(start?.class ?? options.classes[0]?.id ?? "");
  const [background, setBackground] = useState(start?.background ?? options.backgrounds[0]?.id ?? "");
  const [scores, setScores] = useState<Record<StatKey, number>>({ str: 8, dex: 8, con: 8, int: 8, wis: 8, cha: 8 });
  const [color, setColor] = useState<string | null>(options.colors?.current ?? null);
  const left = options.pointBuy - pointsSpent(scores);
  const boosts = options.backgrounds.find((b) => b.id === background)?.boosts ?? {};
  const totals = Object.fromEntries(STATS.map((s) => [s, scores[s] + (boosts[s] ?? 0)])) as Record<StatKey, number>;
  const step = (s: StatKey, d: number) => {
    const next = scores[s] + d;
    if (next < 8 || next > 15) return;
    const tried = { ...scores, [s]: next };
    if (pointsSpent(tried) <= options.pointBuy) setScores(tried);
  };
  const choose = (t: Topic) => {
    if (readOnly) return null;
    if (t.kind === "race" && t.id !== race) return { label: `Pick ${options.races.find((r) => r.id === t.id)?.name ?? t.id}`, run: () => setRace(t.id) };
    if (t.kind === "class" && t.id !== cls) return { label: `Pick ${options.classes.find((c) => c.id === t.id)?.name ?? t.id}`, run: () => setCls(t.id) };
    if (t.kind === "background" && t.id !== background) return { label: `Pick ${options.backgrounds.find((b) => b.id === t.id)?.name ?? t.id}`, run: () => setBackground(t.id) };
    return null;
  };
  const explain = explainWith({ scores: totals, level: details?.level, choose });
  const raceName = options.races.find((r) => r.id === race)?.name ?? race;
  const clsName = options.classes.find((c) => c.id === cls)?.name ?? cls;
  const r = details?.races[race], c = details?.classes[cls], b = details?.backgrounds[background];
  const traitNames = (r?.traits ?? []).map((id) => details?.features[id]?.name ?? id);
  return (
    <section class="tx-panel">
      <h2 class="tx-title">Make your character</h2>
      {freeRemake ? (
        <p class="portal-banner" role="note">
          This is your one free remake. Your XP, injuries, gold and bag carry over, and after this the character is yours for good.
        </p>
      ) : start ? <p class="tx-note">Your gold, bag and injuries carry over. Remaking does not refill starting supplies.</p> : null}
      {start?.trinket ? <p class="tx-note">{start.trinket.name} carries over{cls !== start.trinket.class ? `, but stays dormant unless you choose ${start.trinket.className}` : " and is active for this class"}.</p> : null}

      <LookPreview race={race} cls={cls} color={color} title={`${raceName} ${clsName}`} />

      {options.colors ? <ColorPicker colors={options.colors} value={color} onChange={setColor} readOnly={readOnly} /> : null}

      <Section>Race</Section>
      <Options label="Race" kind="race" items={options.races} value={race} onPick={setRace} readOnly={readOnly} explain={explain} />
      {r ? <Summary title={r.name} lines={[r.facts.slice(0, 2).join(" "), traitNames.length ? `Traits: ${traitNames.join(", ")}.` : ""].filter(Boolean)}
        onMore={explain && (() => explain({ kind: "race", id: race }))} /> : null}

      <Section>Class</Section>
      <div class="tx-classes" role="group" aria-label="Class">
        {options.classes.map((k) => (
          <div class="tx-class-cell" key={k.id}>
            <button type="button" class={`tx-class${k.id === cls ? " on" : ""}`} aria-pressed={k.id === cls} disabled={readOnly}
              onClick={() => setCls(k.id)}>
              <Crest cls={k.id} scale={2} />
              <b>{k.name}</b>
              <span class="tx-role">{k.role}</span>
            </button>
            {explain ? <InfoButton label={`About ${k.name}`} onClick={() => explain({ kind: "class", id: k.id })} /> : null}
          </div>
        ))}
      </div>
      {c ? <Summary title={c.name} lines={[c.role, c.facts[0] ?? ""]} onMore={explain && (() => explain({ kind: "class", id: cls }))} /> : null}

      <Section>Background</Section>
      <Options label="Background" kind="background" items={options.backgrounds} value={background} onPick={setBackground} readOnly={readOnly} explain={explain} />
      {b ? <Summary title={b.name} lines={b.facts.slice(0, 2)} onMore={explain && (() => explain({ kind: "background", id: background }))} />
        : <p class="tx-note">{Object.entries(boosts).map(([s, n]) => `${s.toUpperCase()} +${n}`).join(", ") || "No score boosts"}</p>}

      {voices.length ? <>
        <Section>Voice</Section>
        <VoicePicker voices={voices} value={voice} onPick={setVoice} readOnly={readOnly} />
      </> : null}

      <p class="tx-sect tx-points">Ability scores: {left} of {options.pointBuy} points left</p>
      <div class="tx-scores">
        {STATS.map((s) => (
          <div class={`tx-score-row${boosts[s] ? " boosted" : ""}`} key={s}>
            {explain
              ? <button type="button" class="tx-score-name" aria-label={`About ${details?.stats[s]?.name ?? s}`} onClick={() => explain({ kind: "stat", id: s })}>{s.toUpperCase()}</button>
              : <span class="tx-score-name">{s.toUpperCase()}</span>}
            <span class="tx-score">{scores[s]}{boosts[s] ? <em>+{boosts[s]}</em> : null}</span>
            <span class="tx-score-mod">{signed(modifier(totals[s]))}</span>
            <button type="button" class="tx-step" aria-label={`Lower ${s}`} disabled={readOnly || scores[s] <= 8} onClick={() => step(s, -1)}>−</button>
            <button type="button" class="tx-step" aria-label={`Raise ${s}`}
              disabled={readOnly || scores[s] >= 15 || COST[scores[s] + 1]! - COST[scores[s]]! > left} onClick={() => step(s, 1)}>+</button>
          </div>
        ))}
      </div>
      {explain ? <p class="tx-note">Tap a score's name for what it does.</p> : null}
      <div class="tx-foot">
        {onCancel ? <button type="button" class="tx-btn ghost" onClick={onCancel}>Cancel</button> : null}
        <button type="button" class="tx-btn gold" disabled={readOnly}
          onClick={() => void send("createCharacter", { race, class: cls, background, scores, ...(color ? { color } : {}), ...(voice ? { voice } : {}) })}>Create</button>
      </div>
    </section>
  );
}

// ---- The look ---------------------------------------------------------------------------------------------------------

function Pixels({ image, scale, label }: { image: Look["sprite"]; scale: number; label?: string }) {
  const ref = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    const ctx = ref.current?.getContext("2d");
    if (!ctx) return;
    ctx.putImageData(new ImageData(new Uint8ClampedArray(image.data), image.width, image.height), 0, 0);
  }, [image]);
  return <canvas ref={ref} class="tx-pixels" width={image.width} height={image.height} role={label ? "img" : undefined} aria-label={label}
    aria-hidden={label ? undefined : true} style={{ width: `${image.width * scale}px`, height: `${image.height * scale}px` }} />;
}

/**
 * The character as the current race, class and colour would make them: the idle sprite standing in their colour's ring (as the TV draws
 * walkers), and the portrait. Composed in the browser from the pack's layers; where that can't be done (no canvas, or the pack hasn't
 * loaded), the class's portrait stands in.
 */
export function LookPreview({ race, cls, color, title, compact = false }: { race: string; cls: string; color: string | null; title: string; compact?: boolean }) {
  const [look, setLook] = useState<Look | null>(null);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    let live = true;
    let retry: ReturnType<typeof setTimeout> | undefined;
    if (attempt === 0) setLook(null);
    if (canCompose()) {
      void lookFor(race, cls).then((l) => {
        if (!live) return;
        setLook(l);
        // A failed look (a slow pack, a dropped request) is tried again a little later, not given up on.
        if (!l && attempt < 5) retry = setTimeout(() => setAttempt((a) => a + 1), RETRY_MS + 100);
      });
    }
    return () => { live = false; clearTimeout(retry); };
  }, [race, cls, attempt]);
  useEffect(() => setAttempt(0), [race, cls]);
  const ring = { "--c": color ?? "transparent" } as JSX.CSSProperties;
  return (
    <figure class={`tx-look${compact ? " compact" : ""}`} aria-label={`Your look: ${title}`}>
      {look ? (
        <>
          <div class="tx-look-stage" style={ring}>
            <i class="tx-ring" aria-hidden="true" />
            <Pixels image={look.sprite} scale={compact ? 2 : 3} />
          </div>
          {compact ? null : <Pixels image={look.portrait} scale={2} />}
        </>
      ) : (
        <div class="tx-look-stage" style={ring}>
          <Portrait unit={{ name: title, sprite: null, hp: 1, maxHp: 1, side: "Party" }} cls={cls} size={80} tone="you" />
        </div>
      )}
      {compact ? null : <figcaption><b>{title}</b><span class="tx-tip">{look ? "Your look. Your colour marks you on the TV and phones." : "Your class's portrait. Your colour marks you on the TV and phones."}</span></figcaption>}
    </figure>
  );
}

// ---- Colours ----------------------------------------------------------------------------------------------------------

/**
 * The colour picker: the palette's readable colours, and any colour of the player's own, which is lightened (as the server will) until
 * it shows on grass, stone, ash and the panels, with a note saying so. The swatches are a radio group.
 */
/** "a, b and c". */
const listed = (items: string[]) => (items.length < 2 ? items.join("") : `${items.slice(0, -1).join(", ")} and ${items.at(-1)}`);

export function ColorPicker({ colors, value, onChange, readOnly, heading = true }: {
  colors: ColorOptions; value: string | null; onChange(hex: string): void; readOnly: boolean; heading?: boolean;
}) {
  const [asked, setAsked] = useState<string | null>(null);
  const custom = value !== null && !colors.palette.some((p) => p.hex === value);
  const taken = colors.taken ?? [];
  const whose = (hex: string) => taken.filter((t) => t.hex === hex).map((t) => t.name);
  const pickCustom = (raw: string) => {
    const made = readable(raw, colors.grounds, colors.minContrast);
    if (!made) return;
    setAsked(made === raw.toLowerCase() ? null : raw.toLowerCase());
    onChange(made);
  };
  const said = `${value ? colorName(value, colors.palette) : ""}${value && whose(value).length ? `, also ${whose(value).join(" and ")}'s` : ""}${asked ? ` — lightened from ${asked} so it shows on ${listed(colors.grounds.map((g) => g.name))}.` : ""}`;
  // A screen reader hears the colour once it settles, not every step of a drag across the custom picker.
  const [announced, setAnnounced] = useState(said);
  useEffect(() => {
    const t = setTimeout(() => setAnnounced(said), 500);
    return () => clearTimeout(t);
  }, [said]);
  const group = useRef<HTMLDivElement>(null);
  // One stop for Tab (the chosen swatch, else the first); the arrows move along the palette and pick, as a radio group does.
  const current = colors.palette.findIndex((p) => p.hex === value);
  const onArrow = (e: KeyboardEvent, i: number) => {
    const step = e.key === "ArrowRight" || e.key === "ArrowDown" ? 1 : e.key === "ArrowLeft" || e.key === "ArrowUp" ? -1 : 0;
    if (!step || readOnly) return;
    e.preventDefault();
    const n = colors.palette.length;
    const next = (i + step + n) % n;
    setAsked(null);
    onChange(colors.palette[next]!.hex);
    group.current?.querySelectorAll<HTMLButtonElement>("button[role=radio]")[next]?.focus();
  };
  const swatch = (p: PaletteColor, i: number) => {
    const owners = whose(p.hex);
    return (
      <button key={p.hex} type="button" role="radio" aria-checked={value === p.hex}
        aria-label={owners.length ? `${p.name}, ${owners.join(" and ")}'s` : p.name}
        tabIndex={i === (current < 0 ? 0 : current) ? 0 : -1}
        class={`tx-swatch${value === p.hex ? " on" : ""}${owners.length ? " taken" : ""}`}
        style={{ "--c": p.hex } as JSX.CSSProperties} disabled={readOnly} onKeyDown={(e) => onArrow(e, i)}
        onClick={() => { setAsked(null); onChange(p.hex); }}>
        {owners.length ? <span class="tx-swatch-who" aria-hidden="true">{owners.map((o) => o.slice(0, 1)).join("")}</span> : null}
      </button>
    );
  };
  return (
    <div class="tx-colors">
      {heading ? <Section>Your colour</Section> : null}
      <p class="tx-tip">Marks you on the TV and on the phones: your ring, your vote dots, your card.</p>
      <div class="tx-swatches" role="radiogroup" aria-label="Your colour" ref={group}>
        {colors.palette.map(swatch)}
        <label class={`tx-swatch custom${custom ? " on" : ""}`} style={{ "--c": custom ? value! : "transparent" } as JSX.CSSProperties}>
          <span class="tx-sr">Your own colour</span>
          <input type="color" value={value ?? "#ffffff"} disabled={readOnly} onInput={(e) => pickCustom((e.target as HTMLInputElement).value)}
            onChange={(e) => pickCustom((e.target as HTMLInputElement).value)} />
          <span aria-hidden="true">{custom ? "" : "+"}</span>
        </label>
      </div>
      {taken.length ? <p class="tx-tip">Letters mark colours others have: {taken.map((t) => `${t.name.slice(0, 1)} is ${t.name}`).join(", ")}. You may share one.</p> : null}
      <div class="tx-grounds" aria-hidden="true">
        {colors.grounds.map((g) => (
          <span key={g.name} class="tx-ground" style={{ "--g": g.hex, "--c": value ?? "transparent" } as JSX.CSSProperties}><i />{g.name}</span>
        ))}
      </div>
      <p class="tx-tip" aria-hidden="true">{said}</p>
      <span class="tx-sr" role="status">{announced}</span>
    </div>
  );
}

// ---- The character sheet ----------------------------------------------------------------------------------------------

function CharacterSheet({ me, readOnly, send, onRemake, explainWith, details, colors, voices }: {
  me: Me; readOnly: boolean; send: PortalProps["send"]; onRemake?: () => void; explainWith(c: Context): Explain;
  details: PortalDetails | null; colors: ColorOptions | null; voices: VoiceOption[];
}) {
  const choose = (t: Topic) => (t.kind === "subclass" && me.subclasses?.some((s) => s.id === t.id) && !readOnly
    ? { label: `Choose ${me.subclasses.find((s) => s.id === t.id)!.name}`, run: () => void send("chooseSubclass", { subclass: t.id }) }
    : null);
  const explain = explainWith({ scores: me.scores, skills: me.skills, bonuses: me.bonuses, level: me.level, choose });
  const features = useMemo(() => ownFeatures(me, details), [me, details]);
  return (
    <section class="tx-panel">
      <div class="tx-hero">
        <LookPreview race={me.race} cls={me.class} color={me.color ?? null} title={me.describe} compact />
        <div>
          <h2 class="tx-title">{me.name}</h2>
          <span class="tx-note">{me.describe} · Lv {me.level}</span>
          <span class="tx-note">{me.xp} XP{me.injuries.length ? ` · ${me.injuries.join(", ")}` : ""}</span>
        </div>
      </div>
      {me.trinket ? <TrinketSlot trinket={me.trinket} /> : null}
      {explain ? (
        <div class="tx-chips" aria-label="About your character">
          <button type="button" class="tx-chipbtn" onClick={() => explain({ kind: "race", id: me.race })}>{details?.races[me.race]?.name ?? me.race}</button>
          <button type="button" class="tx-chipbtn" onClick={() => explain({ kind: "class", id: me.class })}>{details?.classes[me.class]?.name ?? me.class}</button>
          {me.subclass ? <button type="button" class="tx-chipbtn" onClick={() => explain({ kind: "subclass", id: me.subclass!, cls: me.class })}>{details?.classes[me.class]?.subclasses.find((s) => s.id === me.subclass)?.name ?? me.subclass}</button> : null}
          <button type="button" class="tx-chipbtn" onClick={() => explain({ kind: "background", id: me.background })}>{details?.backgrounds[me.background]?.name ?? me.background}</button>
        </div>
      ) : null}
      <div class="tx-score-grid">
        {STATS.map((s) => {
          const inner = <><small>{s.toUpperCase()}</small><b class="tx-score">{me.scores[s]}</b><i>{signed(modifier(me.scores[s]))}</i></>;
          return explain
            ? <button type="button" key={s} aria-label={`${details?.stats[s]?.name ?? s} ${me.scores[s]}, ${signed(modifier(me.scores[s]))}`} onClick={() => explain({ kind: "stat", id: s })}>{inner}</button>
            : <div key={s}>{inner}</div>;
        })}
      </div>
      {me.skills?.length && explain ? (
        <div class="tx-block">
          <h3 class="tx-sect">Skills</h3>
          <div class="tx-chips">
            {me.skills.map((s) => (
              <button key={s} type="button" class="tx-chipbtn" onClick={() => explain({ kind: "skill", id: s })}>
                {details?.skills[s]?.name ?? s}{me.bonuses?.[s] !== undefined ? ` ${signed(me.bonuses[s]!)}` : ""}
              </button>
            ))}
          </div>
        </div>
      ) : null}
      {colors || me.color ? <SheetColor me={me} colors={colors} readOnly={readOnly} send={send} /> : null}
      {voices.length ? <SheetVoice me={me} voices={voices} readOnly={readOnly} send={send} /> : null}
      {me.subclasses ? (
        <div class="tx-block">
          <h3 class="tx-sect">Choose your subclass</h3>
          <div class="tx-subclasses">
            {me.subclasses.map((s) => (
              <div class="tx-opt tall" key={s.id}>
                <button type="button" class="tx-subclass" disabled={readOnly} onClick={() => void send("chooseSubclass", { subclass: s.id })}>{s.name}</button>
                {explain ? <InfoButton label={`About ${s.name}`} onClick={() => explain({ kind: "subclass", id: s.id, cls: me.class })} /> : null}
              </div>
            ))}
          </div>
        </div>
      ) : null}
      {me.increasesLeft >= 2 ? <Increase readOnly={readOnly} send={send} /> : null}
      {features.length && explain ? (
        <div class="tx-block">
          <h3 class="tx-sect">Your features</h3>
          <div class="tx-chips">
            {features.map((id) => <button key={id} type="button" class="tx-chipbtn" onClick={() => explain({ kind: "feature", id })}>{details?.features[id]?.name ?? id}</button>)}
          </div>
        </div>
      ) : null}
      <Picks me={me} readOnly={readOnly} send={send} explain={explain} details={details} />
      {onRemake ? <button type="button" class="tx-btn ghost" disabled={readOnly} onClick={onRemake}>Remake character</button> : null}
    </section>
  );
}

/** The features the character has: race traits, then the class's and subclass's up to their level, in level order. */
function ownFeatures(me: Me, details: PortalDetails | null): string[] {
  if (!details) return [];
  const cls = details.classes[me.class];
  const sub = cls?.subclasses.find((s) => s.id === me.subclass);
  const ids = [...(details.races[me.race]?.traits ?? [])];
  for (const l of [...(cls?.levels ?? []), ...(sub?.levels ?? [])].filter((x) => x.level <= me.level).sort((a, b) => a.level - b.level))
    ids.push(...l.features);
  return [...new Set(ids)].filter((id) => details.features[id]);
}

/** The colour on the sheet: a swatch, and Change for the picker, which saves on its own. */
function SheetColor({ me, colors, readOnly, send }: { me: Me; colors: ColorOptions | null; readOnly: boolean; send: PortalProps["send"] }) {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState(me.color ?? null);
  useEffect(() => setDraft(me.color ?? null), [me.color]);
  return (
    <div class="tx-block">
      <h3 class="tx-sect">Your colour</h3>
      <div class="tx-color-row">
        <i class="tx-dot wide" style={{ "--c": me.color ?? "transparent" } as JSX.CSSProperties} aria-hidden="true" />
        <span>{me.color ? colorName(me.color, colors?.palette ?? []) : "None yet"}</span>
        {colors ? <button type="button" class="tx-btn ghost tx-more" disabled={readOnly} onClick={() => setOpen(true)}>Change</button> : null}
      </div>
      {open && colors ? (
        <Panel title="Your colour" onClose={() => setOpen(false)}
          foot={<>
            <button type="button" class="tx-btn ghost" onClick={() => setOpen(false)}>Cancel</button>
            <button type="button" class="tx-btn gold" disabled={readOnly || !draft || draft === me.color}
              onClick={() => { void send("setColor", { color: draft }); setOpen(false); }}>Save colour</button>
          </>}>
          <ColorPicker colors={colors} value={draft} onChange={setDraft} readOnly={readOnly} heading={false} />
        </Panel>
      ) : null}
    </div>
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

/** The picks each choice holds: the server's matching when it sends one, else the picks its list offers, in order, up to its count. */
export function placed(me: Pick<Me, "picks" | "choices">): string[][] {
  if (me.choices.every((c) => c.picked)) return me.choices.map((c) => c.picked!);
  const used = new Set<string>();
  return me.choices.map((c) => {
    const mine = me.picks.filter((p) => !used.has(p) && c.from.some((o) => o.id === p)).slice(0, c.choose ?? 0);
    mine.forEach((p) => used.add(p));
    return mine;
  });
}

/**
 * Your picks, one line per choice ("Level 4 · choose 1"): the names picked, each opening its details, and Change, which opens that
 * choice's list. Saving sends the whole set with the choice's picks swapped; the server checks it fits the features.
 */
function Picks({ me, readOnly, send, explain, details }: { me: Me; readOnly: boolean; send: PortalProps["send"]; explain: Explain; details: PortalDetails | null }) {
  const [editing, setEditing] = useState<number | null>(null);
  const groups = placed(me);
  if (me.choices.length === 0) return null;
  const nameOf = (id: string) => details?.features[id]?.name ?? me.choices.flatMap((c) => c.from).find((o) => o.id === id)?.name ?? id;
  return (
    <div class="tx-block">
      <h3 class="tx-sect">Picks</h3>
      <ul class="tx-pick-groups">
        {me.choices.map((c, i) => (
          <li key={i} class="tx-pick-group">
            <div class="tx-pick-group-head">
              <span>Level {c.level} · choose {c.choose}</span>
              <button type="button" class="tx-btn ghost tx-more" disabled={readOnly} aria-label={`Change level ${c.level} picks: ${groups[i]!.length ? groups[i]!.map(nameOf).join(", ") : "none yet"}`} onClick={() => setEditing(i)}>Change</button>
            </div>
            <div class="tx-chips">
              {groups[i]!.length === 0 ? <span class="tx-tip">None picked</span> : groups[i]!.map((id) => (
                explain
                  ? <button key={id} type="button" class="tx-chipbtn" onClick={() => explain({ kind: "feature", id })}>{nameOf(id)}</button>
                  : <span key={id} class="tx-chip">{nameOf(id)}</span>
              ))}
            </div>
          </li>
        ))}
      </ul>
      {editing !== null ? (
        <PickList choice={me.choices[editing]!} mine={groups[editing]!} elsewhere={groups.flatMap((g, i) => (i === editing ? [] : g.map((id) => [id, me.choices[i]!.level] as const)))}
          details={details} readOnly={readOnly} onClose={() => setEditing(null)}
          onSave={(chosen) => {
            // The picks the other choices hold: a pick that fits no choice (a stale one) is left out, or every save would be refused.
            const others = groups.flatMap((g, i) => (i === editing ? [] : g));
            void send("setPicks", { picks: [...others, ...chosen.filter((p) => !others.includes(p))] });
            setEditing(null);
          }} />
      ) : null}
    </div>
  );
}

/** One choice's list, full screen: tick up to its count; each option's details open in place. */
function PickList({ choice, mine, elsewhere, details, readOnly, onClose, onSave }: {
  choice: Choice; mine: string[]; elsewhere: (readonly [string, number])[]; details: PortalDetails | null; readOnly: boolean;
  onClose(): void; onSave(chosen: string[]): void;
}) {
  const [chosen, setChosen] = useState<string[]>(mine);
  const [shown, setShown] = useState<string | null>(null);
  const max = choice.choose ?? 0;
  const toggle = (id: string) => setChosen((c) => (c.includes(id) ? c.filter((x) => x !== id) : max === 1 ? [id] : c.length < max ? [...c, id] : c));
  const changed = [...chosen].sort().join() !== [...mine].sort().join();
  return (
    <Panel title={`Level ${choice.level} · choose ${choice.choose}`} eyebrow={`${chosen.length} of ${max} picked`} onClose={onClose} wide
      foot={<>
        <button type="button" class="tx-btn ghost" onClick={onClose}>Cancel</button>
        <button type="button" class="tx-btn gold" disabled={readOnly || !changed} onClick={() => onSave(chosen)}>Save picks</button>
      </>}>
      <ul class="tx-picklist">
        {choice.from.map((o) => {
          const taken = elsewhere.find(([id]) => id === o.id);
          const on = chosen.includes(o.id);
          const f = details?.features[o.id];
          return (
            <li key={o.id} class={`tx-pick${on ? " on" : ""}${taken ? " taken" : ""}`}>
              <label>
                <input type="checkbox" checked={on || !!taken} disabled={readOnly || !!taken || (!on && max > 1 && chosen.length >= max)}
                  onChange={() => toggle(o.id)} />
                <span>
                  <b>{o.name}</b>
                  {taken ? <span class="tx-tip">Picked at level {taken[1]}</span> : f ? <Tags tags={f.tags} /> : null}
                </span>
              </label>
              <button type="button" class="tx-btn ghost tx-more" aria-expanded={shown === o.id} aria-label={`${shown === o.id ? "Hide" : "Show"} ${o.name}`}
                onClick={() => setShown((s) => (s === o.id ? null : o.id))}>{shown === o.id ? "Hide" : "What it does"}</button>
              {shown === o.id ? <p class="tx-feature-text tx-pick-text">{f?.text ?? o.tooltip}</p> : null}
            </li>
          );
        })}
      </ul>
    </Panel>
  );
}
