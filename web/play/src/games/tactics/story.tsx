import { useEffect, useRef, useState } from "preact/hooks";
import type { CustomScreenProps } from "../module";
import { MoodPortrait, Portrait } from "./art";
import { useAlertOnMount, useSfx } from "./sfx";
import type { CheckCard, CheckData, CheckRoll, DialogueData, StoryOption, VoteData } from "./types";
import { className, LineIcon, useShellClock } from "./ui";

/**
 * The story on a phone (5d §9): the line being spoken, the party's votes and the leader's choices, and skill checks.
 * The TV speaks and shows the tally; a phone carries the words, a way to answer, and nothing that changes because of
 * what someone else did.
 */

/** A skip tapped this soon after a new line shows is the second tap of a double tap meant for the line before, and is dropped. */
export const SKIP_GUARD_MS = 600;

/** "persuasion" → "Persuasion", "animal_handling" → "Animal Handling". */
const skillName = (skill: string) => className(skill) ?? skill;
/** A bonus as a card shows it: +2, +0, −1 (a real minus). */
const signed = (n: number) => (n < 0 ? `−${-n}` : `+${n}`);
/** "Ann", "Ann and Bo", "Ann, Bo and Cy". */
const names = (list: string[]) => (list.length < 2 ? list.join("") : `${list.slice(0, -1).join(", ")} and ${list[list.length - 1]}`);
const LETTERS = "ABCDEFGHIJ";

/** The question's clock, in its heading, from the shell's countdown (which the CSS hides on these screens). */
function Clock() {
  const ref = useRef<HTMLSpanElement>(null);
  const clock = useShellClock(ref);
  return (
    <span ref={ref} class={`tx-clock${clock?.urgent ? " urgent" : ""}`} role="timer" aria-label={clock ? `${clock.text} left` : undefined}>
      {clock?.text ?? ""}
    </span>
  );
}

/** A question's heading: what kind it is, what's asked of you, and its clock. */
function AskHead({ eyebrow, title, tie }: { eyebrow: string; title: string; tie: boolean }) {
  return (
    <div class={`tx-ask-head${tie ? " tie" : ""}`}>
      <div>
        <span class="tx-eyebrow">{eyebrow}</span>
        <h2 class="tx-ask-title">{title}</h2>
      </div>
      <Clock />
    </div>
  );
}

// ---- Dialogue ------------------------------------------------------------------------------------------------------

/**
 * The line the TV is speaking: who says it, in their portrait and mood, and the words. Lines move on by themselves; the
 * leader can skip one. A skip names the line it meant, goes once, and isn't taken in the first moment a new line shows,
 * so the second tap of a double tap can't skip the line after.
 */
export function Dialogue({ data, send, viewId }: CustomScreenProps) {
  const d = data as DialogueData;
  const sfx = useSfx();
  const shown = useRef({ view: viewId, at: Date.now() });
  if (shown.current.view !== viewId) shown.current = { view: viewId, at: Date.now() };
  const [skipped, setSkipped] = useState<string | null>(null);
  const sentFor = useRef<string | null>(null);

  const skip = () => {
    if (!d.line || sentFor.current === d.line || Date.now() - shown.current.at < SKIP_GUARD_MS) return;
    sentFor.current = d.line;
    setSkipped(d.line);
    sfx("select");
    send("skip", { line: d.line });
  };

  const narration = !d.speaker;
  return (
    <div class="tx tx-story">
      {d.title ? <p class="tx-story-title"><span class="tx-eyebrow">The story</span><b>{d.title}</b></p> : null}
      {d.text ? (
        <figure key={d.line} class={`tx-say${narration ? " narration" : ""}`}>
          {d.speaker ? (
            <figcaption class="tx-say-who">
              {d.portrait ? <MoodPortrait preset={d.portrait} mood={d.mood} name={d.speaker} size={120} /> : null}
              <b>{d.speaker}</b>
            </figcaption>
          ) : null}
          <blockquote class="tx-say-text">{narration ? d.text : `“${d.text}”`}</blockquote>
        </figure>
      ) : (
        <div class="tx-say quiet" role="status">
          <p class="tx-note center">The story goes on. Watch the TV.</p>
        </div>
      )}
      {d.canSkip && d.text ? (
        <div class="tx-foot tx-story-foot">
          <p class="tx-note">Lines move on by themselves.</p>
          <button type="button" class="tx-btn ghost tx-skip" disabled={skipped === d.line} onClick={skip}>
            <LineIcon name="skip" />
            {skipped === d.line ? "Skipping…" : "Skip line"}
          </button>
        </div>
      ) : null}
    </div>
  );
}

// ---- Votes and choices ---------------------------------------------------------------------------------------------

