# REDLINE Qualifying — Design Spec

- **Date:** 2026-10-29
- **Status:** Approved in conversation (sections 1–3); written spec awaiting review
- **Builds on:** [REDLINE design](2026-10-25-redline-design.md), [audio design](2026-10-26-redline-audio-design.md), [podium design](2026-10-28-redline-podium-design.md)

## 1. Purpose
The grid is a random shuffle today (`RedlineGame.EndDriverSelect`). Replace it with a short qualifying session:
three easy questions, and the field lines up by who answers correctly, fastest.

### Decisions made by the user
| Topic | Decision |
|---|---|
| Reward | **Grid order only.** Every car still starts at distance 0; qualifying sets the grid slots and the tie-break order (`Ranking` is a stable sort over the standings). No distance head start, so the race balance is unchanged. |
| Wrong answer | **Lap deleted.** A wrong or missing answer scores no time for that question. |
| Format | **Three laps, best total.** Everyone answers all three; rank by correct count, then total time of the correct laps. |
| Timing | **Phone-timed, server-clamped.** The phone measures from answers-on-screen to tap; the server clamps it against its own clock. |

## 2. Rules and flow
Driver Select → **QualifyingReading → QualifyingQuestion → QualifyingResult** (× `QualifyingLaps`) → Grid → round 1.

| Phase | Duration | Ends early |
|---|---|---|
| `QualifyingReading` | `RedlineRules.ReadingTime(text)` (2.5–10 s), plus `QualifyingIntro` (3.5 s) on lap 1 for the opener (§6); skipped when `ReadQuestions` is off | no |
| `QualifyingQuestion` | `RedlineRules.QualifyingAnswer` = 10 s | when every connected player has answered |
| `QualifyingResult` | `RedlineRules.QualifyingResult` = 5 s | no |

New `RedlineRules`: `Qualify` (bool, default **true**), `QualifyingLaps` = 3, `QualifyingAnswer` = 10 s,
`QualifyingResult` = 5 s. `Qualify = false`, or a room with no players, keeps today's seeded shuffle and goes straight
to Grid.

### 2.1 Timing
- `ChoiceInput.ElapsedMs` (§3) is the phone's measurement `t`. The server's own measurement `s` = receive time −
  the `QualifyingQuestion` phase start (`ctx.Now`).
