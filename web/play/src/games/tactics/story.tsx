import { useEffect, useRef, useState } from "preact/hooks";
import type { CustomScreenProps } from "../module";
import { MoodPortrait, Portrait } from "./art";
import { useAlertOnMount, useSfx } from "./sfx";
import type { CheckCard, CheckData, CheckRoll, DialogueData, StoryOption, VoteData } from "./types";
import { className, LineIcon, useShellClock } from "./ui";
import { HostMenu } from "./walk";

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

/**
 * The VIP's End session in a scene, walk's menu with its confirm. It heads the screen, above the words and the question, so it's
 * never where the thumb that skips a line or answers a question is.
 */
function StoryHost({ vip, send }: { vip: boolean; send: CustomScreenProps["send"] }) {
  return vip ? <HostMenu canBattle={false} keep="Keep playing" send={send} /> : null;
}

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
      <StoryHost vip={d.vip} send={send} />
      {d.title ? <p class="tx-story-title"><span class="tx-eyebrow">The story</span><b>{d.title}</b></p> : null}
      {/* Live, and never remounted, so a screen reader says each new line as it comes. */}
      <div class="tx-say-live" aria-live="polite">
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
          <div class="tx-say quiet">
            <p class="tx-note center">The story goes on. Watch the TV.</p>
          </div>
        )}
      </div>
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

/** A ribbon on an option or a card: yours, the party's, or (on a tie-break) your vote from before the tie. */
interface Tag { text: string; earlier?: boolean }

function OptionBody({ option, letter, tag }: { option: StoryOption; letter: string; tag: Tag | null }) {
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
      {tag ? <span class={`tx-opt-tag${tag.earlier ? " earlier" : ""}`}>{tag.text}</span> : null}
    </>
  );
}

/**
 * A question's screen is keyed by what it asks (`voteKey`, `checkKey`), so a new question is a new screen: it alerts,
 * and nothing tapped on the one before carries over to it. A tie-break is a new question too, so a tap held from the
 * open round is dropped, and the decider breaks the tie from the tie-break's own view.
 */
const questionKey = (kind: string, tie: boolean, parts: string[]) => [kind, tie ? "tie" : "open", ...parts].join("\n");
/** Option ids repeat across a story's questions, so the words go in too. */
const voteKey = (d: VoteData) => questionKey(d.party ? "vote" : "choice", d.tie, d.options.map((o) => `${o.id}=${o.label}`));
/**
 * A check is what it tests (skill, DC, the option's words), and whether it's rolled or tied; not who can roll it. Someone getting a
 * character mid-check adds a card, and that mustn't remount every phone's check (an alert again, a tap in flight dropped).
 */
const checkKey = (d: CheckData) => questionKey(d.roll ? "rolled" : "check", d.tie, [d.skill, String(d.dc), d.title]);

/** How long a tap held behind one already sent waits for the view that answers it before it goes anyway. */
export const QUEUE_MS = 1500;

/**
 * An answer to a question (a vote, or a roller pick) as this phone sees it, and a way to tap one.
 *
 * - A tap shows as yours at once. A second tap before the server's view comes back would go out on a view the server
 *   has already moved past, and be dropped, so it waits for that view (or `QUEUE_MS`, if none comes) and goes out on
 *   it then. The latest tap wins.
 * - On a tie-break, the server's `mine` is still this player's vote from the open round. It's shown as `earlier`, not as
 *   this question's answer, until they tap: any tied option they tap goes out, their earlier vote included.
 */
function useAnswer(viewId: string, serverMine: string | null, tie: boolean, post: (id: string) => void) {
  const sfx = useSfx();
  const out = useRef(post);
  out.current = post;
  const [local, setLocal] = useState<string | null>(null);
  const inflight = useRef<string | null>(null);
  const queued = useRef<string | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  // A tie-break is its own screen (`questionKey`), so this is whether this tie-break has been answered yet.
  const answered = useRef(false);
  const fresh = tie && !answered.current;
  const mine = local ?? (fresh ? null : serverMine);
  const earlier = fresh && !local ? serverMine : null;

  const go = (id: string, view: string) => {
    inflight.current = view;
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(() => {
      timer.current = null;
      inflight.current = null;
      const next = queued.current;
      queued.current = null;
      if (next) go(next, view);
    }, QUEUE_MS);
    out.current(id);
  };

  // A new view: the server has heard the last tap. One held behind it goes now, unless the view already says so.
  const latest = useRef(serverMine);
  latest.current = serverMine;
  useEffect(() => {
    if (timer.current) clearTimeout(timer.current);
    timer.current = null;
    inflight.current = null;
    const next = queued.current;
    queued.current = null;
    if (next && next !== latest.current) go(next, viewId);
    else setLocal(null);
  }, [viewId]);
  useEffect(() => () => {
    if (timer.current) clearTimeout(timer.current);
  }, []);

  const tap = (id: string) => {
    if (id === mine) return;
    answered.current = true;
    setLocal(id);
    sfx("confirm");
    if (inflight.current === viewId) queued.current = id;
    else go(id, viewId);
  };
  return { mine, earlier, tap };
}