function OptionBody({ option, letter, tag }: { option: StoryOption; letter: string; tag: string | null }) {
  return (
    <>
      <span class="tx-letter" aria-hidden="true">{letter}</span>
      <span class="tx-opt-text">
        <b>“{option.label}”</b>
        {option.skill ? (
          <span class="tx-opt-check">
            <i aria-hidden="true" />
            {`${skillName(option.skill)} check${option.dc != null ? ` · DC ${option.dc}` : ""}`}
          </span>
        ) : null}
      </span>
      {tag ? <span class="tx-opt-tag">{tag}</span> : null}
    </>
  );
}

/** Plays the alert again when a tie-break lands on this phone, which is already showing the question. */
function useTieAlert(tie: boolean, mine: boolean) {
  const sfx = useSfx();
  const was = useRef(tie);
  useEffect(() => {
    if (tie && !was.current && mine) sfx("yourTurn");
    was.current = tie;
  }, [tie]);
}

/**
 * A question: the party's vote, or the leader's choice for everyone. Options are big tap targets; a tap answers at once
 * and shows as yours before the server's view comes back, and stays changeable until the clock runs out. Those who
 * can't answer see the options read-only, with the party's choice when there is one.
 */
export function Vote({ data, send, viewId }: CustomScreenProps) {
  const d = data as VoteData;
  const sfx = useSfx();
  const alerted = useAlertOnMount("yourTurn", d.canChoose);
  useTieAlert(d.tie, d.canChoose);
  const [tapped, setTapped] = useState<{ id: string; view: string } | null>(null);
  const mine = tapped?.view === viewId ? tapped.id : d.mine;

  const pick = (id: string) => {
    if (id === mine) return;
    setTapped({ id, view: viewId });
    sfx("confirm");
    send("vote", { option: id });
  };

  const eyebrow = d.tie ? "Tie-break" : d.party ? "Party vote" : "The leader's choice";
  const title = d.tie
    ? d.canChoose ? "Tied: you break it" : `Tied: ${d.decider ?? "the leader"} breaks it`
    : d.party
      ? d.canChoose ? "Cast your vote" : "The party is voting"
      : d.canChoose ? "You choose for the party" : `${d.decider ?? "The leader"} chooses for the party`;
  const hint = !d.canChoose
    ? "Watch the TV."
    : d.tie
      ? "Only the tied options are left."
      : d.party
        ? "You can change your vote until time runs out."
        : "You lead, so this one's yours. You can change it until time runs out.";
  const tagOf = (id: string) =>
    id === mine ? (d.party ? "Your vote" : "Your choice") : !mine && id === d.chosen ? "The party's choice" : null;

  return (
    <div class={`tx tx-ask${alerted ? " arrived" : ""}`}>
      <AskHead eyebrow={eyebrow} title={title} tie={d.tie} />
      <ul class={`tx-options${d.canChoose ? "" : " static"}`} aria-label="Options">
        {d.options.map((o, i) => {
          const tag = tagOf(o.id);
          const on = tag !== null;
          return (
            <li key={o.id}>
              {d.canChoose ? (
                <button type="button" class={`tx-option${on ? " on" : ""}`} aria-pressed={o.id === mine} onClick={() => pick(o.id)}>
                  <OptionBody option={o} letter={LETTERS[i] ?? ""} tag={tag} />
                </button>
              ) : (
                <div class={`tx-option${on ? " on" : ""}`}>
                  <OptionBody option={o} letter={LETTERS[i] ?? ""} tag={tag} />
                </div>
              )}
            </li>
          );
        })}
      </ul>
      <p class="tx-note">{hint}</p>
      {d.party && d.away.length > 0
        ? <p class="tx-note tx-away">{`${names(d.away)} ${d.away.length === 1 ? "is" : "are"} away and ${d.away.length === 1 ? "doesn't" : "don't"} vote`}</p>
        : null}
    </div>
  );
}

// ---- Skill checks --------------------------------------------------------------------------------------------------

/** A class's name as the art knows it. */
const classId = (name: string) => name.toLowerCase().replaceAll(" ", "_");

function CardBody({ card, tag }: { card: CheckCard; tag: string | null }) {
  return (
    <>
      {tag ? <span class="tx-rcard-tag">{tag}</span> : !card.here ? <span class="tx-rcard-tag away">Away</span> : null}
      <Portrait unit={{ name: card.name, sprite: null, hp: 1, maxHp: 1, side: "Party" }} cls={classId(card.cls)} size={60} />
      <b class="tx-rcard-name">{card.name}</b>
      <span class="tx-rcard-who">{card.player !== card.name ? `${card.player} · ${card.cls}` : card.cls}</span>
      <span class="tx-rcard-bonus">{signed(card.bonus)}</span>
      <span class="tx-rcard-note">{card.note}</span>
    </>
  );
}

/** A d20's face, with the number rolled. */
function D20({ value }: { value: number }) {
  return (
    <span class="tx-d20" aria-hidden="true">
      <svg viewBox="-50 -50 100 100">
        <path class="rim" d="M0 -46L40 -23V23L0 46L-40 23V-23Z" />
        <path class="face" d="M0 -26L23 14H-23Z" />
        <path class="edge" d="M0 -46L0 -26M40 -23L23 14M40 23L23 14M0 46L23 14M0 46L-23 14M-40 23L-23 14M-40 -23L-23 14M-40 -23L0 -26M40 -23L0 -26" />
      </svg>
      <b>{value}</b>
    </span>
  );
}