- Lap time = `clamp(t, max(200 ms, s − 1000 ms), s)`; with no `t`, `s`.
  - The upper bound stops a phone from claiming more time than passed.
  - The lower bound limits a modified client or a mid-question page reload (which restarts the phone's clock) to
    1 s of gain, while still absorbing ordinary Wi-Fi delay on the way in.
- Times are whole milliseconds. A player's first answer per lap counts; later inputs are ignored. An answer that
  doesn't parse to one of the four options is ignored.
- A disconnected player who doesn't answer gets no time for that lap; they keep their seat.

### 2.2 Classification
- Per lap: correct → lap time; wrong or none → **deleted** (no time).
- Order: `Correct` desc, then `TotalMs` asc (sum of correct laps). Exact ties: the seeded random order drawn when
  qualifying starts (the one today's shuffle would have produced), so the result stays deterministic.
- After the last `QualifyingResult` the classification becomes `_standings` and each driver's `Position`, then Grid.
- `FastestLap` per lap = the lowest correct lap time across the field (ties: the better-classified car).

### 2.3 AI cars
- Correct with `AiPolicy.Accuracy(aiSkill, Easy, rules.Ai)` (0.61–0.83 over the grid's 0.85–1.15 skills).
- Time: log-normal, median from 3.6 s at skill 0.85 to 1.9 s at 1.15 (linear in skill), σ = 0.25; clamped to
  [0.6 s, `QualifyingAnswer`]; a time at the window's end counts as no answer.
- Drawn from `ctx.Random` in team order at `QualifyingQuestion` start, then hidden until Result like everyone else.
- New `AiRules` entries hold these numbers (`QualiSlowMedian`, `QualiFastMedian`, `QualiSigma`) so the sim can tune them.

### 2.4 Questions
- **Easy only**, taken before the race's 15, never repeated in the same game.
- `IQuestionSource` gains `Take(int count, Difficulty difficulty)`, a default interface method that returns
  `Take(count)` for sources that can't filter (test fakes keep working).
  - `PooledQuestionSource` overrides it.
    - It asks `OpenTdbQuestionPool.TryTake(count, into, difficulty)`, which scans at most `Count` items, keeps the
      matches, and re-enqueues the rest at the tail. The method stays thread-safe and non-blocking.
    - The shortfall comes from the fallback pack's easy questions (20 today), on the same shuffled cursor.
  - `NarratedQuestionSource` passes through and prewarms the narration as now.
  - `SyntheticQuestionSource` (sim) returns easy.
- The race keeps its mixed pool. Same contract: synchronous, never throws, exactly `count` questions.

## 3. SDK: time to answer
```csharp
public sealed record ChoiceInput(IReadOnlyList<string> Selected, double? ElapsedMs = null);
```
- Optional, serialised `elapsedMs`; older clients omit it. Any game may use it; games that don't, ignore it.
- C#↔TS contract fixture gains a case with `elapsedMs`.

## 4. Phone
### 4.1 `Choice` widget (generic)
- Start: the first frame that shows the options — a double `requestAnimationFrame` after the view mounts, read with
  `performance.now()` (monotonic). If the page is hidden then, start on the first frame after it becomes visible.
- The start is kept per `viewId` (`useViewState`), so re-renders don't restart it; a reload does (bounded by §2.1).
- Every Choice sends `elapsedMs` (single tap, or "Lock in" for multiple choice).

### 4.2 REDLINE views
| Phase | View |
|---|---|
| QualifyingReading | `WaitView`: "QUALIFYING · LAP n/3", the question, "Answers in a moment…" |
| QualifyingQuestion | `ChoiceView`: same header, the question, four options |
| Answered | `WaitView`: "Lap time **2.41 s** — checking…" (the clamped time) |
| QualifyingResult | `WaitView`: "✔ 2.41 s · P3 so far" (with "Purple!" when `FastestLap`), or "✖ Lap deleted · P14 so far"; existing correct/wrong feedback |
| Grid | existing message + "Qualified P4" |

## 5. Host view
```csharp
// RedlineHostView: new last parameter
Qualifying? Qualifying = null);   // the three Qualifying phases only

public sealed record Qualifying(int Lap, int Laps, IReadOnlyList<QualifyingRow> Rows);
public sealed record QualifyingRow(string Code, int Position, int PositionChange,
    int Correct, int TotalMs,
    int? LastLapMs,        // QualifyingResult only; null = deleted or no answer
    bool Answered,         // live tick during QualifyingQuestion, no time shown
    bool FastestLap);      // purple
```
- `Question` and `CorrectIndex` are filled as in the race (CorrectIndex in QualifyingResult only).
- `Drivers` are sorted by the qualifying classification; `Round` = 0.
- No time or correctness is exposed before QualifyingResult.

## 6. TV
- **Timing tower** in qualifying mode: POS · DRIVER · LAP · TOTAL; purple fastest lap; red "DELETED"; answered ticks
  during the question; rows slide on Result.
- **Question panel** reused, header "QUALIFYING · LAP n/3", with LISTEN during the read-out.
- **Camera:** existing trackside and helicopter shots following the provisional pole car. On Result, a
  "PROVISIONAL POLE · {name}" card when the leader changes. After the last lap, "POLE POSITION" (name, team colour,
  total), then Grid with the cars in their new slots.
- **Track map** hidden during qualifying.
- **Audio:**
  - Questions via `redline.questions`.
  - Commentary via `redline.announcer`, with names through `Commentary.SpokenName`:
    - session start: "Qualifying! Three questions, fastest correct answers take pole." Said first: lap 1's question waits
      behind it rather than cutting it off (with `ReadQuestions` off it is said as lap 1's question opens).
    - purple lap: "{Name} goes purple!"
    - leader change: "Provisional pole for {Name}."
    - end: "Pole position for {Name}, {total} seconds in total!"
  - Effects: existing whoosh, sting and buzz; a new synthesised `purple` chime.
- **Demo:** `--redline-demo=qualifying` scripts a session with a deleted lap and a pole change.

## 7. Testing
- **Logic:**
  - phase flow and early advance;
  - `Qualify` off and AI-only rooms skip;
  - clamp bounds (no `t`, `t` too small, `t` > `s`);
  - first answer counts;
  - deleted laps;
  - classification order and seeded tie-break;
  - `FastestLap`;
  - grid = classification;
  - nothing revealed before Result;
  - the race's 15 questions untouched.
- **AI:** deterministic with a seed; medians and accuracy within tolerance over many draws. The balance sim reports
  how often the best of three players takes pole against the AI.
- **Questions:**
  - easy-only from the pool, with tail re-enqueue and no duplicates under concurrent takes;
  - fallback fill;
  - the default interface method.
- **SDK and web:**
  - `ChoiceInput` serialisation and contract fixture;
  - vitest for the paint-time start, hidden-page start, `viewId` persistence and `elapsedMs` on single and multiple
    choice.
- **TV:**
  - demo screenshots of the tower, provisional pole and pole cards;
  - `tools/tv-e2e.sh` bots answer at different speeds (one wrong);
  - the run asserts the qualifying phases, the pole line in the AUDIO log, and that the three bots' grid order matches their
    qualifying times.
