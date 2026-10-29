import { useEffect, useState } from "preact/hooks";
import { teamColorById } from "../skins/redline";
import { remainingSeconds } from "../widgets/Countdown";
import type { DriverRow, RacePhase, RedlineHostView, RoundOutcome } from "./redlineView";

/** A browser stand-in for the TV's REDLINE screens (the Godot host comes in M3): timing tower, question, awards. */

const TITLES: Partial<Record<RacePhase, string>> = {
  DriverSelect: "Driver select",
  Grid: "Lights out in a moment",
  Throttle: "Choose your throttle",
  Chequered: "Chequered flag",
  Anthem: "National anthem",
  Champagne: "Champagne!",
};

/** The tower's right-hand column heading; Grid and Chequered show only the gap. */
const COLUMN: Record<RacePhase, string> = {
  DriverSelect: "SEAT",
  Grid: "GAP",
  Throttle: "GAP · STATUS",
  Reading: "GAP · THROTTLE",
  Question: "GAP · THROTTLE",
  Reveal: "GAP · RESULT",
  Chequered: "GAP",
  Anthem: "GAP",
  Champagne: "GAP",
};

const OUTCOME: Record<RoundOutcome, string> = { Correct: "✓", Wrong: "✗", NoAnswer: "MISS", SpunOut: "SPIN" };
const DIFFICULTY = { Easy: 1, Medium: 2, Hard: 3 } as const;
const minus = (n: number) => `−${Math.abs(n)}`;
const signed = (n: number) => (n > 0 ? `+${n}` : n < 0 ? minus(n) : "0");
const clock = (s: number) => `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;
/** Reading, Question and Reveal: the question text, the answers and every throttle are public. */
const questionShown = (phase: RacePhase) => phase === "Reading" || phase === "Question" || phase === "Reveal";
const throttleShown = questionShown;

function useNow(active: boolean): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!active) return;
    const id = setInterval(() => setNow(Date.now()), 250);
    return () => clearInterval(id);
  }, [active]);
  return now;
}

function StatusChip({ row, phase }: { row: DriverRow; phase: RacePhase }) {
  if (phase === "Throttle") {
    if (row.status === "Limited") return <span class="chip LIMIT">PIT</span>;
    return row.status === "Choosing" ? <span class="chip WAIT">CHOOSING</span> : <span class="chip LOCKED">LOCKED</span>;
  }
  if (throttleShown(phase) && row.throttle) {
    const name = row.throttle.toUpperCase();
    return <span class={`chip ${name}`}>{name}</span>;
  }
  return null;
}

function TowerRow({ row, view, leader }: { row: DriverRow; view: RedlineHostView; leader: number }) {
  const { phase, finishLine } = view;
  const color = teamColorById(row.teamId) ?? "#888";
  const select = phase === "DriverSelect";
  const gap = leader - row.distance;
  const pct = (d: number) => `${(Math.min(1, Math.max(0, d / finishLine)) * 100).toFixed(1)}%`;
  const moved = phase === "Reveal" ? row.positionChange : 0;
  const cls = [
    "trow",
    row.position === 1 && !select && "leader",
    row.player && "human",
    select && row.player && "claimed",
    row.player && !row.connected && "offline",
    phase === "Question" && row.status === "Answered" && "answered",
  ].filter(Boolean).join(" ");

  return (
    <div class={cls} data-code={row.code}>
      <span class="p">{select ? row.number : row.position}</span>
      <i class="c" style={{ background: color }} />
      <span class="n">
        {row.code}
        {moved ? <span class={`mv ${moved > 0 ? "up" : "down"}`}>{moved > 0 ? `▲${moved}` : `▼${-moved}`}</span> : null}
        {row.pitLimiter && phase !== "Throttle" && !select ? <span class="pit">PIT</span> : null}
      </span>
      <span class="who">{row.playerName ?? "AI"}</span>
      {select ? <span class="dist" /> : (
        <span class="dist">
          <span class="track">
            {phase === "Reveal" ? <i class="ghost" style={{ width: pct(row.previousDistance), background: color }} /> : null}
            <i class="fill" style={{ width: pct(row.distance), background: color }} />
          </span>
          <b class="d">{row.distance}</b>
        </span>
      )}
      <span class="gap">{select ? "" : row.position === 1 ? "LEAD" : gap === 0 ? "TIED" : minus(gap)}</span>
      <span class="state">
        <StatusChip row={row} phase={phase} />
        {phase === "Reveal" && row.outcome ? (
          <span class={`outcome ${row.outcome}`}>{OUTCOME[row.outcome]} {signed(row.distance - row.previousDistance)}</span>
        ) : null}
      </span>
    </div>
  );
}

export function RedlineBoard({ view }: { view: RedlineHostView }) {
  const now = useNow(!!view.deadlineUtc);
  const { phase } = view;
  // The question belongs to the round phases; Grid and Chequered show none even if the view still carries one.
  const question = phase === "Throttle" || questionShown(phase) ? view.question : null;
  const leader = view.drivers.reduce((m, d) => Math.max(m, d.distance), 0);
  const humans = view.drivers.filter((d) => d.player);
  const title = questionShown(phase) && question?.text ? question.text
    : phase === "Anthem" && view.podium ? `National anthem · ${view.podium.anthemCountry}`
    : TITLES[phase] ?? phase;
  const roundLabel = view.round > 0 ? `ROUND ${view.round}/${view.maxRounds}` : `${view.circuitId.toUpperCase()} · ${view.maxRounds} ROUNDS`;
  const left = view.deadlineUtc ? remainingSeconds(view.deadlineUtc, now) : null;
  const decided =
    phase === "Throttle" ? humans.filter((d) => d.status !== "Choosing").length
      : phase === "Question" ? humans.filter((d) => d.status === "Answered").length
      : null;

  return (
    <div class={`rboard phase-${phase}`}>
      <header class="rb-head">
        <div class="brand">REDLINE</div>
        <div class="qpanel">
          <div class="qmeta">
            <span>{roundLabel}</span>
            {question ? <span>{question.category}</span> : null}
            {question ? (
              <span class="diff">
                {question.difficulty}
                {[1, 2, 3].map((n) => <i key={n} class={n <= DIFFICULTY[question.difficulty] ? "" : "off"} />)}
              </span>
            ) : null}
          </div>
          <div class="q">{title}</div>
        </div>
        {phase === "Reading" ? (
          // The question is being read out; the answer clock starts when it opens.
          <div class="clock listen">
            <small>ANSWERS NEXT</small>
            <b>LISTEN</b>
          </div>
        ) : (
          <div class="clock">
            <small>{phase === "Reveal" ? "NEXT" : "TIME"}</small>
            <b>{left === null ? "–" : clock(left)}</b>
          </div>
        )}
      </header>

      <div class="rb-body">
        <section class="tower">
          <div class="th">
            <span>POS · DRIVER</span>
            <span>{COLUMN[phase]}</span>
          </div>
          <div class="rows">
            {view.drivers.map((row) => <TowerRow key={row.code} row={row} view={view} leader={leader} />)}
          </div>
        </section>

        <aside class="rb-side">
          {question?.answers ? (
            <div class="answers">
              {question.answers.map((a, i) => {
                const reveal = phase === "Reveal" && view.correctIndex !== null;
                const cls = ["ans", reveal && (i === view.correctIndex ? "right" : "wrong")].filter(Boolean).join(" ");
                const pickers = phase === "Reveal" ? view.drivers.filter((d) => d.answerIndex === i) : [];
                return (
                  <div class={cls} key={i}>
                    <span class="k">{String.fromCharCode(65 + i)}</span>
                    <span class="a">{a}</span>
                    {pickers.length ? (
                      <span class="pickers" title={pickers.map((p) => p.code).join(" ")}>
                        {pickers.map((p) => <i key={p.code} class={p.player ? "h" : ""} style={{ background: teamColorById(p.teamId) ?? "#888" }} />)}
                      </span>
                    ) : null}
                  </div>
                );
              })}
            </div>
          ) : null}

          {phase === "Throttle" ? (
            <div class="legend">
              {/* Numbers only from the view's payouts (server RedlineRules); a view without them gets the words alone. */}
              {view.payouts ? (
                <>
                  <span class="chip CRUISE">CRUISE {signed(view.payouts.cruiseGain)} / 0</span>
                  <span class="chip PUSH">PUSH {signed(view.payouts.pushGain)} / {signed(-view.payouts.pushLoss)}</span>
                  <span class="chip REDLINE">REDLINE {signed(view.payouts.redlineGain)} / {signed(-view.payouts.redlineLoss)} · spin</span>
                </>
              ) : (
                <>
                  <span class="chip CRUISE">CRUISE · safe</span>
                  <span class="chip PUSH">PUSH · lose ground if wrong</span>
                  <span class="chip REDLINE">REDLINE · spin out if wrong</span>
                </>
              )}
            </div>
          ) : null}

          {decided !== null && humans.length ? (
            <div class="summary">
              {decided} of {humans.length} {phase === "Throttle" ? "locked" : "answered"}
              {phase === "Throttle" ? " · the question drops when all drivers are in" : ""}
            </div>
          ) : null}

          {phase === "DriverSelect" ? (
            <div class="summary">{view.drivers.filter((d) => !d.player).length} seats left · AI takes the rest</div>
          ) : null}

          {(phase === "Anthem" || phase === "Champagne") && view.podium ? (
            <div class="podium">
              {view.podium.steps.map((s) => {
                const color = teamColorById(s.teamId) ?? "#888";
                const pct = `${Math.round(s.charge * 100)}%`;
                return (
                  <div class="step" key={s.player} style={{ borderLeftColor: color }}>
                    <small>STEP {s.step} · P{s.racePosition} · {s.code}</small>
                    <b>{s.name}</b>
                    <span class="gauge" title={`pressure ${pct}`}><i style={{ width: pct, background: color }} /></span>
                    <span>{pct} · {s.sprays.length} {s.sprays.length === 1 ? "spray" : "sprays"}</span>
                  </div>
                );
              })}
            </div>
          ) : null}

          {phase === "Chequered" && view.finish ? (
            <div class="awards">
              {view.finish.awards.map((a) => (
                <div class="award" key={a.title}>
                  <small>{a.title}</small>
                  <b>{a.name}</b>
                  <span>{a.detail}</span>
                </div>
              ))}
            </div>
          ) : null}
        </aside>
      </div>

      <footer class="rb-foot">{view.attribution}</footer>
    </div>
  );
}