function RollPanel({ roll, by }: { roll: CheckRoll; by: CheckCard | undefined }) {
  const sfx = useSfx();
  useEffect(() => sfx("dice"), [roll.member, roll.die]);
  const verdict = roll.success ? "Success" : "Failure";
  return (
    <div class={`tx-roll ${roll.success ? "ok" : "bad"}`} role="status"
      aria-label={`${by?.name ?? "The party"} rolled ${roll.die}, ${signed(roll.bonus)}, ${roll.total} against DC ${roll.dc}: ${verdict}`}>
      <D20 value={roll.die} />
      <div class="tx-roll-text">
        <p class="tx-roll-sum">
          {roll.die} {roll.bonus < 0 ? "−" : "+"} {Math.abs(roll.bonus)} = <b>{roll.total}</b> <small>vs DC</small> {roll.dc}
        </p>
        <p class="tx-roll-verdict">{verdict}</p>
        <p class="tx-note">{`${by?.name ?? "The party"} rolled`}</p>
      </div>
    </div>
  );
}

/** The roll, and the card of who rolled it. */
function Rolled({ roll, by }: { roll: CheckRoll; by: CheckCard | undefined }) {
  return (
    <>
      <RollPanel roll={roll} by={by} />
      {by ? <div class="tx-rcards one"><div class="tx-rcard on" role="group" aria-label={`${by.name}, ${signed(by.bonus)}, ${by.note}`}><CardBody card={by} tag="Rolled" /></div></div> : null}
    </>
  );
}

/**
 * A skill check. First the party votes on who rolls, from cards like mockup 03's: each hero's bonus and where it comes
 * from, and who's away (they can still be picked: their character rolls). On a tie the decider picks among the tied. Then
 * the roll: the die, the sum against the DC, and the verdict.
 */
export function Check({ data, send, viewId }: CustomScreenProps) {
  const d = data as CheckData;
  const sfx = useSfx();
  const alerted = useAlertOnMount("yourTurn", d.canChoose && !d.roll);
  useTieAlert(d.tie, d.canChoose);
  const [tapped, setTapped] = useState<{ id: string; view: string } | null>(null);
  const mine = tapped?.view === viewId ? tapped.id : d.mine;

  const pick = (id: string) => {
    if (id === mine) return;
    setTapped({ id, view: viewId });
    sfx("confirm");
    send("pickRoller", { member: id });
  };

  const roll = d.roll;
  const heading = d.tie
    ? d.canChoose ? "Tied: you break it" : `Tied: ${d.decider ?? "the leader"} breaks it`
    : d.canChoose ? "Who rolls? Vote for one" : "The party is picking who rolls";
  const pickable = (c: CheckCard) => d.canChoose && (!d.tie || d.tied.includes(c.id));
  const tagOf = (c: CheckCard) => (c.id === mine ? "Your pick" : null);

  return (
    <div class={`tx tx-check${roll ? " rolled" : ""}${alerted ? " arrived" : ""}`}>
      <div class="tx-check-head">
        <span class="tx-eyebrow">Skill check</span>
        <h2 class="tx-check-skill">{skillName(d.skill)}</h2>
        <p class="tx-check-for"><span class="tx-dc">DC {d.dc}</span><span>“{d.title}”</span></p>
        {roll ? null : <Clock />}
      </div>
      {roll ? <Rolled roll={roll} by={d.cards.find((c) => c.id === roll.member)} /> : (
        <>
          <h3 class={`tx-sect${d.tie ? " tie" : ""}`}>{heading}</h3>
          <ul class="tx-rcards" aria-label="Who rolls">
            {d.cards.map((c) => {
              const tag = tagOf(c);
              const cls = `tx-rcard${tag ? " on" : ""}${c.here ? "" : " away"}${d.tie && !d.tied.includes(c.id) ? " out" : ""}`;
              const label = `${c.name}, ${signed(c.bonus)}, ${c.note}${c.here ? "" : ", away"}${tag ? `, ${tag.toLowerCase()}` : ""}`;
              return (
                <li key={c.id}>
                  {d.canChoose ? (
                    <button type="button" class={cls} aria-pressed={c.id === mine} aria-label={label} disabled={!pickable(c)}
                      onClick={() => pickable(c) && pick(c.id)}>
                      <CardBody card={c} tag={tag} />
                    </button>
                  ) : (
                    <div class={cls} role="group" aria-label={label}><CardBody card={c} tag={tag} /></div>
                  )}
                </li>
              );
            })}
          </ul>
          <p class="tx-note">{d.canChoose ? "If nobody picks, the best bonus rolls." : "Watch the TV."}</p>
        </>
      )}
    </div>
  );
}
