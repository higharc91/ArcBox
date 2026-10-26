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
};

const OUTCOME: Record<RoundOutcome, string> = { Correct: "✓", Wrong: "✗", NoAnswer: "–", SpunOut: "SPIN" };
const DIFFICULTY = { Easy: 1, Medium: 2, Hard: 3 } as const;
const minus = (n: number) => `−${Math.abs(n)}`;
const signed = (n: number) => (n > 0 ? `+${n}` : n < 0 ? minus(n) : "0");
const clock = (s: number) => `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;
const throttleShown = (phase: RacePhase) => phase === "Question" || phase === "Reveal";

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
        {row.pitLimiter && phase !== "Throttle" && !select ? <span class="pit">PIT</span> : null}
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
  const { phase, question } = view;
  const leader = view.drivers.reduce((m, d) => Math.max(m, d.distance), 0);
  const humans = view.drivers.filter((d) => d.player);
  const title = (phase === "Question" || phase === "Reveal") && question?.text ? question.text : TITLES[phase] ?? phase;
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
        <div class="clock">
          <small>{phase === "Reveal" ? "NEXT" : "TIME"}</small>
          <b>{left === null ? "–" : clock(left)}</b>
        </div>
      </header>

      <div class="rb-body">
        <section class="tower">
          <div class="th">
            <span>POS · DRIVER</span>
            <span>{phase === "DriverSelect" ? "SEAT" : phase === "Reveal" ? "GAP · RESULT" : phase === "Throttle" ? "GAP · STATUS" : "GAP · THROTTLE"}</span>
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
              <span class="chip CRUISE">CRUISE +1 / 0</span>
              <span class="chip PUSH">PUSH +2 / −1</span>
              <span class="chip REDLINE">REDLINE +4 / SPIN</span>
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