/**
 * A question: the party's vote, or the leader's choice for everyone. Options are big tap targets; a tap answers at once
 * and shows as yours before the server's view comes back, and stays changeable until the clock runs out. Those who
 * can't answer see the options read-only, with the party's choice when there is one.
 */
export function Vote(props: CustomScreenProps) {
  const key = voteKey(props.data as VoteData);
  return <VoteQuestion key={key} question={key} {...props} />;
}

function VoteQuestion({ data, send, viewId, question }: CustomScreenProps & { question: string }) {
  const d = data as VoteData;
  const alerted = useAlertOnMount("yourTurn", question, d.canChoose);
  const { mine, earlier, tap: pick } = useAnswer(viewId, d.mine, d.tie, (id) => send("vote", { option: id }));

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
  const tagOf = (id: string): Tag | null =>
    id === mine ? { text: d.party ? "Your vote" : "Your choice" }
      : id === earlier ? { text: "You voted this earlier", earlier: true }
        : !mine && id === d.chosen ? { text: "The party's choice" } : null;

  return (
    <div class={`tx tx-ask${alerted ? " arrived" : ""}`}>
      <StoryHost vip={d.vip} send={send} />
      <AskHead eyebrow={eyebrow} title={title} tie={d.tie} />
      <ul class={`tx-options${d.canChoose ? "" : " static"}`} aria-label="Options">
        {d.options.map((o, i) => {
          const tag = tagOf(o.id);
          const on = tag !== null && !tag.earlier;
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

function CardBody({ card, tag }: { card: CheckCard; tag: Tag | null }) {
  return (
    <>
      {tag ? <span class={`tx-rcard-tag${tag.earlier ? " earlier" : ""}`}>{tag.text}</span> : !card.here ? <span class="tx-rcard-tag away">Away</span> : null}
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

function RollPanel({ roll, by, question }: { roll: CheckRoll; by: CheckCard | undefined; question: string }) {
  // A reconnect re-sends the rolled view: its remount stays quiet and still, as an alert's does.
  const fresh = useAlertOnMount("dice", question);
  const verdict = roll.success ? "Success" : "Failure";
  return (
    <div class={`tx-roll ${roll.success ? "ok" : "bad"}${fresh ? " fresh" : ""}`} role="status"
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
function Rolled({ roll, by, question }: { roll: CheckRoll; by: CheckCard | undefined; question: string }) {
  return (
    <>
      <RollPanel roll={roll} by={by} question={question} />
      {by ? <div class="tx-rcards one"><div class="tx-rcard on" role="group" aria-label={`${by.name}, ${signed(by.bonus)}, ${by.note}`}><CardBody card={by} tag={{ text: "Rolled" }} /></div></div> : null}
    </>
  );
}

/**
 * A skill check. First the party votes on who rolls, from cards like mockup 03's: each hero's bonus and where it comes
 * from, and who's away (they can still be picked: their character rolls). On a tie the decider picks among the tied. Then
 * the roll: the die, the sum against the DC, and the verdict.
 */
export function Check(props: CustomScreenProps) {
  const key = checkKey(props.data as CheckData);
  return <CheckQuestion key={key} question={key} {...props} />;
}

function CheckQuestion({ data, send, viewId, question }: CustomScreenProps & { question: string }) {
  const d = data as CheckData;
  const alerted = useAlertOnMount("yourTurn", question, d.canChoose && !d.roll);
  const { mine, earlier, tap: pick } = useAnswer(viewId, d.mine, d.tie, (id) => send("pickRoller", { member: id }));

  const roll = d.roll;
  const heading = d.tie
    ? d.canChoose ? "Tied: you break it" : `Tied: ${d.decider ?? "the leader"} breaks it`
    : d.canChoose ? "Who rolls? Vote for one" : "The party is picking who rolls";
  const pickable = (c: CheckCard) => d.canChoose && (!d.tie || d.tied.includes(c.id));
  const tagOf = (c: CheckCard): Tag | null =>
    c.id === mine ? { text: "Your pick" } : c.id === earlier ? { text: "Your pick earlier", earlier: true } : null;

  return (
    <div class={`tx tx-check${roll ? " rolled" : ""}${alerted ? " arrived" : ""}`}>
      <StoryHost vip={d.vip} send={send} />
      <div class="tx-check-head">
        <span class="tx-eyebrow">Skill check</span>
        <h2 class="tx-check-skill">{skillName(d.skill)}</h2>
        <p class="tx-check-for"><span class="tx-dc">DC {d.dc}</span><span>“{d.title}”</span></p>
        {roll ? null : <Clock />}
      </div>
      {roll ? <Rolled roll={roll} by={d.cards.find((c) => c.id === roll.member)} question={question} /> : (
        <>
          <h3 class={`tx-sect${d.tie ? " tie" : ""}`}>{heading}</h3>
          <ul class="tx-rcards" aria-label="Who rolls">
            {d.cards.map((c) => {
              const tag = tagOf(c);
              const cls = `tx-rcard${tag && !tag.earlier ? " on" : ""}${c.here ? "" : " away"}${d.tie && !d.tied.includes(c.id) ? " out" : ""}`;
              const label = `${c.name}, ${signed(c.bonus)}, ${c.note}${c.here ? "" : ", away"}${tag ? `, ${tag.text.toLowerCase()}` : ""}`;
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
